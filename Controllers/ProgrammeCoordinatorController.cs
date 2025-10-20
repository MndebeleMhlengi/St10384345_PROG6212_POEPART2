using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using CMCS.Data;
using CMCS.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Claim = CMCS.Models.Claim;

namespace CMCS.Controllers
{
    [Authorize(Roles = "PROGRAMME_COORDINATOR")]
    public class ProgrammeCoordinatorController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProgrammeCoordinatorController> _logger;

        public ProgrammeCoordinatorController(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<ProgrammeCoordinatorController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var pendingClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.PENDING && c.IsActive)
                    .Include(c => c.Lecturer)
                    .Include(c => c.Documents)
                    .OrderBy(c => c.SubmissionDate)
                    .ToListAsync();

                ViewBag.TotalPending = pendingClaims.Count;
                ViewBag.TotalApproved = await _context.Claims.CountAsync(c => c.Status == ClaimStatus.APPROVED_PC && c.IsActive);
                ViewBag.TotalRejected = await _context.Claims.CountAsync(c => c.Status == ClaimStatus.REJECTED && c.IsActive);

                return View(pendingClaims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Programme Coordinator dashboard");
                TempData["ErrorMessage"] = "Error loading dashboard. Please try again.";
                return View(new List<Claim>());
            }
        }

        public async Task<IActionResult> ReviewClaim(int id)
        {
            try
            {
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .Include(c => c.Documents)
                    .Include(c => c.Approvals)
                        .ThenInclude(a => a.Approver)
                    .FirstOrDefaultAsync(c => c.ClaimId == id && c.IsActive);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                    return RedirectToAction("Dashboard");
                }

                // Ensure total amount is calculated
                if (claim.TotalAmount == 0)
                {
                    claim.TotalAmount = claim.HoursWorked * claim.HourlyRate;
                }

                return View(claim);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading claim details for claim ID: {ClaimId}", id);
                TempData["ErrorMessage"] = "Error loading claim details.";
                return RedirectToAction("Dashboard");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveClaim(int claimId, string comments)
        {
            try
            {
                _logger.LogInformation("Attempting to approve claim {ClaimId}", claimId);

                // Get current user
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    TempData["ErrorMessage"] = "User not authenticated. Please log in again.";
                    return RedirectToAction("ReviewClaim", new { id = claimId });
                }

                // Get claim with related data
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.IsActive);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                    return RedirectToAction("Dashboard");
                }

                if (claim.Status != ClaimStatus.PENDING)
                {
                    TempData["ErrorMessage"] = $"This claim has already been {claim.Status.ToString().ToLower()}.";
                    return RedirectToAction("ReviewClaim", new { id = claimId });
                }

                // Start transaction for data consistency
                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Update claim status
                    claim.Status = ClaimStatus.APPROVED_PC;
                    claim.LastModifiedDate = DateTime.Now;

                    // Ensure total amount is calculated
                    if (claim.TotalAmount == 0)
                    {
                        claim.TotalAmount = claim.HoursWorked * claim.HourlyRate;
                    }

                    // Create approval record - FIX: Ensure no NULL values
                    var approval = new ClaimApproval
                    {
                        ClaimId = claimId,
                        ApproverId = userId,
                        Level = ApprovalLevel.PROGRAMME_COORDINATOR,
                        Status = ApprovalStatus.APPROVED,
                        Comments = string.IsNullOrWhiteSpace(comments) ?
                            "Claim approved by Programme Coordinator." :
                            comments.Trim(),
                        RejectionReason = string.Empty, // Explicitly set to empty string, not NULL
                        ReviewDate = DateTime.Now,
                        ApprovalDate = DateTime.Now,
                        IsActive = true
                    };

                    _context.ClaimApprovals.Add(approval);

                    // Save changes
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Claim {ClaimId} approved successfully by user {UserId}", claimId, userId);
                    TempData["SuccessMessage"] = $"Claim approved successfully! Reference: {claim.ClaimReference}";

                    return RedirectToAction("Dashboard");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw; // Re-throw to be caught by outer catch block
                }
            }
            catch (DbUpdateException dbEx)
            {
                // Log the inner exception for more details
                var innerException = dbEx.InnerException;
                var errorMessage = innerException?.Message ?? dbEx.Message;

                _logger.LogError(dbEx, "Database error while approving claim {ClaimId}. Error: {ErrorMessage}", claimId, errorMessage);

                // Provide more specific error messages
                if (errorMessage.Contains("RejectionReason") && errorMessage.Contains("NULL"))
                {
                    TempData["ErrorMessage"] = "Database configuration error: RejectionReason column does not allow NULL values. Please contact system administrator.";
                }
                else if (errorMessage.Contains("FK_") || errorMessage.Contains("foreign key"))
                {
                    TempData["ErrorMessage"] = "Database constraint error. Please contact system administrator.";
                }
                else if (errorMessage.Contains("Cannot insert duplicate key"))
                {
                    TempData["ErrorMessage"] = "This claim has already been processed.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Database error: {errorMessage}";
                }

                return RedirectToAction("ReviewClaim", new { id = claimId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while approving claim {ClaimId}", claimId);
                TempData["ErrorMessage"] = $"An unexpected error occurred: {ex.Message}. Please try again or contact support.";
                return RedirectToAction("ReviewClaim", new { id = claimId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectClaim(int claimId, string reason)
        {
            try
            {
                _logger.LogInformation("Attempting to reject claim {ClaimId}", claimId);

                // Get current user
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    TempData["ErrorMessage"] = "User not authenticated. Please log in again.";
                    return RedirectToAction("ReviewClaim", new { id = claimId });
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    TempData["ErrorMessage"] = "Please provide a reason for rejection.";
                    return RedirectToAction("ReviewClaim", new { id = claimId });
                }

                // Get claim with related data
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.IsActive);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                    return RedirectToAction("Dashboard");
                }

                if (claim.Status != ClaimStatus.PENDING)
                {
                    TempData["ErrorMessage"] = $"This claim has already been {claim.Status.ToString().ToLower()}.";
                    return RedirectToAction("ReviewClaim", new { id = claimId });
                }

                // Start transaction
                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Update claim status
                    claim.Status = ClaimStatus.REJECTED;
                    claim.LastModifiedDate = DateTime.Now;

                    // Create approval record - FIX: Ensure proper values
                    var approval = new ClaimApproval
                    {
                        ClaimId = claimId,
                        ApproverId = userId,
                        Level = ApprovalLevel.PROGRAMME_COORDINATOR,
                        Status = ApprovalStatus.REJECTED,
                        Comments = string.Empty, // Explicitly set to empty string
                        RejectionReason = reason.Trim(),
                        ReviewDate = DateTime.Now,
                        ApprovalDate = DateTime.Now,
                        IsActive = true
                    };

                    _context.ClaimApprovals.Add(approval);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Claim {ClaimId} rejected successfully by user {UserId}", claimId, userId);
                    TempData["SuccessMessage"] = $"Claim rejected successfully. Reference: {claim.ClaimReference}";

                    return RedirectToAction("Dashboard");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (DbUpdateException dbEx)
            {
                var innerException = dbEx.InnerException;
                var errorMessage = innerException?.Message ?? dbEx.Message;

                _logger.LogError(dbEx, "Database error while rejecting claim {ClaimId}. Error: {ErrorMessage}", claimId, errorMessage);

                if (errorMessage.Contains("RejectionReason") && errorMessage.Contains("NULL"))
                {
                    TempData["ErrorMessage"] = "Database configuration error: RejectionReason column does not allow NULL values. Please contact system administrator.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Database error while rejecting claim: {errorMessage}";
                }

                return RedirectToAction("ReviewClaim", new { id = claimId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while rejecting claim {ClaimId}", claimId);
                TempData["ErrorMessage"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction("ReviewClaim", new { id = claimId });
            }
        }

        public async Task<IActionResult> ApprovedClaims()
        {
            try
            {
                var approvedClaims = await _context.Claims
                    .Where(c => (c.Status == ClaimStatus.APPROVED_PC || c.Status == ClaimStatus.APPROVED_FINAL) && c.IsActive)
                    .Include(c => c.Lecturer)
                    .Include(c => c.Approvals)
                        .ThenInclude(a => a.Approver)
                    .Include(c => c.Documents)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .ToListAsync();

                return View(approvedClaims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading approved claims");
                TempData["ErrorMessage"] = "Error loading approved claims.";
                return View(new List<Claim>());
            }
        }

        public async Task<IActionResult> RejectedClaims()
        {
            try
            {
                var rejectedClaims = await _context.Claims
                    .Where(c => c.Status == ClaimStatus.REJECTED && c.IsActive)
                    .Include(c => c.Lecturer)
                    .Include(c => c.Approvals)
                        .ThenInclude(a => a.Approver)
                    .Include(c => c.Documents)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .ToListAsync();

                return View(rejectedClaims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading rejected claims");
                TempData["ErrorMessage"] = "Error loading rejected claims.";
                return View(new List<Claim>());
            }
        }

        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            try
            {
                var document = await _context.Documents
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentId == documentId);

                if (document == null)
                {
                    TempData["ErrorMessage"] = "Document not found.";
                    return RedirectToAction("Dashboard");
                }

                var path = Path.Combine(_environment.WebRootPath, "uploads", document.FilePath);
                if (!System.IO.File.Exists(path))
                {
                    TempData["ErrorMessage"] = "File not found on server.";
                    return RedirectToAction("ReviewClaim", new { id = document.ClaimId });
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
                _logger.LogError(ex, "Error downloading document {DocumentId}", documentId);
                TempData["ErrorMessage"] = "Error downloading document.";
                return RedirectToAction("Dashboard");
            }
        }

        // Emergency fix: Direct SQL approach if model changes don't work
        private async Task<bool> FixClaimApprovalManually(int claimId, int userId, string comments, bool isApproval)
        {
            try
            {
                // Use raw SQL to insert with explicit empty strings for NULL columns
                var sql = @"
                    INSERT INTO ClaimApprovals 
                    (ClaimId, ApproverId, Level, Status, Comments, RejectionReason, ReviewDate, ApprovalDate, IsActive)
                    VALUES 
                    ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})";

                var level = (int)ApprovalLevel.PROGRAMME_COORDINATOR;
                var status = isApproval ? (int)ApprovalStatus.APPROVED : (int)ApprovalStatus.REJECTED;
                var commentValue = string.IsNullOrWhiteSpace(comments) ?
                    (isApproval ? "Claim approved by Programme Coordinator." : string.Empty) :
                    comments.Trim();
                var rejectionReason = isApproval ? string.Empty : comments.Trim();

                await _context.Database.ExecuteSqlRawAsync(sql,
                    claimId, userId, level, status,
                    commentValue, rejectionReason,
                    DateTime.Now, DateTime.Now, true);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual fix failed for claim {ClaimId}", claimId);
                return false;
            }
        }
    }
}