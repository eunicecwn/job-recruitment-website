using JobRecruitment.Models;
using JobRecruitment.Services;
using JobRecruitment.Services;
using JobRecruitment.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Linq.Expressions;
using System.Security.Claims;
using X.PagedList;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "Employer")]
    public class JobController : Controller
    {
        private readonly DB db;
        private readonly IPremiumService _premiumService;  // <-- ADD THIS FIELD

        public JobController(DB db, IPremiumService premiumService)
        {
            this._premiumService = premiumService;  // <-- Now properly assigning to the field
            this.db = db;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            SetCacheHeaders();
            SetViewBagUserInfo();
            base.OnActionExecuting(context);
        }

        private void SetCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "-1";
        }

        private void SetViewBagUserInfo()
        {
            if (User.Identity.IsAuthenticated)
            {
                ViewBag.EmployerName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.EmployerEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.EmplpyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }
        private string NextId()
        {
            var numericIds = db.Jobs
                .Where(j => j.Id.StartsWith("JOB") && j.Id.Length == 10)
                .Select(j => j.Id.Substring(3))
                .AsEnumerable()
                .Where(s => int.TryParse(s, out var _))
                .Select(s => int.Parse(s))
                .DefaultIfEmpty(0)
                .Max();

            return $"JOB{(numericIds + 1):D7}";
        }
        public async Task<IActionResult> JobPost()
        {
            var employeeID = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check if user can post a job
            var canPost = await _premiumService.CanPostJobAsync(employeeID);
            if (!canPost)
            {
                var premiumStatus = await _premiumService.GetUserPremiumStatusAsync(employeeID);

                // FIX: Add null checks here
                var jobLimit = "3";
                if (premiumStatus?.PlanInfo != null)
                {
                    jobLimit = premiumStatus.PlanInfo.JobPostLimit.ToString();
                }

                TempData["ErrorMessage"] = $"You have reached your job posting limit ({jobLimit} posts). Please upgrade your plan to post more jobs.";
                return RedirectToAction("Upgrade", "Premium");
            }

            // Get remaining posts for display
            var remaining = await _premiumService.GetRemainingJobPostsAsync(employeeID);
            ViewBag.RemainingPosts = remaining == int.MaxValue ? "Unlimited" : remaining.ToString();

            var model = new JobViewModel
            {
                EmployerId = employeeID,
                Id = string.Empty
            };

            model.InitializeDropdowns();
            model.StatusOptions = model.StatusOptions
                .Where(option => option.Value != "Closed")
                .ToList();

            model.Categories = db.JobCategories
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToList();

            return View(model);
        }


        [HttpPost]
        public async Task<IActionResult> JobPost(JobViewModel model)
        {
            var employeeID = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Double-check limit before posting
            var canPost = await _premiumService.CanPostJobAsync(employeeID);
            if (!canPost)
            {
                TempData["ErrorMessage"] = "You have reached your job posting limit. Please upgrade your plan.";
                return RedirectToAction("Upgrade", "Premium");
            }

            Console.WriteLine($"ModelState isValid: {ModelState.IsValid}");
            Console.WriteLine($"ModelState errors: {ModelState.ErrorCount}");

            foreach (var error in ModelState)
            {
                if (error.Value.Errors.Count > 0)
                {
                    Console.WriteLine($"Error in {error.Key}: {error.Value.Errors[0].ErrorMessage}");
                }
            }

            if (model.MaxSalary < model.MinSalary)
            {
                ModelState.AddModelError(nameof(model.MaxSalary), "Max salary must be greater than or equal to min salary.");
            }

            if (!ModelState.IsValid)
            {
                // Repopulate dropdowns and remaining posts
                var remaining = await _premiumService.GetRemainingJobPostsAsync(employeeID);
                ViewBag.RemainingPosts = remaining == int.MaxValue ? "Unlimited" : remaining.ToString();

                model.InitializeDropdowns();
                model.Categories = db.JobCategories
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name,
                        Selected = c.Id.ToString() == model.CategoryId
                    })
                    .ToList();

                foreach (var item in model.JobTypes)
                {
                    item.Selected = item.Value == model.JobType.ToString();
                }

                foreach (var item in model.StatusOptions)
                {
                    item.Selected = item.Value == model.Status.ToString();
                }

                return View(model);
            }

            var job = new Job
            {
                Id = NextId(),
                Title = model.Title,
                Description = model.Description,
                Location = model.Location,
                MinSalary = model.MinSalary,
                MaxSalary = model.MaxSalary,
                Latitude = (decimal?)model.Latitude,
                Longitude = (decimal?)model.Longitude,
                PostedDate = model.PostedDate,
                ClosingDate = model.ClosingDate,
                JobType = model.JobType,
                Status = model.Status,
                CategoryId = model.CategoryId,
                EmployerId = model.EmployerId
            };

            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            // Increment job post count after successful post
            await _premiumService.IncrementJobPostCountAsync(employeeID);

            TempData["SuccessMessage"] = "Job posted successfully!";
            return RedirectToAction("DisplayJob", new { id = job.Id });
        }
        public async Task<IActionResult> JobList(int? page)
        {
            var categories = await db.JobCategories.ToListAsync();
            ViewBag.Categories = categories;

            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.EmployerId = employerId;

            await UpdateJobStatuses();

            int pageSize = 6;
            int pageNumber = page ?? 1;

            var query = db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .Where(j => j.EmployerId == employerId)
                .OrderByDescending(j => j.PostedDate);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

            var pagedJobs = new StaticPagedList<Job>(items, pageNumber, pageSize, totalCount);

            if (TempData["SuccessMessage"] != null)
            {
                ViewBag.SuccessMessage = TempData["SuccessMessage"];
            }

            return View(pagedJobs);
        }

        private async Task UpdateJobStatuses()
        {
            var now = DateTime.UtcNow;
            var jobsToClose = await db.Jobs
                .Where(j => j.Status == JobStatus.Open &&
                       j.ClosingDate.HasValue &&
                       j.ClosingDate.Value < now)
                .ToListAsync();

            foreach (var job in jobsToClose)
            {
                job.Status = JobStatus.Closed;
            }

            if (jobsToClose.Any())
            {
                await db.SaveChangesAsync();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Search(
            string searchTerm = "",
            string jobType = "",
            string category = "",
            string location = "",
            decimal? minSalary = null,
            string status = "",
            string sortBy = "PostedDate",
            string sortOrder = "desc",
            int page = 1,
            int pageSize = 6,
            string viewMode = "card",
            string employerId = "")
        {
            try
            {
                await UpdateJobStatuses();

                var query = db.Jobs
                    .Include(j => j.Employer)
                    .Include(j => j.Category)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(employerId))
                {
                    query = query.Where(j => j.EmployerId == employerId);
                }

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(j => j.Title.Contains(searchTerm) || j.Description.Contains(searchTerm));
                }

                if (!string.IsNullOrEmpty(jobType) && jobType != "All Types")
                {
                    query = query.Where(j => j.JobType.ToString() == jobType);
                }

                if (!string.IsNullOrEmpty(category) && category != "All Categories")
                {
                    query = query.Where(j => j.CategoryId == category);
                }

                if (!string.IsNullOrEmpty(location))
                {
                    query = query.Where(j => j.Location.Contains(location));
                }

                if (minSalary.HasValue)
                {
                    query = query.Where(j => j.MaxSalary >= minSalary.Value);
                }

                if (!string.IsNullOrEmpty(status) && status != "All Status")
                {
                    query = query.Where(j => j.Status.ToString() == status);
                }

                // Apply sorting
                query = ApplySorting(query, sortBy, sortOrder);

                // Manual pagination
                var totalCount = await query.CountAsync();
                var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                var pagedJobs = new StaticPagedList<Job>(items, page, pageSize, totalCount);

                return Json(new
                {
                    Success = true,
                    Jobs = pagedJobs.Select(j => new
                    {
                        j.Id,
                        j.Title,
                        j.Description,
                        j.Location,
                        j.MinSalary,
                        j.MaxSalary,
                        JobType = j.JobType.ToString(),
                        Status = j.Status.ToString(),
                        PostedDate = j.PostedDate.ToString("yyyy-MM-dd"),
                        ClosingDate = j.ClosingDate?.ToString("yyyy-MM-dd"),
                        IsActive = j.IsActive,
                        Employer = new
                        {
                            j.Employer?.Id,
                            j.Employer?.CompanyName
                        },
                        Category = new
                        {
                            j.Category?.Id,
                            j.Category?.Name
                        }
                    }),
                    TotalCount = pagedJobs.TotalItemCount,
                    CurrentPage = pagedJobs.PageNumber,
                    TotalPages = pagedJobs.PageCount,
                    PageSize = pagedJobs.PageSize,
                    ViewMode = viewMode
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        // GET: DisplayJob (View mode only)
        public async Task<IActionResult> DisplayJob(string id)
        {
            await UpdateJobStatuses();

            var job = await db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
            {
                return NotFound();
            }

            return View(job);
        }

        [HttpGet]
        public async Task<IActionResult> UpdateJob(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
            if (job == null)
            {
                return NotFound();
            }

            var model = new JobViewModel
            {
                Id = job.Id, // ← MAKE SURE THIS IS SET
                Title = job.Title,
                Description = job.Description,
                Location = job.Location,
                MinSalary = job.MinSalary,
                MaxSalary = job.MaxSalary,
                Latitude = job.Latitude.HasValue ? (double)job.Latitude.Value : 0,
                Longitude = job.Longitude.HasValue ? (double)job.Longitude.Value : 0,
                PostedDate = job.PostedDate,
                ClosingDate = job.ClosingDate,
                JobType = job.JobType,
                Status = job.Status,
                CategoryId = job.CategoryId,
                EmployerId = job.EmployerId
            };

            model.InitializeDropdowns();
            model.Categories = db.JobCategories
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToList();

            return View(model);
        }

        // POST: UpdateJob (Save changes)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateJob(JobViewModel model)
        {
            if (model.MaxSalary < model.MinSalary)
            {
                ModelState.AddModelError(nameof(model.MaxSalary), "Max salary must be greater than or equal to min salary.");
            }

            if (!ModelState.IsValid)
            {
                // Repopulate dropdowns for the form
                model.InitializeDropdowns();
                model.Categories = db.JobCategories
                    .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                    .ToList();

                return View(model);
            }

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == model.Id);
            if (job == null)
            {
                return NotFound();
            }

            // Update all fields
            job.Title = model.Title;
            job.Description = model.Description;
            job.Location = model.Location;
            job.MinSalary = model.MinSalary;
            job.MaxSalary = model.MaxSalary;
            job.Latitude = (decimal?)model.Latitude;
            job.Longitude = (decimal?)model.Longitude;
            job.ClosingDate = model.ClosingDate;
            job.JobType = model.JobType;
            job.Status = model.Status;
            job.CategoryId = model.CategoryId;

            db.Update(job);
            await db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Job updated successfully!";
            return RedirectToAction("JobList");
        }

        private IQueryable<Job> ApplySorting(IQueryable<Job> query, string sortBy, string sortOrder)
        {
            return (sortBy?.ToLower(), sortOrder?.ToLower()) switch
            {
                ("title", "asc") => query.OrderBy(j => j.Title),
                ("title", "desc") => query.OrderByDescending(j => j.Title),
                ("salary", "asc") => query.OrderBy(j => j.MinSalary),
                ("salary", "desc") => query.OrderByDescending(j => j.MinSalary),
                ("location", "asc") => query.OrderBy(j => j.Location),
                ("location", "desc") => query.OrderByDescending(j => j.Location),
                ("closingdate", "asc") => query.OrderBy(j => j.ClosingDate ?? DateTime.MaxValue),
                ("closingdate", "desc") => query.OrderByDescending(j => j.ClosingDate ?? DateTime.MinValue),
                ("posteddate", "asc") => query.OrderBy(j => j.PostedDate),
                (_, _) => query.OrderByDescending(j => j.PostedDate)
            };
        }

        [HttpGet]
        public async Task<IActionResult> EmployeeProfile(string id = null)
        {
            // If no ID provided, use current user's ID
            var employeeId = id ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(employeeId))
            {
                return NotFound("Employee not found");
            }

            // Get the user from database
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == employeeId);

            if (user == null)
            {
                return NotFound("Employee not found");
            }

            // Map user data to EmployeeProfileViewModel
            var model = new EmployeeProfileViewModel
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Gender = user.Gender,
                DateOfBirth = user.DateOfBirth,
                ProfilePhotoFileName = user.ProfilePhotoFileName,

                // Set employee fields to null since they don't exist in UserBase
                JobTitle = "",
                Department = "",
                EmploymentStatus = "",
                HireDate = null,
                Salary = null,
                Manager = "",
                OfficeLocation = "",
                Bio = "",
                EmergencyContactName = "",
                EmergencyContactPhone = "",
                EmergencyContactRelationship = ""
            };
            ViewBag.ProfilePhotoFileName = user.ProfilePhotoFileName;
            ViewBag.UserName = user.FullName;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeProfile(JobRecruitment.ViewModels.EmployeeProfileViewModel model)
        {
            // SKIP ALL VALIDATION - just process the photo
            try
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == model.Id);

                if (user == null)
                {
                    TempData["Error"] = "Employee not found";
                    return View(model);
                }

                // Update basic user properties
                user.FullName = model.FullName ?? user.FullName;
                user.Email = model.Email ?? user.Email;
                user.Phone = model.Phone ?? user.Phone;
                user.Gender = model.Gender ?? user.Gender;
                user.DateOfBirth = model.DateOfBirth ?? user.DateOfBirth;

                // Handle profile photo upload
                if (model.ProfilePhoto != null && model.ProfilePhoto.Length > 0)
                {
                    var uploadResult = await HandleProfilePhotoUpload(model.ProfilePhoto, user.Id);
                    if (uploadResult.Success)
                    {
                        if (!string.IsNullOrEmpty(user.ProfilePhotoFileName))
                        {
                            DeleteOldProfilePhoto(user.ProfilePhotoFileName);
                        }
                        user.ProfilePhotoFileName = uploadResult.FileName;
                    }
                }

                db.Users.Update(user);
                await db.SaveChangesAsync();

                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction("EmployeeProfile", new { id = model.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = "An error occurred while updating your profile.";
                return View(model);
            }
        }

        // Helper method to handle profile photo upload
        private async Task<(bool Success, string FileName, string ErrorMessage)> HandleProfilePhotoUpload(IFormFile photo, string userId)
        {
            try
            {
                // Validate file
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    return (false, null, "Invalid file type. Please use jpg, jpeg, png, gif, or webp files.");
                }

                if (photo.Length > 5 * 1024 * 1024) // 5MB limit
                {
                    return (false, null, "File size must be less than 5MB.");
                }

                // Create uploads directory if it doesn't exist
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profilepics");
                Directory.CreateDirectory(uploadsPath);

                // Generate unique filename
                var fileName = $"{userId}_{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await photo.CopyToAsync(stream);
                }

                return (true, fileName, null);
            }
            catch (Exception ex)
            {
                return (false, null, $"Error uploading file: {ex.Message}");
            }
        }

        // Helper method to delete old profile photo
        private void DeleteOldProfilePhoto(string fileName)
        {
            try
            {
                if (!string.IsNullOrEmpty(fileName))
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profilepics", fileName);
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - old file deletion shouldn't break the update
                Console.WriteLine($"Error deleting old profile photo: {ex.Message}");
            }
        }

        public IActionResult Logout()
        {
            return RedirectToAction("Logout", "Account");
        }
    }
}