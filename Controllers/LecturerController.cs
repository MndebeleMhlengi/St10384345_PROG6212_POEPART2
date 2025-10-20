using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using CMCS.Data;
using CMCS.Models;
using CMCS.Models.ViewModels;
using System.Security.Claims;
using Claim = CMCS.Models.Claim;

namespace CMCS.Controllers
{
    [Authorize(Roles = "LECTURER")]
    public class LecturerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LecturerController> _logger;

        public LecturerController(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<LecturerController> logger = null)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var user = await _context.Users.FindAsync(userId);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found. Please log in again.";
                    return RedirectToAction("Login", "Account");
                }

                var recentClaims = await _context.Claims
                    .Where(c => c.LecturerId == userId)
                    .OrderByDescending(c => c.SubmissionDate)
                    .Take(10)
                    .ToListAsync();

                var allUserClaims = _context.Claims.Where(c => c.LecturerId == userId);

                ViewBag.User = user;
                ViewBag.PendingClaims = await allUserClaims.CountAsync(c => c.Status == ClaimStatus.PENDING);
                ViewBag.ApprovedClaims = await allUserClaims.CountAsync(c => c.Status == ClaimStatus.APPROVED_FINAL);
                ViewBag.TotalClaims = await allUserClaims.CountAsync();

                return View(recentClaims);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dashboard error for user");
                TempData["ErrorMessage"] = "Error loading dashboard. Please try again.";
                return View(new List<Claim>());
            }
        }

        public async Task<IActionResult> SubmitClaim()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var user = await _context.Users.FindAsync(userId);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found. Please log in again.";
                    return RedirectToAction("Dashboard");
                }

                var model = new ClaimSubmissionViewModel
                {
                    HourlyRate = user.HourlyRate,
                    YearWorked = DateTime.Now.Year,
                    MonthWorked = DateTime.Now.Month
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SubmitClaim GET error");
                TempData["ErrorMessage"] = "Error loading claim form. Please try again.";
                return RedirectToAction("Dashboard");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitClaim(ClaimSubmissionViewModel model)
        {
            var userId = 0;
            try
            {
                userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var user = await _context.Users.FindAsync(userId);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found. Please log in again.";
                    return RedirectToAction("Login", "Account");
                }

                // REMOVE THIS LINE - it was overriding user input with profile rate
                // model.HourlyRate = user.HourlyRate;

                // Remove ModelState validation for HourlyRate since we're not overriding it anymore
                ModelState.Remove("HourlyRate");

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                    _logger?.LogWarning($"ModelState invalid: {string.Join(", ", errors)}");
                    return View(model);
                }

                // Validate inputs
                if (model.HoursWorked <= 0 || model.HoursWorked > 500)
                {
                    ModelState.AddModelError("HoursWorked", "Hours worked must be between 0.1 and 500.");
                    return View(model);
                }

                // ADD VALIDATION FOR HOURLY RATE
                if (model.HourlyRate <= 0 || model.HourlyRate > 100000)
                {
                    ModelState.AddModelError("HourlyRate", "Hourly rate must be between 0.01 and 100,000.");
                    return View(model);
                }

                if (string.IsNullOrWhiteSpace(model.ModuleTaught))
                {
                    ModelState.AddModelError("ModuleTaught", "Module taught is required.");
                    return View(model);
                }

                if (model.MonthWorked < 1 || model.MonthWorked > 12)
                {
                    ModelState.AddModelError("MonthWorked", "Please select a valid month.");
                    return View(model);
                }

                if (model.YearWorked < 2020 || model.YearWorked > 2030)
                {
                    ModelState.AddModelError("YearWorked", "Year must be between 2020 and 2030.");
                    return View(model);
                }

                // Trim inputs
                var moduleTaught = model.ModuleTaught.Trim();
                var additionalNotes = string.IsNullOrWhiteSpace(model.AdditionalNotes)
                    ? null
                    : model.AdditionalNotes.Trim();

                // Check for duplicate using case-insensitive comparison
                var hasDuplicate = await _context.Claims
                    .AnyAsync(c =>
                        c.LecturerId == userId &&
                        c.MonthWorked == model.MonthWorked &&
                        c.YearWorked == model.YearWorked &&
                        c.ModuleTaught.ToLower() == moduleTaught.ToLower() &&
                        c.IsActive);

                if (hasDuplicate)
                {
                    var monthName = new DateTime(model.YearWorked, model.MonthWorked, 1).ToString("MMMM yyyy");
                    ModelState.AddModelError("", $"You have already submitted a claim for {moduleTaught} in {monthName}. Please view your existing claims or submit for a different period/module.");
                    return View(model);
                }

                // Generate unique reference (FIXED: Shorter format to fit 20 characters)
                var timestamp = DateTime.Now.ToString("yyMMddHHmm"); // 10 characters: YYMMDDHHMM
                var uniqueId = Guid.NewGuid().ToString().Substring(0, 4).ToUpper(); // 4 characters
                var claimReference = $"CLM-{timestamp}-{uniqueId}"; // Total: 4 + 10 + 1 + 4 = 19 characters

                // Ensure claim reference doesn't exceed column length
                if (claimReference.Length > 20)
                {
                    claimReference = claimReference.Substring(0, 20);
                    _logger?.LogWarning($"Claim reference truncated to 20 characters: {claimReference}");
                }


                var claim = new Claim
                {
                    LecturerId = userId,
                    MonthWorked = model.MonthWorked,
                    YearWorked = model.YearWorked,
                    HoursWorked = model.HoursWorked,
                    HourlyRate = model.HourlyRate, 
                    ModuleTaught = moduleTaught,
                    AdditionalNotes = additionalNotes,
                    Status = ClaimStatus.PENDING,
                    SubmissionDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    ClaimReference = claimReference,
                    IsActive = true
                };

                // Calculate total amount using the rate from the form
                claim.TotalAmount = claim.HoursWorked * claim.HourlyRate;

                // Log the claim details
                _logger?.LogInformation($"Creating claim: User={userId}, Month={model.MonthWorked}, Year={model.YearWorked}, Hours={model.HoursWorked}, Rate={model.HourlyRate}, Module={moduleTaught}, Reference={claimReference}");

                _context.Claims.Add(claim);

                // Save with error handling
                try
                {
                    await _context.SaveChangesAsync();
                    _logger?.LogInformation($"Claim saved successfully with ID: {claim.ClaimId}");
                }
                catch (DbUpdateException dbEx)
                {
                    var innerException = dbEx.InnerException;
                    var errorMessage = innerException?.Message ?? dbEx.Message;

                    _logger?.LogError(dbEx, $"Database error saving claim: {errorMessage}");

                    // Check for specific constraint violations
                    if (errorMessage.Contains("UNIQUE") || errorMessage.Contains("duplicate") || errorMessage.Contains("IX_"))
                    {
                        ModelState.AddModelError("", "A similar claim already exists in the system. Please check your existing claims.");
                    }
                    else if (errorMessage.Contains("FOREIGN KEY") || errorMessage.Contains("FK_"))
                    {
                        ModelState.AddModelError("", "Invalid reference data. Please refresh the page and try again.");
                    }
                    else if (errorMessage.Contains("NULL") || errorMessage.Contains("required"))
                    {
                        ModelState.AddModelError("", "Missing required information. Please ensure all required fields are filled.");
                    }
                    else if (errorMessage.Contains("String or binary data would be truncated"))
                    {
                        ModelState.AddModelError("", $"Database error: The claim reference '{claimReference}' is too long. Please try submitting again.");
                    }
                    else
                    {
                        ModelState.AddModelError("", $"Database error: {errorMessage}");
                    }

                    return View(model);
                }

                TempData["SuccessMessage"] = $"Claim submitted successfully! Reference: {claim.ClaimReference}";
                return RedirectToAction("ViewClaims");
            }
            catch (FormatException)
            {
                TempData["ErrorMessage"] = "Invalid user session. Please log in again.";
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Unexpected error in SubmitClaim for user {userId}");

                ModelState.AddModelError("", $"An unexpected error occurred: {ex.Message}. Please contact support if this persists.");

                // Try to reload user data
                try
                {
                    if (userId > 0)
                    {
                        var user = await _context.Users.FindAsync(userId);
                        if (user != null)
                        {
                            // Set the hourly rate back to user's profile rate for the form
                            model.HourlyRate = user.HourlyRate;
                        }
                    }
                }
                catch { }

                return View(model);
            }
        }

        public async Task<IActionResult> ViewClaims()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var claims = await _context.Claims
                    .Where(c => c.LecturerId == userId)
                    .Include(c => c.Documents)
                    .Include(c => c.Approvals)
                        .ThenInclude(a => a.Approver)
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                return View(claims);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ViewClaims error");
                TempData["ErrorMessage"] = "Error loading claims. Please try again.";
                return View(new List<Claim>());
            }
        }

        public IActionResult UploadDocuments(int claimId)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var claim = _context.Claims.FirstOrDefault(c => c.ClaimId == claimId && c.LecturerId == userId);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found or access denied.";
                    return RedirectToAction("ViewClaims");
                }

                ViewBag.ClaimId = claimId;
                ViewBag.ClaimReference = claim.ClaimReference;
                return View();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UploadDocuments GET error");
                TempData["ErrorMessage"] = "Error loading upload page.";
                return RedirectToAction("ViewClaims");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocuments(int claimId, IFormFile file, string description)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    TempData["ErrorMessage"] = "Please select a file to upload.";
                    return RedirectToAction("UploadDocuments", new { claimId });
                }

                var allowedExtensions = new[] { ".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    TempData["ErrorMessage"] = "Only PDF, DOCX, XLSX, JPG, JPEG, and PNG files are allowed.";
                    return RedirectToAction("UploadDocuments", new { claimId });
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    TempData["ErrorMessage"] = "File size cannot exceed 10MB.";
                    return RedirectToAction("UploadDocuments", new { claimId });
                }

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var claim = await _context.Claims
                    .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.LecturerId == userId);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found or access denied.";
                    return RedirectToAction("ViewClaims");
                }

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var document = new Document
                {
                    ClaimId = claimId,
                    FileName = file.FileName,
                    FilePath = fileName,
                    FileType = extension,
                    FileSize = file.Length,
                    Description = description,
                    ContentType = file.ContentType,
                    UploadDate = DateTime.Now,
                    IsVerified = false
                };

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Document uploaded successfully!";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UploadDocuments POST error");
                TempData["ErrorMessage"] = $"An error occurred while uploading the document: {ex.Message}";
            }

            return RedirectToAction("ViewClaims");
        }

        public async Task<IActionResult> GetClaimDetails(int id)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .Include(c => c.Documents)
                    .Include(c => c.Approvals)
                        .ThenInclude(a => a.Approver)
                    .FirstOrDefaultAsync(c => c.ClaimId == id && c.LecturerId == userId);

                if (claim == null)
                {
                    return Content("<div class='alert alert-danger'>Claim not found.</div>");
                }

                return PartialView("_ClaimDetailsPartial", claim);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetClaimDetails error");
                return Content($"<div class='alert alert-danger'>Error loading claim details: {ex.Message}</div>");
            }
        }

        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var document = await _context.Documents
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentId == documentId);

                if (document == null || document.Claim.LecturerId != userId)
                {
                    TempData["ErrorMessage"] = "Document not found or access denied.";
                    return RedirectToAction("ViewClaims");
                }

                var path = Path.Combine(_environment.WebRootPath, "uploads", document.FilePath);
                if (!System.IO.File.Exists(path))
                {
                    TempData["ErrorMessage"] = "File not found on server.";
                    return RedirectToAction("ViewClaims");
                }

                var memory = new MemoryStream();
                using (var stream = new FileStream(path, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;

                return File(memory, document.ContentType, document.FileName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DownloadDocument error");
                TempData["ErrorMessage"] = "Error downloading document.";
                return RedirectToAction("ViewClaims");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var document = await _context.Documents
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.Claim.LecturerId == userId);

                if (document == null)
                {
                    TempData["ErrorMessage"] = "Document not found or access denied.";
                    return RedirectToAction("ViewClaims");
                }

                var filePath = Path.Combine(_environment.WebRootPath, "uploads", document.FilePath);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                _context.Documents.Remove(document);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Document deleted successfully!";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteDocument error");
                TempData["ErrorMessage"] = $"An error occurred while deleting the document: {ex.Message}";
            }

            return RedirectToAction("ViewClaims");
        }
    }
}