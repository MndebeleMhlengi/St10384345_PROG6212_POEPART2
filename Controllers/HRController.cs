using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using CMCS.Data;
using CMCS.Models;
using System.Security.Claims;
using Claim = CMCS.Models.Claim;

namespace CMCS.Controllers
{
    [Authorize(Roles = "HR")]
    public class HRController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HRController> _logger;

        public HRController(ApplicationDbContext context, ILogger<HRController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Dashboard()
        {
            try
            {
                // Get claims ready for payment (final approved claims)
                var readyForPaymentClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.APPROVED_FINAL && c.IsActive)
                    .Include(c => c.Lecturer)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .ToListAsync();

                // Get paid claims
                var paidClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.PAID && c.IsActive)
                    .Include(c => c.Lecturer)
                    .ToListAsync();

                // Get active lecturers
                var activeLecturers = await _context.Users
                    .Where(u => u.Role == UserRole.LECTURER && u.IsActive)
                    .ToListAsync();

                // Calculate statistics
                var totalForPayment = readyForPaymentClaims.Count;
                var totalAmount = readyForPaymentClaims.Sum(c => c.TotalAmount);
                var paidCount = paidClaims.Count;
                var activeLecturerCount = activeLecturers.Count;

                // Get current month/year for "Full This Month" - claims paid in current month
                var currentDate = DateTime.Now;
                var thisMonthPaidClaims = paidClaims
                    .Where(c => c.LastModifiedDate.Month == currentDate.Month &&
                               c.LastModifiedDate.Year == currentDate.Year)
                    .ToList();

                ViewBag.TotalForPayment = totalForPayment;
                ViewBag.TotalAmount = totalAmount;
                ViewBag.PaidClaims = paidCount;
                ViewBag.ActiveLecturers = activeLecturerCount;
                ViewBag.ThisMonthPaid = thisMonthPaidClaims.Count;
                ViewBag.ThisMonthAmount = thisMonthPaidClaims.Sum(c => c.TotalAmount);

                return View(readyForPaymentClaims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading HR dashboard");
                TempData["ErrorMessage"] = "Error loading dashboard. Please try again.";
                return View(new List<Claim>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int claimId)
        {
            try
            {
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.IsActive);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                    return RedirectToAction("Dashboard");
                }

                if (claim.Status != ClaimStatus.APPROVED_FINAL)
                {
                    TempData["ErrorMessage"] = $"This claim is not ready for payment. Current status: {claim.Status}";
                    return RedirectToAction("Dashboard");
                }

                // Update claim status to PAID
                claim.Status = ClaimStatus.PAID;
                claim.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Claim {ClaimId} marked as paid by HR", claimId);
                TempData["SuccessMessage"] = $"Claim {claim.ClaimReference} marked as paid successfully!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking claim {ClaimId} as paid", claimId);
                TempData["ErrorMessage"] = "Error marking claim as paid. Please try again.";
            }

            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkMultipleAsPaid(int[] claimIds)
        {
            try
            {
                if (claimIds == null || claimIds.Length == 0)
                {
                    TempData["ErrorMessage"] = "Please select at least one claim to mark as paid.";
                    return RedirectToAction("Dashboard");
                }

                var claims = await _context.Claims
                    .Where(c => claimIds.Contains(c.ClaimId) &&
                               c.Status == ClaimStatus.APPROVED_FINAL &&
                               c.IsActive)
                    .ToListAsync();

                if (!claims.Any())
                {
                    TempData["ErrorMessage"] = "No valid claims found for payment.";
                    return RedirectToAction("Dashboard");
                }

                foreach (var claim in claims)
                {
                    claim.Status = ClaimStatus.PAID;
                    claim.LastModifiedDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Multiple claims ({Count}) marked as paid by HR", claims.Count);
                TempData["SuccessMessage"] = $"{claims.Count} claims marked as paid successfully!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking multiple claims as paid");
                TempData["ErrorMessage"] = "Error marking claims as paid. Please try again.";
            }

            return RedirectToAction("Dashboard");
        }

