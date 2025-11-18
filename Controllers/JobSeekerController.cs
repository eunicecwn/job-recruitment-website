using JobRecruitment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "JobSeeker")]
    public class JobSeekerController : Controller
    {
        private readonly DB _db;
        public JobSeekerController(DB db) => _db = db;

        // GET /JobSeeker   or   /JobSeeker/Dashboard
        // Added paging for the Recent Applications table

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
                ViewBag.UserName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.UserEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }
        public async Task<IActionResult> Index(int page = 1, int pageSize = 5)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var js = await _db.JobSeekers.FirstOrDefaultAsync(x => x.Id == uid);
            if (js is null) return NotFound();

            // Base query for user's applications
            var baseApps = _db.Applications
                .Include(a => a.Job).ThenInclude(j => j.Employer)
                .Where(a => a.JobSeekerId == uid);

            // Summary counts
            ViewBag.TotalApplications = await baseApps.CountAsync();
            ViewBag.Pending = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.Pending);
            ViewBag.Shortlisted = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.Shortlisted);
            ViewBag.InterviewScheduled = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.InterviewScheduled);
            ViewBag.OfferSent = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.OfferSent);
            ViewBag.Hired = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.Hired);
            ViewBag.Rejected = await baseApps.CountAsync(a => a.Status == ApplicationStatusEnum.Rejected);

            // ---------------- Recent applications (paged) ----------------
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 5 : pageSize;

            var recentTotal = await baseApps.CountAsync();
            var recentPageItems = await baseApps
                .OrderByDescending(a => a.AppliedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.RecentTotal = recentTotal;
            ViewBag.RecentPage = page;
            ViewBag.RecentPageSize = pageSize;

            ViewBag.RecentApplications = recentPageItems.Select(a => new
            {
                a.Id,
                a.JobId,
                JobTitle = a.Job?.Title ?? "",
                CompanyName = a.Job?.Employer?.CompanyName ?? "",
                AppliedLocal = a.AppliedDate.ToLocalTime(),
                StatusText = a.Status.ToString(),
                BadgeClass = StatusToBadge(a.Status)
            }).ToList();
            // ----------------------------------------------------------------

            // Profile completeness (single source of truth)
            var skillCount = await _db.JobSeekerSkills.CountAsync(s => s.JobSeekerId == uid);
            var expCount = await _db.WorkExperiences.CountAsync(e => e.JobSeekerId == uid);
            var eduCount = await _db.Educations.CountAsync(e => e.JobSeekerId == uid);
            var languageCount = await _db.Languages.CountAsync(l => l.JobSeekerId == uid);
            var licenseCount = await _db.Licenses.CountAsync(l => l.JobSeekerId == uid);

            ViewBag.ProfileCompleteness = ProfileMeter.Compute(
                js, skillCount, expCount, eduCount, languageCount, licenseCount);

            return View("Index"); // Views/JobSeeker/Index.cshtml
        }

        // GET /JobSeeker/Saved   -> Views/JobSeeker/Saved.cshtml
        public async Task<IActionResult> Saved()
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var items = await _db.SavedJobs
                .AsNoTracking()
                .Where(s => s.JobSeekerId == uid)
                .OrderByDescending(s => s.SavedUtc)
                .Select(s => new JobRecruitment.Models.JobSeekerViewModels.SavedJobItemVm
                {
                    JobId = s.JobId,
                    Title = s.Job.Title,
                    CompanyName = s.Job.Employer.CompanyName,
                    Location = s.Job.Location,
                    MinSalary = s.Job.MinSalary,
                    MaxSalary = s.Job.MaxSalary,
                    CategoryName = s.Job.Category != null ? s.Job.Category.Name : null,
                    SavedUtc = s.SavedUtc
                })
                .ToListAsync();

            return View("~/Views/JobSeeker/Saved.cshtml", items);
        }

        // GET /JobSeeker/Search   -> Views/JobSeeker/Search.cshtml
        public IActionResult Search()
        {
            return View("~/Views/JobSeeker/Search.cshtml");
        }

        // ---- helpers ---------------------------------------------------------
        private static string StatusToBadge(ApplicationStatusEnum s) => s switch
        {
            ApplicationStatusEnum.Hired => "bg-success",
            ApplicationStatusEnum.OfferSent => "bg-success",
            ApplicationStatusEnum.Rejected => "bg-danger",
            ApplicationStatusEnum.InterviewScheduled => "bg-info text-dark",
            ApplicationStatusEnum.Shortlisted => "bg-primary",
            _ => "bg-secondary"
        };
    }
}
