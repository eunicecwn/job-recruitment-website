using Azure;
using Azure.Core;
using JobRecruitment.Controllers;
using JobRecruitment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;

namespace Demo.Controllers
{
    public class HomeController : BaseController
    {
        private readonly DB db;
        private readonly ILogger<HomeController> _logger;

        // Single constructor that passes DB context to base
        public HomeController(DB db, ILogger<HomeController> logger) : base(db)
        {
            this.db = db;
            _logger = logger;
        }

        // Call base first to set ViewBag properties for sidebar
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context); // This sets all the sidebar ViewBag properties
                                             // Then add any additional headers if needed
        }

        // GET: Home/Index
        public IActionResult Index()
        {
            // Show all users (Admins, Employers, JobSeekers included)
            var users = db.Users.OrderByDescending(u => u.CreatedDate).ToList();

            // Add some stats to ViewBag
            ViewBag.TotalUsers = users.Count;
            ViewBag.TotalJobs = db.Jobs.Count(j => j.Status == JobStatus.Open);
            ViewBag.TotalEmployers = db.Employers.Count(e => e.IsActive);
            ViewBag.TotalJobSeekers = db.JobSeekers.Count(js => js.IsActive);
            ViewBag.TotalApplications = db.Applications.Count();

            return View(users);
        }

        // GET: Browse Jobs
        public async Task<IActionResult> BrowseJobs(string search = "", string category = "",
            string location = "", string jobType = "", int page = 1)
        {
            const int pageSize = 12;

            var query = db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .Where(j => j.Status == JobStatus.Open);

            // Apply filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(j => j.Title.Contains(search) ||
                                   j.Description.Contains(search) ||
                                   j.Employer.CompanyName.Contains(search));
                ViewBag.Search = search;
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(j => j.Category.Name == category);
                ViewBag.Category = category;
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                query = query.Where(j => j.Location.Contains(location));
                ViewBag.Location = location;
            }

            if (!string.IsNullOrWhiteSpace(jobType))
            {
                query = query.Where(j => j.JobType.ToString() == jobType);
                ViewBag.JobType = jobType;
            }

            // Get filter options
            ViewBag.Categories = await db.JobCategories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.JobTypes = await db.Jobs
                .Where(j => j.Status == JobStatus.Open)
                .Select(j => j.JobType.ToString())
                .Distinct()
                .OrderBy(jt => jt)
                .ToListAsync();

            ViewBag.Locations = await db.Jobs
                .Where(j => j.Status == JobStatus.Open)
                .Select(j => j.Location)
                .Distinct()
                .OrderBy(l => l)
                .Take(20)
                .ToListAsync();

            // Manual pagination
            var totalJobs = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalJobs / pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var jobs = await query
                .OrderByDescending(j => j.PostedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Pass pagination info to view
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalJobs = totalJobs;
            ViewBag.PageSize = pageSize;
            ViewBag.HasPreviousPage = page > 1;
            ViewBag.HasNextPage = page < totalPages;

            if (Request.IsAjax())
            {
                return PartialView("_JobsList", jobs);
            }

            return View(jobs);
        }

        // GET: Job Details
        public async Task<IActionResult> JobDetails(string id)
        {
            var job = await db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .Include(j => j.Applications)
                    .ThenInclude(a => a.JobSeeker)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
            {
                return NotFound();
            }

            // Check if current user has already applied
            ViewBag.HasApplied = false;
            if (IsLoggedIn && IsJobSeeker && !string.IsNullOrEmpty(CurrentUserId))
            {
                ViewBag.HasApplied = await db.Applications
                    .AnyAsync(a => a.JobId == id && a.JobSeekerId == CurrentUserId);
            }

            // Get related jobs
            var relatedJobs = await db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .Where(j => j.Id != id && j.Status == JobStatus.Open &&
                           (j.EmployerId == job.EmployerId || j.CategoryId == job.CategoryId))
                .OrderByDescending(j => j.PostedDate)
                .Take(4)
                .ToListAsync();

            ViewBag.RelatedJobs = relatedJobs;
            return View(job);
        }

        // POST: Apply for Job
        [HttpPost]
        [Authorize(Roles = "JobSeeker")]
        public async Task<IActionResult> ApplyForJob(string jobId)
        {
            if (string.IsNullOrEmpty(CurrentUserId))
            {
                return Json(new { success = false, message = "Please log in to apply." });
            }

            var job = await db.Jobs.FindAsync(jobId);
            if (job == null || job.Status != JobStatus.Open)
            {
                return Json(new { success = false, message = "Job not found or no longer available." });
            }

            // Check if already applied
            var existingApplication = await db.Applications
                .FirstOrDefaultAsync(a => a.JobId == jobId && a.JobSeekerId == CurrentUserId);

            if (existingApplication != null)
            {
                return Json(new { success = false, message = "You have already applied for this job." });
            }

            // Create application
            var application = new Application
            {
                JobId = jobId,
                JobSeekerId = CurrentUserId,
                AppliedDate = DateTime.Now,
                Status = ApplicationStatusEnum.Pending
            };

            db.Applications.Add(application);
            await db.SaveChangesAsync();

            return Json(new { success = true, message = "Application submitted successfully!" });
        }

        // Enhanced user detail view
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Detail(string id)
        {
            var user = await db.Users.FindAsync(id);
            if (user == null)
            {
                return RedirectToAction("Index");
            }

            // Get additional data based on user type
            if (user is Employer employer)
            {
                await db.Entry(employer)
                    .Collection(e => e.Jobs)
                    .LoadAsync();
            }
            else if (user is JobSeeker jobSeeker)
            {
                await db.Entry(jobSeeker)
                    .Collection(js => js.Applications)
                    .LoadAsync();
            }

            return View(user);
        }

        // Demo methods for Admin
        [Authorize(Roles = "Admin")]
        public IActionResult Demo1(string? role)
        {
            ViewBag.Roles = new[] { "Admin", "Employer", "JobSeeker" };
            var m = db.Users.Where(u => u.Role == role || role == null);

            if (Request.IsAjax())
            {
                return PartialView("_A", m);
            }

            return View(m);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Demo2(string? name)
        {
            name = name?.Trim() ?? "";
            var m = db.Users.Where(u => u.FullName.Contains(name));

            if (Request.IsAjax())
            {
                return PartialView("_A", m);
            }

            return View(m);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Demo3(string? sort, string? dir)
        {
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            Func<UserBase, object> fn = sort switch
            {
                "Id" => u => u.Id,
                "Name" => u => u.FullName,
                "Username" => u => u.Username,
                "Role" => u => u.Role,
                "Email" => u => u.Email,
                "Created" => u => u.CreatedDate,
                _ => u => u.Id
            };

            var m = dir == "des" ? db.Users.OrderByDescending(fn) : db.Users.OrderBy(fn);

            if (Request.IsAjax())
            {
                return PartialView("_B", m);
            }

            return View(m);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Demo4(int page = 1)
        {
            const int pageSize = 10;

            if (page < 1) page = 1;

            var totalEmployers = db.Employers.Count();
            var totalPages = (int)Math.Ceiling((double)totalEmployers / pageSize);

            if (page > totalPages && totalPages > 0) page = totalPages;

            var employers = db.Employers
                .Include(e => e.Jobs)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Pass pagination info
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.HasPreviousPage = page > 1;
            ViewBag.HasNextPage = page < totalPages;
            ViewBag.TotalItems = totalEmployers;

            if (Request.IsAjax())
            {
                return PartialView("_C", employers);
            }

            return View(employers);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Demo5(string? name, string? sort, string? dir, int page = 1)
        {
            const int pageSize = 10;

            // Searching
            ViewBag.Name = name = name?.Trim() ?? "";
            var searched = db.Users.Where(u => u.FullName.Contains(name));

            // Sorting
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            Func<UserBase, object> fn = sort switch
            {
                "Id" => u => u.Id,
                "Name" => u => u.FullName,
                "Username" => u => u.Username,
                "Role" => u => u.Role,
                "Email" => u => u.Email,
                "Created" => u => u.CreatedDate,
                _ => u => u.Id
            };

            var sorted = dir == "des" ? searched.OrderByDescending(fn) : searched.OrderBy(fn);

            // Manual Paging
            var totalUsers = sorted.Count();
            var totalPages = (int)Math.Ceiling((double)totalUsers / pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var users = sorted
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Pass pagination info
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalUsers = totalUsers;
            ViewBag.HasPreviousPage = page > 1;
            ViewBag.HasNextPage = page < totalPages;

            if (Request.IsAjax())
            {
                return PartialView("_D", users);
            }

            return View(users);
        }

        // Utility pages
        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
