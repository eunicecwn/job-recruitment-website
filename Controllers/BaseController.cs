using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace JobRecruitment.Controllers
{
    public class BaseController : Controller
    {
        protected readonly DB _context;

        // Add constructor to inject database context
        public BaseController(DB context)
        {
            _context = context;
        }

        // Properties to access current user data from claims
        protected string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
        protected string CurrentUsername => User.FindFirstValue(ClaimTypes.Name);
        protected string CurrentUserFullName => User.FindFirstValue("FullName");
        protected string CurrentUserRole => User.FindFirstValue(ClaimTypes.Role);
        protected string CurrentUserEmail => User.FindFirstValue(ClaimTypes.Email);
        protected string CurrentCompanyName => User.FindFirstValue("CompanyName");

        // Check if user is logged in
        protected bool IsLoggedIn => User.Identity?.IsAuthenticated ?? false;

        // Check if user has specific role
        protected bool IsAdmin => User.IsInRole("Admin");
        protected bool IsEmployer => User.IsInRole("Employer");
        protected bool IsJobSeeker => User.IsInRole("JobSeeker");

        // Check if user has specific permissions (for admins)
        protected bool HasFullPermissions => User.HasClaim("Permissions", "Full");

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Add cache control headers for sensitive pages
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "-1";

            // Pass user data to ViewBag for use in views
            ViewBag.CurrentUserId = CurrentUserId;
            ViewBag.CurrentUsername = CurrentUsername;
            ViewBag.CurrentUserFullName = CurrentUserFullName;
            ViewBag.CurrentUserRole = CurrentUserRole;
            ViewBag.CurrentUserEmail = CurrentUserEmail;
            ViewBag.CurrentCompanyName = CurrentCompanyName;
            ViewBag.IsLoggedIn = IsLoggedIn;
            ViewBag.IsAdmin = IsAdmin;
            ViewBag.IsEmployer = IsEmployer;
            ViewBag.IsJobSeeker = IsJobSeeker;
            ViewBag.HasFullPermissions = HasFullPermissions;

            // Use ViewBag instead of TempData for layout data
            if (IsLoggedIn)
            {
                try
                {
                    // Set default values from claims
                    ViewBag.UserName = CurrentUserFullName ?? CurrentUsername ?? "User";
                    ViewBag.UserEmail = CurrentUserEmail ?? "user@example.com";
                    ViewBag.UserRole = CurrentUserRole;

                    // Try to get fresh data from database
                    if (!string.IsNullOrEmpty(CurrentUserId) && _context != null)
                    {
                        var user = _context.Users.FirstOrDefault(u => u.Id == CurrentUserId);

                        if (user != null)
                        {
                            // Update with database values
                            ViewBag.UserName = user.FullName ?? user.Username;
                            ViewBag.UserEmail = user.Email;
                            ViewBag.ProfilePhotoFileName = user.ProfilePhotoFileName;

                            // Set additional role-specific properties
                            if (user is Employer employer)
                            {
                                ViewBag.CompanyName = employer.CompanyName;
                            }
                        }
                        else
                        {
                            // User not found - use claims
                            ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Error accessing database - use claims as fallback
                    ViewBag.UserName = CurrentUserFullName ?? CurrentUsername ?? "User";
                    ViewBag.UserEmail = CurrentUserEmail ?? "user@example.com";
                    ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");

                    // Log the error instead of exposing it
                    System.Diagnostics.Debug.WriteLine($"Error in BaseController: {ex.Message}");
                }
            }
            else
            {
                // Not logged in - set guest values
                ViewBag.UserName = "Guest";
                ViewBag.UserEmail = "guest@example.com";
                ViewBag.ProfilePhotoFileName = null;
            }

            base.OnActionExecuting(context);
        }
    }

    // Custom authorization attributes for easier use
    public class RequireLoginAttribute : AuthorizeAttribute
    {
        public RequireLoginAttribute()
        {
            Policy = "AuthenticatedUsers";
        }
    }

    public class RequireAdminAttribute : AuthorizeAttribute
    {
        public RequireAdminAttribute()
        {
            Policy = "AdminOnly";
        }
    }

    public class RequireEmployerAttribute : AuthorizeAttribute
    {
        public RequireEmployerAttribute()
        {
            Policy = "EmployerOnly";
        }
    }

    public class RequireJobSeekerAttribute : AuthorizeAttribute
    {
        public RequireJobSeekerAttribute()
        {
            Policy = "JobSeekerOnly";
        }
    }

    public class RequireFullPermissionAdminAttribute : AuthorizeAttribute
    {
        public RequireFullPermissionAdminAttribute()
        {
            Policy = "FullPermissionAdmin";
        }
    }
}