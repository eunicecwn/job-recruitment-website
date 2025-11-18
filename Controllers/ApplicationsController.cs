using JobRecruitment.Models;
using JobRecruitment.Models.JobSeekerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace JobRecruitment.Controllers
{
    // All routes under /Applications/... and only for JobSeekers
    [Authorize(Roles = "JobSeeker")]
    [Route("Applications")]


    public class ApplicationsController : Controller
    {
        private readonly DB _db;
        public ApplicationsController(DB db) => _db = db;

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
                ViewBag.UserName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.UserEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }

        // Accept /Applications/Details?id=APP0000001
        [HttpGet("Details")]
        public Task<IActionResult> DetailsQuery([FromQuery] string id) =>
            ApplicationDetails(id);

        // Accept /Applications/Details/APP0000001  (GET)
        [HttpGet("Details/{id}")]
        public Task<IActionResult> DetailsRoute([FromRoute] string id) =>
            ApplicationDetails(id);

        // Accept /Applications/Details/APP0000001  (POST)
        [HttpPost("Details/{id}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> DetailsPost([FromRoute] string id, ApplicationDetailsVm vm) =>
            ApplicationDetails(id, vm);

        

        // GET /Applications
        // GET /Applications/MyApplications
        [HttpGet("")]
        [HttpGet("MyApplications")]
        public async Task<IActionResult> MyApplications([FromQuery] ApplicationsVm vm)
        {
            const int FixedPageSize = 12;

            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var q = _db.Applications
                .Include(a => a.Job).ThenInclude(j => j.Employer)
                .Where(a => a.JobSeekerId == uid);

            if (!string.IsNullOrWhiteSpace(vm.Term))
            {
                var term = vm.Term.Trim();
                q = q.Where(a =>
                    (a.Job != null && a.Job.Title.Contains(term)) ||
                    (a.Job != null && a.Job.Employer != null && a.Job.Employer.CompanyName.Contains(term)));
            }

            if (vm.Status.HasValue)
                q = q.Where(a => a.Status == vm.Status.Value);

            vm.Page = vm.Page <= 0 ? 1 : vm.Page;
            vm.PageSize = FixedPageSize;
            vm.Total = await q.CountAsync();

            var rows = await q.OrderByDescending(a => a.AppliedDate)
                .Skip((vm.Page - 1) * vm.PageSize)
                .Take(vm.PageSize)
                .Select(a => new ApplicationListItemVm
                {
                    Id = a.Id,
                    JobId = a.JobId,
                    JobTitle = a.Job != null ? a.Job.Title ?? "" : "",
                    CompanyName = a.Job != null && a.Job.Employer != null ? a.Job.Employer.CompanyName ?? "" : "",
                    AppliedLocal = a.AppliedDate.ToLocalTime(),
                    StatusEnum = a.Status,
                    StatusText = StatusToText(a.Status),
                    BadgeClass = StatusToBadge(a.Status)
                })
                .ToListAsync();

            vm.Items = rows;

            return View("~/Views/Applications/MyApplications.cshtml", vm);
        }

        // GET /Applications/ApplicationDetails/{id}
        [HttpGet("ApplicationDetails/{id}")]
        public async Task<IActionResult> ApplicationDetails(string id)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var app = await _db.Applications
                .Include(a => a.Job).ThenInclude(j => j.Employer)
                .Include(a => a.Job).ThenInclude(j => j.QuestionSet).ThenInclude(qs => qs.Questions)
                .Include(a => a.JobSeeker)
                .Include(a => a.QuestionResponses)
                .FirstOrDefaultAsync(a => a.Id == id && a.JobSeekerId == uid);

            if (app == null) return NotFound();

            var vm = new ApplicationDetailsVm
            {
                Id = app.Id,
                JobId = app.JobId,
                Application = app,
                Job = app.Job,
                CoverLetter = app.CoverLetter,
                ResumeFileName = app.ResumeFileName,
                HasQuestionSet = app.Job?.QuestionSet != null && app.Job.QuestionSet.IsActive,
                QuestionSetName = app.Job?.QuestionSet?.Name
            };

            if (vm.HasQuestionSet)
            {
                var orderedQs = app.Job!.QuestionSet!.Questions
                    .OrderBy(q => q.Order)
                    .ToList();

                foreach (var q in orderedQs)
                {
                    var existing = app.QuestionResponses.FirstOrDefault(r => r.QuestionId == q.Id);
                    vm.Questions.Add(new ApplicationDetailsVm.QuestionAnswerVm
                    {
                        QuestionId = q.Id,
                        QuestionText = q.Text,
                        Type = q.Type,
                        IsRequired = q.IsRequired,
                        OptionsCsv = q.Options,
                        MaxLength = q.MaxLength,
                        Answer = existing?.Answer
                    });
                }
            }

            return View("~/Views/Applications/Details.cshtml", vm);
        }

        // POST /Applications/ApplicationDetails/{id}
        [HttpPost("ApplicationDetails/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplicationDetails(string id, ApplicationDetailsVm vm)
        {
            if (string.IsNullOrWhiteSpace(vm.Id) && !string.IsNullOrWhiteSpace(id))
                vm.Id = id;

            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var app = await _db.Applications
                .Include(a => a.Job).ThenInclude(j => j.QuestionSet).ThenInclude(qs => qs.Questions)
                .Include(a => a.QuestionResponses)
                .FirstOrDefaultAsync(a => a.Id == vm.Id && a.JobSeekerId == uid);

            if (app == null) return NotFound();

            // Required/maxlength validation
            if (vm.Questions != null && app.Job?.QuestionSet != null)
            {
                for (int i = 0; i < vm.Questions.Count; i++)
                {
                    var qvm = vm.Questions[i];
                    qvm.Answer = qvm.Answer?.Trim();

                    if (qvm.IsRequired && string.IsNullOrWhiteSpace(qvm.Answer))
                        ModelState.AddModelError($"Questions[{i}].Answer", "This field is required.");

                    if (qvm.MaxLength.HasValue && !string.IsNullOrEmpty(qvm.Answer) && qvm.Answer.Length > qvm.MaxLength.Value)
                        ModelState.AddModelError($"Questions[{i}].Answer", $"Maximum {qvm.MaxLength.Value} characters.");
                }
            }

            if (!ModelState.IsValid)
            {
                // Refill summary data so the page can re-render
                vm.Application = app;
                vm.Job = app.Job;
                vm.CoverLetter = app.CoverLetter;
                vm.ResumeFileName = app.ResumeFileName;
                vm.HasQuestionSet = app.Job?.QuestionSet != null && app.Job.QuestionSet.IsActive;
                vm.QuestionSetName = app.Job?.QuestionSet?.Name;
                return View("~/Views/Applications/Details.cshtml", vm);
            }

            // ---------- SAFE ID ALLOCATION ----------
            var idSuffixes = await _db.QuestionResponses
                .Where(x => x.Id.StartsWith("QRS") && x.Id.Length == 10)
                .Select(x => x.Id.Substring(3))
                .ToListAsync();

            int qrsSeq = idSuffixes
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();

            // Save answers
            if (vm.Questions != null && vm.Questions.Count > 0)
            {
                foreach (var qvm in vm.Questions)
                {
                    var existing = app.QuestionResponses.FirstOrDefault(r => r.QuestionId == qvm.QuestionId);
                    if (existing == null)
                    {
                        qrsSeq++; // increment in-memory
                        var newId = $"QRS{qrsSeq:D7}";

                        _db.QuestionResponses.Add(new QuestionResponse
                        {
                            Id = newId,
                            ApplicationId = app.Id,
                            JobSeekerId = app.JobSeekerId,
                            QuestionId = qvm.QuestionId,
                            Answer = qvm.Answer ?? ""
                        });
                    }
                    else
                    {
                        existing.Answer = qvm.Answer ?? "";
                    }
                }

                await _db.SaveChangesAsync();
            }
            // ---------------------------------------

            FlashSuccess("Saved", "Your answers have been saved.");
            return RedirectToAction(nameof(ApplicationDetails), new { id = app.Id });
        }

        // ----- helpers -----
        private static string StatusToText(ApplicationStatusEnum s) => s switch
        {
            ApplicationStatusEnum.Pending => "Pending",
            ApplicationStatusEnum.Shortlisted => "Shortlisted",
            ApplicationStatusEnum.InterviewScheduled => "Interview Scheduled",
            ApplicationStatusEnum.OfferSent => "Offer Sent",
            ApplicationStatusEnum.Hired => "Hired",
            ApplicationStatusEnum.Rejected => "Rejected",
            _ => "Unknown"
        };

        private static string StatusToBadge(ApplicationStatusEnum s) => s switch
        {
            ApplicationStatusEnum.Hired => "bg-success",
            ApplicationStatusEnum.OfferSent => "bg-success",
            ApplicationStatusEnum.Rejected => "bg-danger",
            ApplicationStatusEnum.InterviewScheduled => "bg-info text-dark",
            ApplicationStatusEnum.Shortlisted => "bg-primary",
            ApplicationStatusEnum.Pending => "bg-secondary",
            _ => "bg-secondary"
        };
    }
}
