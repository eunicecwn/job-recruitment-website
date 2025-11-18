using JobRecruitment.Models;
using JobRecruitment.Models.JobSeekerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace JobRecruitment.Controllers
{
    [AllowAnonymous]
    [Route("JobSearch")]
    public class JobSearchController : Controller
    {
        private const int PageSize = 12; // fixed (PageSize removed from UI)
        private readonly DB _db;
        public JobSearchController(DB db) => _db = db;

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
        // ----------------- flash helpers -----------------
        private void FlashSuccess(string title, string message)
        {
            TempData["SuccessTitle"] = title;
            TempData["SuccessMessage"] = message;
        }
        private void FlashError(string title, string message)
        {
            TempData["ErrorTitle"] = title;
            TempData["ErrorMessage"] = message;
            // retain legacy key some views may use:
            TempData["Error"] = message;
        }

        // GET /JobSearch/Search
        [HttpGet("Search")]
        public async Task<IActionResult> Index([FromQuery] JobSearchVm vm, [FromQuery] int open = 0)
        {
            var hadErrors = !ValidateFilters(vm);
            await PopulateResultsAsync(vm);
            ViewBag.ReopenFilters = open == 1 || hadErrors;
            return View("~/Views/JobSeeker/Search.cshtml", vm);
        }

        // GET /JobSearch/SearchPartial (AJAX pager)
        [HttpGet("SearchPartial")]
        public async Task<IActionResult> SearchPartial([FromQuery] JobSearchVm vm)
        {
            ValidateFilters(vm); // keep normalization for partial too
            await PopulateResultsAsync(vm);
            return PartialView("~/Views/Shared/_SearchResults.cshtml", vm);
        }


        [HttpGet("JobSearch/Details/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var job = await _db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null) return NotFound();

            // Your file lives at /Views/JobDetails.cshtml
            return View("~/Views/JobDetails.cshtml", job);
        }

        // GET /JobSearch/Search/{id}
        [HttpGet("Search/{id}")]
        public async Task<IActionResult> Search(string id, string? mode)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var job = await _db.Jobs
                .Include(j => j.Employer)
                .Include(j => j.Category)
                .FirstOrDefaultAsync(j => j.Id == id && j.IsActive);

            if (job == null) return NotFound();

            ViewBag.OpenApply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);

            return View("~/Views/JobDetails.cshtml", job);
        }

        // ---------------- helpers ----------------

        private bool ValidateFilters(JobSearchVm vm)
        {
            vm ??= new JobSearchVm();
            vm.Page = vm.Page <= 0 ? 1 : vm.Page;
            vm.SortBy ??= "recent";

            if (vm.MinSalary.HasValue && vm.MinSalary.Value < 0)
                ModelState.AddModelError(nameof(vm.MinSalary), "Minimum salary cannot be negative.");
            if (vm.MaxSalary.HasValue && vm.MaxSalary.Value < 0)
                ModelState.AddModelError(nameof(vm.MaxSalary), "Maximum salary cannot be negative.");

            if (vm.MinSalary.HasValue && vm.MaxSalary.HasValue && vm.MinSalary > vm.MaxSalary)
            {
                // Be forgiving: swap instead of rejecting
                (vm.MinSalary, vm.MaxSalary) = (vm.MaxSalary, vm.MinSalary);
            }

            if (!ModelState.IsValid)
            {
                var sb = new StringBuilder();
                foreach (var kv in ModelState)
                    foreach (var err in kv.Value.Errors)
                        if (!string.IsNullOrWhiteSpace(err.ErrorMessage))
                            sb.AppendLine(err.ErrorMessage);
                if (sb.Length > 0) FlashError("Invalid filters", sb.ToString().Trim());
            }
            return ModelState.IsValid;
        }

        private async Task PopulateResultsAsync(JobSearchVm vm)
        {
            // Dropdowns
            vm.Categories = await _db.JobCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new ValueTuple<string, string>(c.Id, c.Name))
                .ToListAsync();

            vm.JobTypes = Enum.GetValues(typeof(JobType))
                .Cast<JobType>()
                .Select(e => new ValueTuple<int, string>((int)e, e.ToString()))
                .ToList();

            // Base query
            var q = _db.Jobs.AsNoTracking()
                .Include(j => j.Category)
                .Include(j => j.Employer)
                .Where(j => j.IsActive && j.Status == JobStatus.Open);

            // Filters
            if (!string.IsNullOrWhiteSpace(vm.Q))
            {
                var term = vm.Q.Trim();
                q = q.Where(j =>
                    j.Title.Contains(term) ||
                    j.Description.Contains(term) ||
                    j.Location.Contains(term) ||
                    (j.Employer != null && j.Employer.CompanyName.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(vm.CategoryId))
                q = q.Where(j => j.CategoryId == vm.CategoryId);

            if (!string.IsNullOrWhiteSpace(vm.Location))
            {
                var loc = vm.Location.Trim();
                q = q.Where(j => j.Location.Contains(loc));
            }

            if (vm.JobType.HasValue)
                q = q.Where(j => (int)j.JobType == vm.JobType.Value);

            // Salary range intersection
            if (vm.MinSalary.HasValue)
                q = q.Where(j => j.MaxSalary >= vm.MinSalary.Value);
            if (vm.MaxSalary.HasValue)
                q = q.Where(j => j.MinSalary <= vm.MaxSalary.Value);

            // Count + sort + page
            vm.Total = await q.CountAsync();

            q = (vm.SortBy ?? "recent").ToLowerInvariant() switch
            {
                "salary_high" => q.OrderByDescending(j => j.MaxSalary).ThenByDescending(j => j.PostedDate),
                "salary_low" => q.OrderBy(j => j.MinSalary).ThenByDescending(j => j.PostedDate),
                "title_az" => q.OrderBy(j => j.Title),
                "title_za" => q.OrderByDescending(j => j.Title),
                _ => q.OrderByDescending(j => j.PostedDate)
            };

            var pageIndex = (vm.Page - 1) * PageSize;
            var page = await q.Skip(pageIndex).Take(PageSize).ToListAsync();

            vm.Results = page.Select(j => new JobSearchResultItemVm
            {
                JobId = j.Id,
                Title = j.Title,
                CompanyName = j.Employer?.CompanyName ?? "Unknown",
                Location = j.Location,
                MinSalary = j.MinSalary,
                MaxSalary = j.MaxSalary,
                CategoryName = j.Category?.Name,
                JobType = (int)j.JobType,
                PostedDateUtc = j.PostedDate
            }).ToList();

            vm.TotalPages = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)PageSize));
            vm.CurrentPageSize = PageSize;
        }
    }
}
