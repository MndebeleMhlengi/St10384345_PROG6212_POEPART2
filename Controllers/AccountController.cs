using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CMCS.Data;
using CMCS.Models;
using CMCS.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CMCS.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Register
        public IActionResult Register()
        {
            return View();
        }

        // POST: Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // In the Register method, ensure hourly rate is set
                if (model.Role == UserRole.LECTURER)
                {
                    if (model.HourlyRate == null || model.HourlyRate <= 0)
                    {
                        ModelState.AddModelError("HourlyRate", "Lecturers must have an hourly rate greater than 0.");
                        return View(model);
                    }
                }

                // Check if user already exists
                if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                {
                    ModelState.AddModelError("Email", "User with this email already exists.");
                    return View(model);
                }

                // Create new user
                var user = new User
                {
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    Password = HashPassword(model.Password),
                    Role = model.Role,
                    PhoneNumber = model.PhoneNumber,
                    Department = model.Department,
                    EmployeeNumber = model.EmployeeNumber,

                    CreatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Registration successful! Please login.";
                return RedirectToAction("Login");
            }

            return View(model);
        }

        // GET: Login
        public IActionResult Login()
        {
            return View();
        }

        // POST: Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == model.Email && u.IsActive);

                if (user != null && VerifyPassword(model.Password, user.Password))
                {
                    // Update last login date
                    user.LastLoginDate = DateTime.Now;
                    await _context.SaveChangesAsync();

                    // Create claims for authentication
                    var claims = new List<System.Security.Claims.Claim>
                    {
                        new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                        new System.Security.Claims.Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                        new System.Security.Claims.Claim(ClaimTypes.Email, user.Email),
                        // Correctly convert the enum to a string for the role claim
                        new System.Security.Claims.Claim(ClaimTypes.Role, user.Role.ToString())
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        authProperties);

                    // Redirect based on role
                    return RedirectToAction("Dashboard", GetControllerByRole(user.Role));
                }
                else
                {
                    ModelState.AddModelError("", "Invalid email or password.");
                }
            }

            return View(model);
        }

        // POST: Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["SuccessMessage"] = "You have been logged out successfully.";
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        private string HashPassword(string password)
        {
            var key = Encoding.UTF8.GetBytes("YourSecureSecretKey123");
            using (var hmac = new HMACSHA512(key))
            {
                var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private bool VerifyPassword(string password, string hashedPassword)
        {
            return HashPassword(password) == hashedPassword;
        }

        private string GetControllerByRole(UserRole role)
        {
            return role switch
            {
                UserRole.LECTURER => "Lecturer",
                UserRole.PROGRAMME_COORDINATOR => "ProgrammeCoordinator",
                UserRole.ACADEMIC_MANAGER => "AcademicManager",
                UserRole.HR => "HR",
                _ => "Home"
            };
        }
    }
}