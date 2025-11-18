using JobRecruitment.Models;
using JobRecruitment.Models.JobSeekerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace JobRecruitment.Controllers
{
    [Route("[controller]/[action]")]
    public class JobSeekerActionsController : Controller
    {
        private readonly DB _db;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        public JobSeekerActionsController(DB db, IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider)
        {
            _db = db;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
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
        private void FlashSuccess(string title, string message)
        {
            TempData["SuccessTitle"] = title;
            TempData["SuccessMessage"] = message;
            TempData["Success"] = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";
        }
        private void FlashError(string title, string message)
        {
            TempData["ErrorTitle"] = title;
            TempData["ErrorMessage"] = message;
            TempData["Error"] = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";
        }

        private string CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No user id in claims.");

        private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        private IActionResult AjaxBadRequest(string msg) =>
            IsAjax() ? BadRequest(new { success = false, message = msg }) : BadRequest(msg);
        private IActionResult AjaxNotFound(string msg) =>
            IsAjax() ? NotFound(new { success = false, message = msg }) : NotFound(msg);

        // -------- REPORT (unchanged) --------
        [HttpPost("/ReportJob")]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ReportJob([FromForm] string JobId, [FromForm] string Reason, [FromForm] string? Details, CancellationToken ct)
        {
            try
            {
                JobId = (JobId ?? string.Empty).Trim();
                Reason = (Reason ?? string.Empty).Trim();
                Details = (Details ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(JobId)) return AjaxBadRequest("Missing JobId.");
                if (string.IsNullOrWhiteSpace(Reason) || Reason.Length > 100) return AjaxBadRequest("Reason is required and must be 100 characters or fewer.");
                if (Details.Length > 1500) return AjaxBadRequest("Details must be 1500 characters or fewer.");

                var job = await _db.Jobs.Include(j => j.Employer).FirstOrDefaultAsync(j => j.Id == JobId, ct);
                if (job == null) return AjaxNotFound($"Job {JobId} not found.");

                var uid = CurrentUserId;
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
                var report = new JobReport
                {
                    Id = NextJobReportId(),
                    JobId = job.Id,
                    Reason = BuildReason(Reason, user?.FullName ?? "Unknown", user?.Email ?? "Unknown", Details),
                    DateReported = DateTime.UtcNow
                };

                _db.JobReports.Add(report);
                await _db.SaveChangesAsync(ct);

                if (IsAjax()) return Json(new { success = true, message = "Thanks for your report. We’ll review it shortly." });
                FlashSuccess("Report submitted", "Thanks for your report. We’ll review it shortly.");
                return RedirectToAction("Search", "JobSearch", new { id = JobId });
            }
            catch (Exception ex)
            {
                if (IsAjax()) return StatusCode(500, new { success = false, message = "Server error reporting job.", detail = ex.Message });
                FlashError("Error", "Server error reporting job.");
                return RedirectToAction("Search", "JobSearch", new { id = JobId });
            }
        }

        // ======================= APPLY (popup + submit) =======================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ApplyPopup([FromQuery] string jobId, CancellationToken ct)
        {
            jobId = (jobId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(jobId)) return AjaxBadRequest("Missing job id.");

            var job = await _db.Jobs.Include(j => j.Employer).FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job == null) return AjaxNotFound("Job not found.");

            var uid = CurrentUserId;
            var already = await _db.Applications.AsNoTracking().AnyAsync(a => a.JobId == jobId && a.JobSeekerId == uid, ct);
            if (already) return PartialView("_AlreadyAppliedPartial", job);

            // Rehydrate questions from QuestionSet (preferred) or Job.Questions
            var questions = await LoadQuestionsForJob(job, ct);

            var vm = new ApplicationsApplyVm
            {
                JobId = jobId,
                Job = job,
                CoverLetter = "",
                HasQuestions = questions.Count > 0,
                Questions = questions.Select(q => new ApplicationsApplyVm.QuestionVm
                {
                    QuestionId = q.Id,
                    QuestionText = q.Text,
                    Type = q.Type,
                    IsRequired = q.IsRequired,
                    OptionsCsv = q.Options,
                    MaxLength = q.MaxLength
                }).ToList()
            };

            return PartialView("_ApplyPopup", vm);
        }

        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Apply(ApplicationsApplyVm model, IFormFile? Resume, CancellationToken ct)
        {
            model.JobId = (model.JobId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.JobId)) return AjaxBadRequest("Missing JobId.");

            var job = await _db.Jobs
                .Include(j => j.Employer)
                .FirstOrDefaultAsync(j => j.Id == model.JobId, ct);
            if (job == null) return AjaxNotFound("Job not found.");

            var uid = CurrentUserId;

            var already = await _db.Applications.AsNoTracking().AnyAsync(a => a.JobId == model.JobId && a.JobSeekerId == uid, ct);
            if (already)
            {
                var redirectA = Url.Action("MyApplications", "Applications");
                if (IsAjax()) return Json(new { success = true, redirectUrl = redirectA, message = "You’ve already applied to this job." });
                FlashSuccess("Application on file", "You’ve already applied to this job.");
                return RedirectToAction("MyApplications", "Applications");
            }

            // ---------- REHYDRATE DB QUESTION META ----------
            var qMeta = await LoadQuestionsForJob(job, ct);
            var metaById = qMeta.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

            // ---------- VALIDATE RESUME ----------
            if (Resume == null || Resume.Length == 0)
            {
                ModelState.AddModelError("Resume", "Please upload your resume (PDF required).");
            }
            else
            {
                if (!Resume.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    ModelState.AddModelError("Resume", "Resume must be a PDF file.");
                if (Resume.Length > 5 * 1024 * 1024)
                    ModelState.AddModelError("Resume", "Resume must be 5 MB or smaller.");
            }

            // ---------- VALIDATE QUESTIONS ----------
            if (model.Questions != null && model.Questions.Any())
            {
                for (int i = 0; i < model.Questions.Count; i++)
                {
                    var posted = model.Questions[i];
                    metaById.TryGetValue(posted.QuestionId ?? "", out var meta);

                    var qType = meta?.Type ?? posted.Type;
                    var required = meta?.IsRequired ?? false;
                    var isFile = IsFileType(qType);

                    if (isFile)
                    {
                        if (required && (posted.Upload == null || posted.Upload.Length == 0))
                        {
                            var label = posted.QuestionText ?? meta?.Text ?? "This file";
                            ModelState.AddModelError($"Questions[{i}].Upload", $"“{label}” is required.");
                            continue;
                        }
                        if (posted.Upload != null && posted.Upload.Length > 0)
                        {
                            var ok = ValidateAttachment(posted.Upload);
                            if (!ok.success)
                                ModelState.AddModelError($"Questions[{i}].Upload", ok.message);
                        }
                    }
                    else
                    {
                        var answer = (posted.Answer ?? "").Trim();
                        if (required && string.IsNullOrWhiteSpace(answer))
                        {
                            var label = posted.QuestionText ?? meta?.Text ?? "This field";
                            ModelState.AddModelError($"Questions[{i}].Answer", $"“{label}” is required.");
                        }
                        if (meta?.MaxLength is int max && !string.IsNullOrEmpty(answer) && answer.Length > max)
                        {
                            ModelState.AddModelError($"Questions[{i}].Answer", $"Maximum {max} characters.");
                        }
                    }
                }
            }
            else if (qMeta.Count > 0)
            {
                ModelState.AddModelError("", "Please answer the required questions.");
            }

            // ---------- SHORT-CIRCUIT ON INVALID ----------
            if (!ModelState.IsValid)
            {
                var vm = new ApplicationsApplyVm
                {
                    JobId = model.JobId,
                    Job = job,
                    CoverLetter = model.CoverLetter,
                    HasQuestions = qMeta.Count > 0,
                    Questions = qMeta.Select(q =>
                    {
                        var posted = model.Questions?.FirstOrDefault(p => p.QuestionId == q.Id);
                        return new ApplicationsApplyVm.QuestionVm
                        {
                            QuestionId = q.Id,
                            QuestionText = q.Text,
                            Type = q.Type,
                            IsRequired = q.IsRequired,
                            OptionsCsv = q.Options,
                            MaxLength = q.MaxLength,
                            Answer = posted?.Answer
                        };
                    }).ToList()
                };

                if (IsAjax())
                {
                    var html = await RenderPartialViewToStringAsync("_ApplyPopup", vm);
                    return Json(new { success = false, html });
                }

                return PartialView("_ApplyPopup", vm);
            }

            // ---------- SAVE RESUME ----------
            string? savedResumeName = null;
            if (Resume != null && Resume.Length > 0)
                savedResumeName = await SaveUploadedAsync(Resume, Path.Combine("uploads", "resumes"), ct, forcePdf: true);

            // ---------- CREATE APPLICATION ----------
            var app = new Application
            {
                Id = NextApplicationId(),
                JobId = model.JobId,
                JobSeekerId = uid,
                CoverLetter = (model.CoverLetter ?? "").Trim(),
                ResumeFileName = savedResumeName,
                AppliedDate = DateTime.UtcNow,
                Status = ApplicationStatusEnum.Pending
            };

            _db.Applications.Add(app);
            await _db.SaveChangesAsync(ct);

            // ---------- SAVE ANSWERS ----------
            if (model.Questions != null && model.Questions.Any())
            {
                int nextQrpNumber = CurrentMaxQuestionResponseSequence();

                for (int i = 0; i < model.Questions.Count; i++)
                {
                    var q = model.Questions[i];
                    string? answer = null;

                    // File-type?
                    if (IsFileType(q.Type) && q.Upload != null && q.Upload.Length > 0)
                    {
                        var saved = await SaveUploadedAsync(q.Upload, Path.Combine("uploads", "question-files"), ct);
                        answer = saved; // store saved filename (relative)
                    }
                    else
                    {
                        var txt = (q.Answer ?? "").Trim();
                        if (!string.IsNullOrEmpty(txt)) answer = txt;
                    }

                    if (answer == null) continue;

                    nextQrpNumber++;

                    var newResp = new QuestionResponse
                    {
                        Id = $"QRP{nextQrpNumber:D7}",
                        QuestionId = q.QuestionId,
                        ApplicationId = app.Id,
                        JobSeekerId = uid,
                        Answer = answer,
                        ResponseDate = DateTime.UtcNow
                    };
                    await _db.QuestionResponses.AddAsync(newResp, ct);
                }

                await _db.SaveChangesAsync(ct);
            }

            var redirect = Url.Action("MyApplications", "Applications");
            if (IsAjax()) return Json(new { success = true, redirectUrl = redirect, message = "Application submitted successfully." });
            FlashSuccess("Application submitted", "Your application was sent successfully.");
            return Redirect(redirect!);
        }

        // ======================= SAVE / UNSAVE (unchanged) =======================
        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> SaveJob([FromForm] string jobId, CancellationToken ct)
        {
            jobId = (jobId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(jobId)) return AjaxBadRequest("Missing job id.");

            var uid = CurrentUserId;

            var exists = await _db.SavedJobs.AsNoTracking().AnyAsync(s => s.JobSeekerId == uid && s.JobId == jobId, ct);
            if (exists) return IsAjax() ? Json(new { success = true, saved = true }) : RedirectToAction("Saved", "JobSeeker");

            var jobExists = await _db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct);
            if (!jobExists) return AjaxNotFound("Job not found.");

            _db.SavedJobs.Add(new SavedJob
            {
                Id = NextSavedJobId(),
                JobSeekerId = uid,
                JobId = jobId,
                SavedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            if (IsAjax()) return Json(new { success = true, saved = true });
            TempData["Success"] = "Saved to your jobs.";
            return RedirectToAction("Saved", "JobSeeker");
        }

        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> UnsaveJob([FromForm] string jobId, CancellationToken ct)
        {
            jobId = (jobId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(jobId)) return AjaxBadRequest("Missing job id.");

            var uid = CurrentUserId;
            var row = await _db.SavedJobs.FirstOrDefaultAsync(s => s.JobSeekerId == uid && s.JobId == jobId, ct);
            if (row != null)
            {
                _db.SavedJobs.Remove(row);
                await _db.SaveChangesAsync(ct);
            }

            if (IsAjax()) return Json(new { success = true, saved = false });
            TempData["Success"] = "Removed from saved.";
            return RedirectToAction("Saved", "JobSeeker");
        }

        // ---- helpers ----
        private static string BuildReason(string reason, string reporterName, string reporterEmail, string? details)
        {
            var core = $"Reporter: {reporterName} <{reporterEmail}>\nReason: {reason}";
            if (!string.IsNullOrWhiteSpace(details)) core += $"\nDetails: {details}";
            return core;
        }

        private static bool IsFileType(QuestionType t)
        {
            var n = t.ToString();
            return n.Equals("File", StringComparison.OrdinalIgnoreCase)
                || n.Equals("FileUpload", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Attachment", StringComparison.OrdinalIgnoreCase)
                || n.Equals("File Upload", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool success, string message) ValidateAttachment(IFormFile file)
        {
            if (file.Length <= 0) return (false, "Empty file.");
            if (file.Length > 10 * 1024 * 1024) return (false, "File must be 10 MB or smaller.");

            var allowed = new[] { ".pdf", ".doc", ".docx", ".png", ".jpg", ".jpeg" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return (false, "Unsupported file type. Allowed: PDF, DOC, DOCX, PNG, JPG.");
            return (true, "");
        }

        private async Task<string> SaveUploadedAsync(IFormFile file, string subdir, CancellationToken ct, bool forcePdf = false)
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", subdir));

            var ext = forcePdf ? ".pdf" : Path.GetExtension(file.FileName);
            if (forcePdf) ext = ".pdf";
            var fname = $"{Guid.NewGuid():N}{ext}";
            var full = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", subdir, fname);

            using var fs = System.IO.File.Create(full);
            await file.CopyToAsync(fs, ct);

            return $"{subdir.Replace("\\", "/")}/{fname}";
        }

        private string NextJobReportId()
        {
            var max = _db.JobReports.Where(x => x.Id.StartsWith("JRP") && x.Id.Length == 10)
                .Select(x => x.Id.Substring(3)).AsEnumerable()
                .Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return $"JRP{(max + 1):D7}";
        }

        private int CurrentMaxQuestionResponseSequence()
        {
            var max = _db.QuestionResponses.Where(x => x.Id.StartsWith("QRP") && x.Id.Length == 10)
                .Select(x => x.Id.Substring(3)).AsEnumerable()
                .Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return max;
        }

        private string NextApplicationId()
        {
            var max = _db.Applications.Where(x => x.Id.StartsWith("APP") && x.Id.Length == 10)
                .Select(x => x.Id.Substring(3)).AsEnumerable()
                .Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return $"APP{(max + 1):D7}";
        }

        private string NextSavedJobId()
        {
            var max = _db.SavedJobs.Where(x => x.Id.StartsWith("SAV") && x.Id.Length == 10)
                .Select(x => x.Id.Substring(3)).AsEnumerable()
                .Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return $"SAV{(max + 1):D7}";
        }

        private async Task<List<Question>> LoadQuestionsForJob(Job job, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(job.QuestionSetId))
            {
                return await _db.Questions.AsNoTracking()
                    .Where(q => q.QuestionSetId == job.QuestionSetId)
                    .OrderBy(q => q.Order)
                    .ToListAsync(ct);
            }
            return await _db.Questions.AsNoTracking()
                .Where(q => q.JobId == job.Id)
                .OrderBy(q => q.Order)
                .ToListAsync(ct);
        }

        // Render partial to string (for AJAX: return html to replace modal)
        private async Task<string> RenderPartialViewToStringAsync(string viewName, object model)
        {
            ViewData.Model = model;
            await using var sw = new StringWriter();
            var viewResult = _viewEngine.FindView(ControllerContext, viewName, false);
            if (!viewResult.Success) throw new InvalidOperationException($"Partial view '{viewName}' not found.");
            var viewContext = new ViewContext(
                ControllerContext,
                viewResult.View,
                ViewData,
                new TempDataDictionary(HttpContext, _tempDataProvider),
                sw,
                new HtmlHelperOptions()
            );
            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }
    }
}