        public async Task<IActionResult> ManageLecturers()
        {
            try
            {
                var lecturers = await _context.Users
                    .Where(u => u.Role == UserRole.LECTURER)
                    .OrderBy(u => u.LastName)
                    .ThenBy(u => u.FirstName)
                    .ToListAsync();

                return View(lecturers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading lecturers");
                TempData["ErrorMessage"] = "Error loading lecturers. Please try again.";
                return View(new List<User>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLecturerRate(int lecturerId, decimal hourlyRate)
        {
            try
            {
                if (hourlyRate <= 0 || hourlyRate > 10000)
                {
                    TempData["ErrorMessage"] = "Hourly rate must be between 0.01 and 10,000.";
                    return RedirectToAction("ManageLecturers");
                }

                var lecturer = await _context.Users.FindAsync(lecturerId);
                if (lecturer == null || lecturer.Role != UserRole.LECTURER)
                {
                    TempData["ErrorMessage"] = "Lecturer not found.";
                    return RedirectToAction("ManageLecturers");
                }

                lecturer.HourlyRate = hourlyRate;
                lecturer.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Hourly rate updated to R {hourlyRate:F2} for {lecturer.FirstName} {lecturer.LastName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating lecturer rate for {LecturerId}", lecturerId);
                TempData["ErrorMessage"] = "Error updating hourly rate. Please try again.";
            }

            return RedirectToAction("ManageLecturers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLecturerStatus(int lecturerId)
        {
            try
            {
                var lecturer = await _context.Users.FindAsync(lecturerId);
                if (lecturer == null || lecturer.Role != UserRole.LECTURER)
                {
                    TempData["ErrorMessage"] = "Lecturer not found.";
                    return RedirectToAction("ManageLecturers");
                }

                lecturer.IsActive = !lecturer.IsActive;
                lecturer.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                var status = lecturer.IsActive ? "activated" : "deactivated";
                TempData["SuccessMessage"] = $"Lecturer {lecturer.FirstName} {lecturer.LastName} {status} successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling lecturer status for {LecturerId}", lecturerId);
                TempData["ErrorMessage"] = "Error updating lecturer status. Please try again.";
            }

            return RedirectToAction("ManageLecturers");
        }

        public async Task<IActionResult> PaymentReports(string period = "current-month")
        {
            try
            {
                var paidClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.PAID && c.IsActive)
                    .Include(c => c.Lecturer)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .ToListAsync();

                // Filter by period if needed
                if (period == "current-month")
                {
                    var currentDate = DateTime.Now;
                    paidClaims = paidClaims
                        .Where(c => c.LastModifiedDate.Month == currentDate.Month &&
                                   c.LastModifiedDate.Year == currentDate.Year)
                        .ToList();
                }
                else if (period == "last-month")
                {
                    var lastMonth = DateTime.Now.AddMonths(-1);
                    paidClaims = paidClaims
                        .Where(c => c.LastModifiedDate.Month == lastMonth.Month &&
                                   c.LastModifiedDate.Year == lastMonth.Year)
                        .ToList();
                }

                ViewBag.SelectedPeriod = period;
                ViewBag.TotalPaidAmount = paidClaims.Sum(c => c.TotalAmount);
                ViewBag.TotalPaidCount = paidClaims.Count;

                return View(paidClaims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading payment reports");
                TempData["ErrorMessage"] = "Error loading payment reports. Please try again.";
                return View(new List<Claim>());
            }
        }

        public async Task<IActionResult> ExportPayments(string period = "current-month")
        {
            try
            {
                var paidClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.PAID && c.IsActive)
                    .Include(c => c.Lecturer)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .ToListAsync();

                // Filter by period
                if (period == "current-month")
                {
                    var currentDate = DateTime.Now;
                    paidClaims = paidClaims
                        .Where(c => c.LastModifiedDate.Month == currentDate.Month &&
                                   c.LastModifiedDate.Year == currentDate.Year)
                        .ToList();
                }
                else if (period == "last-month")
                {
                    var lastMonth = DateTime.Now.AddMonths(-1);
                    paidClaims = paidClaims
                        .Where(c => c.LastModifiedDate.Month == lastMonth.Month &&
                                   c.LastModifiedDate.Year == lastMonth.Year)
                        .ToList();
                }

                // Generate CSV content
                var csvContent = "Claim Reference,Lecturer,Month/Year,Hours,Amount,Paid Date\n";
                foreach (var claim in paidClaims)
                {
                    var monthYear = new DateTime(claim.YearWorked, claim.MonthWorked, 1).ToString("MMM yyyy");
                    csvContent += $"\"{claim.ClaimReference}\",\"{claim.Lecturer.FirstName} {claim.Lecturer.LastName}\",\"{monthYear}\",{claim.HoursWorked},{claim.TotalAmount:F2},\"{claim.LastModifiedDate:yyyy-MM-dd}\"\n";
                }

                var fileName = $"payment-report-{DateTime.Now:yyyyMMddHHmmss}.csv";
                return File(System.Text.Encoding.UTF8.GetBytes(csvContent), "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting payment report");
                TempData["ErrorMessage"] = "Error exporting payment report. Please try again.";
                return RedirectToAction("PaymentReports");
            }
        }

        public async Task<IActionResult> LecturerDetails(int id)
        {
            try
            {
                var lecturer = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == id && u.Role == UserRole.LECTURER);

                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer not found.";
                    return RedirectToAction("ManageLecturers");
                }

                // Get lecturer's claims history
                var claims = await _context.Claims
                    .Where(c => c.LecturerId == id)
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                ViewBag.Claims = claims;
                ViewBag.TotalClaims = claims.Count;
                ViewBag.TotalAmount = claims.Where(c => c.Status == ClaimStatus.PAID).Sum(c => c.TotalAmount);

                return View(lecturer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading lecturer details for {LecturerId}", id);
                TempData["ErrorMessage"] = "Error loading lecturer details.";
                return RedirectToAction("ManageLecturers");
            }
        }
    }
}