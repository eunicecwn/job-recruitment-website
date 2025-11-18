using Azure.Core;
using DocumentFormat.OpenXml.InkML;
using JobRecruitment.Hubs;
using JobRecruitment.Models; // Make sure this matches your actual namespace
using JobRecruitment.Models.JobSeekerViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Claims;
using X.PagedList;
using X.PagedList.Extensions;
using static Azure.Core.HttpHeader;

namespace Demo.Controllers;

[Authorize(Roles = "Employer")]
public class ApplicationController : Controller
{
    private readonly DB db;
    private readonly Helper helper;
    private readonly ILogger<ApplicationController> logger;
    private Dictionary<DateOnly, List<Application>> calendarModel;
    private readonly IHubContext<NotificationsHub> hub;

    private string GenerateReportId()
    {
        // Get the last report ID from both UserReports and EmployerReports
        var lastUserReportId = db.UserReports
            .OrderByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstOrDefault();

        var lastEmployerReportId = db.EmployerReports
            .OrderByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstOrDefault();

        // Find the highest ID
        var lastId = new[] { lastUserReportId, lastEmployerReportId }
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderByDescending(id => id)
            .FirstOrDefault();

        int lastNumber = 0;
        if (!string.IsNullOrEmpty(lastId) && lastId.StartsWith("R"))
        {
            int.TryParse(lastId.Substring(1), out lastNumber);
        }

        return $"R{(lastNumber + 1):D5}";
    }

    // Constructor for the JobController class that takes a DB instance as a parameter
    public ApplicationController(DB db, Helper helper, ILogger<ApplicationController> logger,
                                 IHubContext<NotificationsHub> hub) // ← add
    {
        this.db = db;
        this.helper = helper;
        this.logger = logger;
        this.hub = hub; // ← add
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

    private string GetCurrentEmployerId()
    {
        var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(employerId))
            throw new InvalidOperationException("User is not authenticated or employer ID not found.");
        return employerId;
    }

    public IActionResult CheckApplications(ApplicationStatusEnum? status, string jobId, int? page,
        string sortColumn = "AppliedDate", string sortDirection = "Descending")
    {
        var currentEmployerId = GetCurrentEmployerId();
        var pageNumber = page ?? 1;
        int pageSize = 10;

        var query = db.Applications
                    .Include(a => a.Job)
                    .Include(a => a.JobSeeker)
                    .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add this filter
                    .AsQueryable();

        // Status filter
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        // Job filter
        if (!string.IsNullOrEmpty(jobId))
        {
            query = query.Where(a => a.JobId == jobId);
        }

        // Apply sorting
        query = SortApplications(query, sortColumn, sortDirection);

        ViewBag.SelectedStatus = status;
        ViewBag.SelectedJobId = jobId;
        ViewBag.Jobs = db.Jobs.Where(j => j.EmployerId == currentEmployerId).ToList();
        ViewBag.SortColumn = sortColumn;
        ViewBag.SortDirection = sortDirection;

        var pagedList = query.ToPagedList(pageNumber, pageSize);

        return View(pagedList);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(string applicationId, ApplicationStatusEnum newStatus, string jobId, ApplicationStatusEnum? status)
    {
        var currentEmployerId = GetCurrentEmployerId();

        var application = await db.Applications
            .Include(a => a.Job)
                .ThenInclude(j => j.Employer)
            .Include(a => a.JobSeeker)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Application not found or access denied" });
            }
            return NotFound();
        }

        // Update the status
        application.Status = newStatus;
        await db.SaveChangesAsync();

        // Send email notification for specific status changes
        if (newStatus == ApplicationStatusEnum.OfferSent)
        {
            await SendOfferEmailUsingHelper(application);
        }
        else if (newStatus == ApplicationStatusEnum.Hired)
        {
            await SendHiredEmailUsingHelper(application);
        }

        string successMessage = $"Status updated to {newStatus} successfully!";

        // Handle AJAX requests
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Json(new
            {
                success = true,
                message = successMessage
            });
        }

        // Handle regular form submissions
        TempData["SuccessMessage"] = successMessage;
        return RedirectToAction("CheckApplications", new { status, jobId });
    }

    private async Task SendOfferEmailUsingHelper(Application application)
    {
        try
        {
            // Use the dedicated OFFER email method, not interview method
            await helper.SendOfferEmailAsync(
                application.JobSeeker.Email,
                application.JobSeeker.FullName,
                application.Job?.Title ?? "Unknown Position",
                application.Job.Employer.CompanyName
            );

            logger.LogInformation("Offer email sent to {Email}", application.JobSeeker.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send offer email to {Email}", application.JobSeeker.Email);
            // You might want to store this failure in a log or database
        }
    }

    private async Task SendHiredEmailUsingHelper(Application application)
    {
        try
        {
            await helper.SendHiredEmailAsync(
                application.JobSeeker.Email,
                application.JobSeeker.FullName,
                application.Job?.Title ?? "Unknown Position",
                application.Job.Employer.CompanyName,
                DateTime.Now.AddDays(14) // Example: Start in 2 weeks
            );

            logger.LogInformation("Hired email sent to {Email}", application.JobSeeker.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send hired email to {Email}", application.JobSeeker.Email);
        }
    }

    // Helper method to get next logical status transitions
    public List<ApplicationStatusEnum> GetNextAvailableStatuses(ApplicationStatusEnum currentStatus)
    {
        return currentStatus switch
        {
            ApplicationStatusEnum.Pending => new List<ApplicationStatusEnum>
        {
            ApplicationStatusEnum.Shortlisted
        },

            ApplicationStatusEnum.Shortlisted => new List<ApplicationStatusEnum>
        {
            ApplicationStatusEnum.InterviewScheduled
        },

            ApplicationStatusEnum.InterviewScheduled => new List<ApplicationStatusEnum>
        {
            ApplicationStatusEnum.OfferSent
        },

            ApplicationStatusEnum.OfferSent => new List<ApplicationStatusEnum>
        {
            ApplicationStatusEnum.Hired
        },

            ApplicationStatusEnum.Hired => new List<ApplicationStatusEnum>(),

            ApplicationStatusEnum.Rejected => new List<ApplicationStatusEnum>(),

            _ => new List<ApplicationStatusEnum>()
        };
    }
    [HttpPost]
    public async Task<IActionResult> UpdateStatusToReviewed([FromBody] UpdateStatusRequest request)
    {
        var currentEmployerId = GetCurrentEmployerId();

        var application = await db.Applications
            .Include(a => a.Job)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == request.Id);

        if (application == null)
        {
            return Json(new { success = false });
        }

        if (application.Status == ApplicationStatusEnum.Pending)
        {
            application.Status = ApplicationStatusEnum.Shortlisted;
            await db.SaveChangesAsync();

            return Json(new { success = true });
        }

        return Json(new { success = false });
    }

    // Add this class for the request model
    public class UpdateStatusRequest
    {
        public string Id { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> ScheduleInterview(string applicationId, string status, string jobId)
    {

        var currentEmployerId = GetCurrentEmployerId();

        if (applicationId == null)
        {
            Console.WriteLine("ApplicationId is null!");
            return NotFound();
        }

        var application = await db.Applications
            .Include(a => a.Job)
                .ThenInclude(j => j.Employer)
            .Include(a => a.JobSeeker)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
            return NotFound();

        // Get interviews for the calendar
        var interviews = await db.Applications
            .Where(a => a.InterviewDate.HasValue)
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ToListAsync();

        // Group interviews by date for the calendar
        var interviewCalendar = interviews
            .Where(a => a.InterviewDate.HasValue)
            .GroupBy(a => DateOnly.FromDateTime(a.InterviewDate.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Create the form model
        var formModel = new ScheduleInterviewViewModel
        {
            ApplicationId = applicationId,
            InterviewStartDate = application.InterviewDate ?? DateTime.Now.AddDays(1),
            InterviewLocation = application.InterviewLocation,
            InterviewNotes = application.InterviewNotes,
            InterviewerInfo = application.InterviewerInfo
        };

        // Set default time if this is a new interview
        if (!application.InterviewDate.HasValue)
        {
            // Set default start time to next hour
            DateTime now = DateTime.Now;
            DateTime nextHour = now.AddHours(1);
            formModel.StartTime = new TimeSpan(nextHour.Hour, 0, 0);
            formModel.EndTime = new TimeSpan(nextHour.Hour + 1, 0, 0); // Default 1 hour duration
        }
        else
        {
            // Extract time from existing interview date
            formModel.StartTime = application.InterviewDate.Value.TimeOfDay;
            // Set default end time to 1 hour after start if InterviewEndDate is null
            formModel.EndTime = application.InterviewEndDate?.TimeOfDay ?? application.InterviewDate.Value.AddHours(1).TimeOfDay;
        }

        // Create the composite view model
        var viewModel = new InterviewCalendarPageViewModel
        {
            Calendar = interviewCalendar,
            Form = formModel,
            Application = application
        };

        // Set ViewBag values for the dropdowns
        ViewBag.MonthList = helper.GetMonthList();
        ViewBag.YearList = helper.GetYearList(DateTime.Now.Year, DateTime.Now.Year + 1);

        // Set default month and year values to prevent null reference exceptions
        ViewBag.Month = application.InterviewDate?.Month ?? DateTime.Now.Month;
        ViewBag.Year = application.InterviewDate?.Year ?? DateTime.Now.Year;

        ViewBag.IsEdit = application.InterviewDate.HasValue;

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleInterview(InterviewCalendarPageViewModel viewModel)
    {
        var currentEmployerId = GetCurrentEmployerId();
        // Get filter values from FORM data
        string statusFilter = Request.Form["status"];
        string jobIdFilter = Request.Form["jobId"];

        // Load application with all required navigation properties
        var application = await db.Applications
        .Include(a => a.Job)
            .ThenInclude(j => j.Employer)
        .Include(a => a.JobSeeker)
        .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
        .FirstOrDefaultAsync(a => a.Id == viewModel.Form.ApplicationId);

        if (application == null)
        {
            TempData["ErrorMessage"] = $"Application not found or access denied. ID: {viewModel.Form.ApplicationId}";
            return RedirectToAction("CheckApplications");
        }

        // Validate time logic
        DateTime interviewDateTime = viewModel.Form.InterviewStartDate.Date.Add(viewModel.Form.StartTime);
        DateTime interviewEndDateTime = viewModel.Form.InterviewStartDate.Date.Add(viewModel.Form.EndTime);

        if (interviewEndDateTime <= interviewDateTime)
        {
            ModelState.AddModelError("Form.EndTime", "End time must be after start time.");
        }

        bool hasConflict = HasInterviewConflict(interviewDateTime, interviewEndDateTime, application.Id);
        if (hasConflict)
        {
            // Get conflict details for better error message
            var conflictingInterviews = db.Applications
                .Where(a => a.InterviewDate.HasValue &&
                           a.InterviewEndDate.HasValue &&
                           a.Id != application.Id &&
                           a.Status != ApplicationStatusEnum.Rejected &&
                           a.Status != ApplicationStatusEnum.Hired &&
                           (
                               (interviewDateTime >= a.InterviewDate.Value && interviewDateTime < a.InterviewEndDate.Value) ||
                               (interviewEndDateTime > a.InterviewDate.Value && interviewEndDateTime <= a.InterviewEndDate.Value) ||
                               (interviewDateTime <= a.InterviewDate.Value && interviewEndDateTime >= a.InterviewEndDate.Value) ||
                               (a.InterviewDate.Value <= interviewDateTime && a.InterviewEndDate.Value >= interviewEndDateTime)
                           ))
                .Include(a => a.JobSeeker)
                .Include(a => a.Job)
                .ToList();

            string conflictMessage = "This time slot conflicts with existing interview(s): " +
                                   GetConflictDetails(conflictingInterviews);

            ModelState.AddModelError("", conflictMessage);
        }

        if (!ModelState.IsValid)
        {
            foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
            {
                Console.WriteLine("Validation error: " + error.ErrorMessage);
            }
            return HandleValidationErrors(application, viewModel.Form);
        }

        bool isUpdate = application.InterviewDate.HasValue;

        if (application.Status == ApplicationStatusEnum.OfferSent ||
        application.Status == ApplicationStatusEnum.Hired)
        {
            TempData["ErrorMessage"] = $"Cannot update interview details because the application is already {application.Status}.";
            return RedirectToAction("CheckApplications", new { status = statusFilter, jobId = jobIdFilter });
        }

        application.InterviewDate = interviewDateTime;
        application.InterviewEndDate = interviewEndDateTime;
        application.InterviewLocation = viewModel.Form.InterviewLocation;
        application.InterviewNotes = viewModel.Form.InterviewNotes;
        application.InterviewerInfo = viewModel.Form.InterviewerInfo;
        application.Status = ApplicationStatusEnum.InterviewScheduled;

        try
        {
            await db.SaveChangesAsync();

            bool emailSent = await SendInterviewEmailUsingHelper(application, viewModel.Form, isUpdate);

            if (isUpdate)
            {
                TempData["SuccessMessage"] = emailSent
                    ? "Interview updated successfully!"
                    : "Interview updated successfully but email notification failed.";
            }
            else
            {
                TempData["SuccessMessage"] = emailSent
                    ? "Interview scheduled successfully!"
                    : "Interview scheduled successfully but email notification failed.";
            }

            return RedirectToAction("CheckApplications", new { status = statusFilter, jobId = jobIdFilter });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving interview details");
            TempData["ErrorMessage"] = "Error saving interview details: " + ex.Message;
            return RedirectToAction("CheckApplications", new { status = statusFilter, jobId = jobIdFilter });
        }
    }

    // New helper method that uses the Helper.cs service
    private async Task<bool> SendInterviewEmailUsingHelper(Application application, ScheduleInterviewViewModel formModel, bool isUpdate)
    {
        try
        {
            var emailModel = new InterviewEmailModel
            {
                JobTitle = application.Job?.Title ?? "N/A",
                InterviewDate = formModel.InterviewStartDate,
                StartTime = formModel.StartTime,
                EndTime = formModel.EndTime,
                InterviewLocation = formModel.InterviewLocation,
                InterviewerInfo = formModel.InterviewerInfo,
                InterviewNotes = formModel.InterviewNotes,
                CompanyName = application.Job?.Employer?.CompanyName ?? "Your Company",
            };

            var candidateEmail = application.JobSeeker?.Email;
            var candidateName = application.JobSeeker?.FullName ?? "Candidate";

            if (string.IsNullOrWhiteSpace(candidateEmail))
            {
                logger.LogError("No email found for JobSeeker (ApplicationId: {Id})", application.Id);
                return false;
            }

            if (isUpdate)
            {
                await helper.SendInterviewUpdatedEmailAsync(candidateEmail, candidateName, emailModel);
            }
            else
            {
                await helper.SendInterviewScheduledEmailAsync(candidateEmail, candidateName, emailModel);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending interview email to {Email}", application.JobSeeker?.Email ?? "UNKNOWN");
            return false;
        }
    }


    // Helper methods to avoid duplication
    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    private IActionResult HandleError(string message)
    {
        TempData["ErrorMessage"] = message;
        return RedirectToAction("CheckApplications");
    }

    private IActionResult HandleValidationErrors(Application application, ScheduleInterviewViewModel formModel)
    {
        // For validation errors, we need to recreate the view model with calendar data
        var interviews = db.Applications
            .Where(a => a.InterviewDate.HasValue)
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ToList();

        var interviewCalendar = interviews
            .Where(a => a.InterviewDate.HasValue)
            .GroupBy(a => DateOnly.FromDateTime(a.InterviewDate.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var viewModel = new InterviewCalendarPageViewModel
        {
            Calendar = interviewCalendar,
            Form = formModel,
            Application = application
        };

        PopulateViewBag(application);

        // Preserve the filter values in ViewBag for the view
        ViewBag.SelectedStatus = Request.Form["status"];
        ViewBag.SelectedJobId = Request.Form["jobId"];

        ViewBag.MonthList = helper.GetMonthList();
        ViewBag.YearList = helper.GetYearList(DateTime.Now.Year, DateTime.Now.Year + 1);

        return View("ScheduleInterview", viewModel);
    }

    private void PopulateViewBag(Application application)
    {
        ViewBag.ApplicantName = application.JobSeeker?.FullName ?? "N/A";
        ViewBag.ApplicantEmail = application.JobSeeker?.Email ?? "N/A";
        ViewBag.ApplicantPhone = application.JobSeeker?.Phone ?? "N/A";
        ViewBag.AppliedDate = application.AppliedDate.ToString("dd MMM yyyy");
        ViewBag.CurrentStatus = application.Status;
        ViewBag.CompanyName = application.Job?.Employer?.CompanyName ?? "N/A";
        ViewBag.IsEdit = application.InterviewDate.HasValue;
    }

    [HttpGet]
    public async Task<IActionResult> GetInterviewForm(string applicationId, string selectedDate)
    {
        var currentEmployerId = GetCurrentEmployerId();

        if (applicationId == null)
            return Content("<div class='alert alert-danger'>Application ID is required</div>");

        var application = await db.Applications
            .Include(a => a.Job)
                .ThenInclude(j => j.Employer)
            .Include(a => a.JobSeeker)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
            return Content("<div class='alert alert-danger'>Application not found or access denied</div>");

        // Parse the selected date or use a default
        DateTime interviewDate;
        if (!DateTime.TryParse(selectedDate, out interviewDate))
        {
            interviewDate = DateTime.Now.AddDays(1);
        }
        interviewDate = interviewDate.Date;

        // Build form model (always include Start/EndTime)
        var formModel = new ScheduleInterviewViewModel
        {
            ApplicationId = application.Id,
            InterviewStartDate = interviewDate,
            InterviewLocation = application.InterviewLocation,
            InterviewNotes = application.InterviewNotes,
            InterviewerInfo = application.InterviewerInfo,
            StartTime = application.InterviewDate?.TimeOfDay
                        ?? new TimeSpan(DateTime.Now.Hour + 1, 0, 0),
            EndTime = application.InterviewEndDate?.TimeOfDay
                      ?? new TimeSpan(DateTime.Now.Hour + 2, 0, 0)
        };

        // ✅ Build the calendar (don’t remove this)
        var interviews = await db.Applications
            .Where(a => a.InterviewDate.HasValue)
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ToListAsync();

        var interviewCalendar = interviews
            .GroupBy(a => DateOnly.FromDateTime(a.InterviewDate.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var viewModel = new InterviewCalendarPageViewModel
        {
            Calendar = interviewCalendar,
            Form = formModel,
            Application = application
        };

        // Populate ViewBag
        ViewBag.ApplicantName = application.JobSeeker?.FullName ?? "N/A";
        ViewBag.ApplicantEmail = application.JobSeeker?.Email ?? "N/A";
        ViewBag.ApplicantPhone = application.JobSeeker?.Phone ?? "N/A";
        ViewBag.AppliedDate = application.AppliedDate.ToString("dd MMM yyyy");
        ViewBag.CurrentStatus = application.Status;
        ViewBag.CompanyName = application.Job?.Employer?.CompanyName ?? "N/A";
        ViewBag.IsEdit = application.InterviewDate.HasValue;

        return View("ScheduleInterview", viewModel);
    }

    [HttpGet]
    public IActionResult InterviewCalendar()
    {
        var currentEmployerId = GetCurrentEmployerId();

        // Get interviews for the calendar - only for current employer
        var interviews = db.Applications
            .Where(a => a.InterviewDate.HasValue &&
                   a.Status == ApplicationStatusEnum.InterviewScheduled &&
                   a.Job.EmployerId == currentEmployerId) // ← Add this filter
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ToList();

        // Group interviews by date for the calendar
        var interviewCalendar = interviews
            .Where(a => a.InterviewDate.HasValue)
            .GroupBy(a => DateOnly.FromDateTime(a.InterviewDate.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Create the view model
        var viewModel = new InterviewCalendarPageViewModel
        {
            Calendar = interviewCalendar,
            Form = new ScheduleInterviewViewModel(),
            Application = null
        };

        // Pass jobs for filter dropdown
        ViewBag.Jobs = db.Jobs.Where(j => j.EmployerId == currentEmployerId).ToList();

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> FilterApplications(
    ApplicationStatusEnum? status,
    string jobId,
    int? page,
    string searchTerm,
    string sortColumn = "AppliedDate",
    string sortDirection = "Descending")
    {
        var currentEmployerId = GetCurrentEmployerId();
        int pageSize = 10;
        int pageNumber = page ?? 1;

        try
        {
            // Get applications with filtering
            var applications = db.Applications
                .Include(a => a.JobSeeker)
                .Include(a => a.Job)
                .Where(a => a.Job.EmployerId == currentEmployerId)
                .AsQueryable();

            // Apply status filter
            if (status.HasValue)
            {
                applications = applications.Where(a => a.Status == status.Value);
            }

            // Apply job filter
            if (!string.IsNullOrEmpty(jobId))
            {
                applications = applications.Where(a => a.JobId == jobId);
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(searchTerm))
            {
                applications = applications.Where(a =>
                    a.JobSeeker.FullName.Contains(searchTerm) ||
                    a.JobSeeker.Email.Contains(searchTerm) ||
                    a.Job.Title.Contains(searchTerm));
            }

            // Apply sorting
            applications = SortApplications(applications, sortColumn, sortDirection);

            // Paginate
            var pagedApplications = applications.ToPagedList(pageNumber, pageSize);
            // Return partial view if AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_ApplicationListPartialView", pagedApplications);
            }

            // For non-AJAX requests, return full view
            ViewBag.SelectedStatus = status;
            ViewBag.SelectedJobId = jobId;
            ViewBag.Jobs = db.Jobs.Where(j => j.EmployerId == currentEmployerId).ToList();
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            return View("CheckApplications", pagedApplications);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error filtering applications");
            return StatusCode(500, "Error loading applications");
        }
    }

    private IQueryable<Application> SortApplications(IQueryable<Application> query, string sortColumn, string sortDirection)
    {
        switch (sortColumn)
        {
            case "JobSeeker.FullName":
                query = sortDirection == "Ascending"
                    ? query.OrderBy(a => a.JobSeeker.FullName)
                    : query.OrderByDescending(a => a.JobSeeker.FullName);
                break;
            case "JobSeeker.Email":
                query = sortDirection == "Ascending"
                    ? query.OrderBy(a => a.JobSeeker.Email)
                    : query.OrderByDescending(a => a.JobSeeker.Email);
                break;
            case "JobSeeker.ExperienceLevel":
                query = sortDirection == "Ascending"
                    ? query.OrderBy(a => a.JobSeeker.ExperienceLevel)
                    : query.OrderByDescending(a => a.JobSeeker.ExperienceLevel);
                break;
            case "AppliedDate":
                query = sortDirection == "Ascending"
                    ? query.OrderBy(a => a.AppliedDate)
                    : query.OrderByDescending(a => a.AppliedDate);
                break;
            case "Job.Title":
                query = sortDirection == "Ascending"
                    ? query.OrderBy(a => a.Job.Title)
                    : query.OrderByDescending(a => a.Job.Title);
                break;
            default:
                query = query.OrderByDescending(a => a.AppliedDate);
                break;
        }

        return query;
    }

    public IActionResult DownloadResume(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            return NotFound();

        // Add .pdf extension if missing
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".pdf";
        }

        var resumesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads/resumes");
        var filePath = Path.Combine(resumesPath, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            // Debug: Log the file path that was checked
            Console.WriteLine($"File not found: {filePath}");

            // List available files for debugging
            if (Directory.Exists(resumesPath))
            {
                var files = Directory.GetFiles(resumesPath);
                Console.WriteLine("Available files:");
                foreach (var file in files)
                {
                    Console.WriteLine($"- {Path.GetFileName(file)}");
                }
            }

            return NotFound();
        }

        return PhysicalFile(filePath, "application/pdf", Path.GetFileName(filePath));
    }

    // Also update the ViewResume method similarly
    public IActionResult ViewResume(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return NotFound();

        // Add .pdf extension if missing
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".pdf";
        }

        var resumesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads/resumes");
        var filePath = Path.Combine(resumesPath, fileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        // Set content disposition to inline to view in browser
        Response.Headers.Append("Content-Disposition", "inline; filename=" + Path.GetFileName(filePath));
        return PhysicalFile(filePath, "application/pdf");
    }

    [HttpGet]
    public async Task<IActionResult> GetApplicationDetails(string id)
    {
        var currentEmployerId = GetCurrentEmployerId();

        if (string.IsNullOrEmpty(id))
            return Content("<div class='alert alert-danger'>Application ID is required</div>");

        var application = await db.Applications
            .Include(a => a.Job)
            .Include(a => a.JobSeeker)
            .Include(a => a.Job.Employer)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application == null)
            return Content("<div class='alert alert-danger'>Application not found or access denied</div>");

        return PartialView("_ApplicationDetailsPartial", application);
    }

    // Interview Conflict Detection Method
    private bool HasInterviewConflict(DateTime interviewStart, DateTime interviewEnd, string excludeApplicationId = null)
    {
        var currentEmployerId = GetCurrentEmployerId();

        var conflictingInterviews = db.Applications
            .Where(a => a.InterviewDate.HasValue &&
                       a.InterviewEndDate.HasValue &&
                       a.Id != excludeApplicationId &&
                       a.Status != ApplicationStatusEnum.Rejected &&
                       a.Status != ApplicationStatusEnum.Hired &&
                       a.Job.EmployerId == currentEmployerId && // ← Add this filter
                       (
                           // Case 1: New interview starts during existing interview
                           (interviewStart >= a.InterviewDate.Value && interviewStart < a.InterviewEndDate.Value) ||
                           // Case 2: New interview ends during existing interview
                           (interviewEnd > a.InterviewDate.Value && interviewEnd <= a.InterviewEndDate.Value) ||
                           // Case 3: New interview completely contains existing interview
                           (interviewStart <= a.InterviewDate.Value && interviewEnd >= a.InterviewEndDate.Value) ||
                           // Case 4: Existing interview completely contains new interview
                           (a.InterviewDate.Value <= interviewStart && a.InterviewEndDate.Value >= interviewEnd)
                       ))
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ToList();

        if (conflictingInterviews.Any())
        {
            logger.LogWarning("Interview conflict detected for {Start} to {End}. Conflicts: {Count}",
                interviewStart, interviewEnd, conflictingInterviews.Count);
            return true;
        }

        return false;
    }

    // Helper method to get conflict details for error messages
    private string GetConflictDetails(List<Application> conflictingInterviews)
    {
        var conflictDetails = new List<string>();

        foreach (var conflict in conflictingInterviews)
        {
            conflictDetails.Add(
                $"{conflict.JobSeeker?.FullName ?? "Unknown"} " +
                $"({conflict.Job?.Title ?? "Unknown Position"}) " +
                $"{conflict.InterviewDate.Value:MMM dd, yyyy hh:mm tt} - " +
                $"{conflict.InterviewEndDate.Value:hh:mm tt}"
            );
        }

        return string.Join("; ", conflictDetails);
    }

    [HttpGet]
    [HttpGet]
    public IActionResult CheckInterviewConflict(DateTime startTime, DateTime endTime, string excludeApplicationId = null)
    {
        var currentEmployerId = GetCurrentEmployerId();

        try
        {
            bool hasConflict = HasInterviewConflict(startTime, endTime, excludeApplicationId);

            if (hasConflict)
            {
                // Get conflict details - only for current employer
                var conflictingInterviews = db.Applications
                    .Where(a => a.InterviewDate.HasValue &&
                               a.InterviewEndDate.HasValue &&
                               a.Id != excludeApplicationId &&
                               a.Status != ApplicationStatusEnum.Rejected &&
                               a.Status != ApplicationStatusEnum.Hired &&
                               a.Job.EmployerId == currentEmployerId && // ← Add this filter
                               (
                                   (startTime >= a.InterviewDate.Value && startTime < a.InterviewEndDate.Value) ||
                                   (endTime > a.InterviewDate.Value && endTime <= a.InterviewEndDate.Value) ||
                                   (startTime <= a.InterviewDate.Value && endTime >= a.InterviewEndDate.Value) ||
                                   (a.InterviewDate.Value <= startTime && a.InterviewEndDate.Value >= endTime)
                               ))
                    .Include(a => a.JobSeeker)
                    .Include(a => a.Job)
                    .ToList();

                return Json(new
                {
                    hasConflict = true,
                    message = "Time slot conflicts with existing interview(s): " + GetConflictDetails(conflictingInterviews),
                    conflicts = conflictingInterviews.Select(c => new
                    {
                        applicantName = c.JobSeeker?.FullName,
                        jobTitle = c.Job?.Title,
                        startTime = c.InterviewDate.Value.ToString("MMM dd, yyyy hh:mm tt"),
                        endTime = c.InterviewEndDate.Value.ToString("hh:mm tt")
                    })
                });
            }

            return Json(new { hasConflict = false, message = "Time slot is available" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking interview conflict");
            return Json(new { hasConflict = false, message = "Error checking availability" });
        }
    }

    // Add this method to your ApplicationController
    [HttpPost]
    public async Task<IActionResult> CancelInterview([FromBody] CancelInterviewRequest request)
    {
        var currentEmployerId = GetCurrentEmployerId();
        try
        {
            var application = await db.Applications
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
            .ThenInclude(j => j.Employer)
            .Where(a => a.Job.EmployerId == currentEmployerId) // ← Add security check
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId);

            if (application == null)
            {
                return Json(new { success = false, message = "Application not found or access denied" });
            }

            // Store old interview details for email
            var oldInterviewDate = application.InterviewDate;
            var oldInterviewLocation = application.InterviewLocation;

            // Reset to Shortlisted and clear interview details
            application.Status = ApplicationStatusEnum.Shortlisted;
            application.InterviewDate = null;
            application.InterviewEndDate = null;
            application.InterviewLocation = null;
            application.InterviewNotes = null;
            application.InterviewerInfo = null;
            application.CancellationReason = request.CancellationReason;
            application.CancellationDate = DateTime.Now;

            await db.SaveChangesAsync();

            // Send cancellation email
            await SendCancellationEmail(application, request.CancellationReason, oldInterviewDate, oldInterviewLocation);

            TempData["SuccessMessage"] = "Interview cancelled successfully!";
            TempData["SuccessTitle"] = "Cancelled!";

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            TempData["ErrorTitle"] = "Cancellation Failed";

            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task SendCancellationEmail(Application application, string reason,
        DateTime? originalDate, string originalLocation)
    {
        try
        {
            var emailModel = new InterviewCancellationModel
            {
                CandidateName = application.JobSeeker.FullName,
                JobTitle = application.Job.Title,
                CompanyName = application.Job.Employer.CompanyName,
                OriginalDate = originalDate,
                OriginalLocation = originalLocation,
                CancellationReason = reason,
                ContactEmail = "hiredrightpro@gmail.com" // Your HR email
            };

            await helper.SendInterviewCancelledEmailAsync(
                application.JobSeeker.Email,
                application.JobSeeker.FullName,
                emailModel
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send cancellation email to {Email}", application.JobSeeker.Email);
            // Don't throw - we don't want email failure to prevent cancellation
        }
    }

    public ActionResult ApplicationDetails(string id)
    {
        var currentEmployerId = GetCurrentEmployerId();

        var application = db.Applications
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
                .ThenInclude(j => j.Employer)
            .Include(a => a.Job)  // Add this include for QuestionSet
                .ThenInclude(j => j.QuestionSet)
            .Include(a => a.Job.QuestionSet)  // Include the QuestionSet itself
                .ThenInclude(qs => qs.Questions)  // Include questions within the QuestionSet
            .Include(a => a.QuestionResponses)  // Include question responses
                .ThenInclude(qr => qr.Question)  // Include the question for each response
            .Where(a => a.Job.EmployerId == currentEmployerId)
            .FirstOrDefault(a => a.Id == id);

        if (application == null)
        {
            return NotFound();
        }

        // Calculate completeness rate
        var responses = application.QuestionResponses?.ToList() ?? new List<QuestionResponse>();
        var answeredCount = responses.Count(qr => !string.IsNullOrEmpty(qr.Answer) && qr.Answer.Trim() != "");
        var totalCount = application.Job?.QuestionSet?.Questions?.Count ?? 0;

        // If no questions in the set, set completeness to 100%
        var completenessRate = totalCount > 0 ? (double)answeredCount / totalCount * 100 : 100;

        ViewBag.CompletenessRate = completenessRate;

        return View(application);
    }

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

            // Calculate completeness rate if needed
            double completenessRate = 0;
            if (app.Job?.QuestionSet != null && app.Job.QuestionSet.Questions.Any())
            {
                var totalQuestions = app.Job.QuestionSet.Questions.Count;
                var answeredQuestions = app.QuestionResponses.Count(r =>
                    !string.IsNullOrEmpty(r.Answer) && r.Answer.Trim() != "");
                completenessRate = totalQuestions > 0 ? (double)answeredQuestions / totalQuestions * 100 : 100;
            }

            var vm = new ApplicationDetailsViewModel
            {
                Application = app,
                CompletenessRate = completenessRate
            };

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

    [HttpGet]
    public async Task<IActionResult> ReportApplicant(string applicationId)
    {
        var currentEmployerId = GetCurrentEmployerId();

        if (string.IsNullOrEmpty(applicationId))
        {
            TempData["ErrorMessage"] = "Application ID is required.";
            return RedirectToAction("CheckApplications");
        }

        // Get the application and verify access
        var application = await db.Applications
            .Include(a => a.JobSeeker)
            .Include(a => a.Job)
                .ThenInclude(j => j.Employer)
            .Where(a => a.Job.EmployerId == currentEmployerId)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
        {
            TempData["ErrorMessage"] = "Application not found or access denied.";
            return RedirectToAction("CheckApplications");
        }

        // Check if already reported
        var existingReport = await db.UserReports
            .FirstOrDefaultAsync(r => r.ReportedUserId == application.JobSeekerId &&
                                     r.EmployerId == currentEmployerId);

        if (existingReport != null)
        {
            TempData["ErrorMessage"] = "You have already reported this applicant.";
            return RedirectToAction("ApplicationDetails", new { id = applicationId });
        }

        var model = new ReportApplicantViewModel
        {
            ApplicationId = applicationId,
            ApplicantName = application.JobSeeker?.FullName ?? "Unknown",
            ApplicantEmail = application.JobSeeker?.Email ?? "Unknown",
            JobTitle = application.Job?.Title ?? "Unknown",
            CompanyName = application.Job?.Employer?.CompanyName ?? "Unknown",
            ReportedUserId = application.JobSeekerId
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportApplicant(ReportApplicantViewModel model)
    {
        var currentEmployerId = GetCurrentEmployerId();

        if (!ModelState.IsValid)
        {
            // Re-populate the applicant info if validation fails
            var application = await db.Applications
                .Include(a => a.JobSeeker)
                .Include(a => a.Job)
                    .ThenInclude(j => j.Employer)
                .Where(a => a.Job.EmployerId == currentEmployerId)
                .FirstOrDefaultAsync(a => a.Id == model.ApplicationId);

            if (application != null)
            {
                model.ApplicantName = application.JobSeeker?.FullName ?? "Unknown";
                model.ApplicantEmail = application.JobSeeker?.Email ?? "Unknown";
                model.JobTitle = application.Job?.Title ?? "Unknown";
                model.CompanyName = application.Job?.Employer?.CompanyName ?? "Unknown";
            }

            return View(model);
        }

        try
        {
            // Verify access and get application details
            var application = await db.Applications
                .Include(a => a.Job)
                .Where(a => a.Job.EmployerId == currentEmployerId)
                .FirstOrDefaultAsync(a => a.Id == model.ApplicationId);

            if (application == null)
            {
                TempData["ErrorMessage"] = "Application not found or access denied.";
                return RedirectToAction("CheckApplications");
            }

            // Check for existing report again (in case of race condition)
            var existingReport = await db.UserReports
                .FirstOrDefaultAsync(r => r.ReportedUserId == application.JobSeekerId &&
                                         r.EmployerId == currentEmployerId);

            if (existingReport != null)
            {
                TempData["ErrorMessage"] = "You have already reported this applicant.";
                return RedirectToAction("ApplicationDetails", new { id = model.ApplicationId });
            }

            // Create the report
            var report = new UserReport
            {
                Id = GenerateReportId(),
                ReportedUserId = application.JobSeekerId,
                EmployerId = currentEmployerId,
                Reason = model.Reason + (!string.IsNullOrEmpty(model.Details) ? $": {model.Details}" : ""),
                DateReported = DateTime.UtcNow
            };

            db.UserReports.Add(report);
            await db.SaveChangesAsync();

            logger.LogInformation("User report created: {ReportId} by employer {EmployerId} against user {UserId}",
                report.Id, currentEmployerId, application.JobSeekerId);

            TempData["SuccessMessage"] = "Report submitted successfully. Our admin team will review it.";
            return RedirectToAction("ApplicationDetails", new { id = model.ApplicationId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating user report");
            TempData["ErrorMessage"] = "An error occurred while submitting the report. Please try again.";
            return View(model);
        }
    }

    public class ReportApplicantViewModel
    {
        [Required]
        public string ApplicationId { get; set; }

        [Required]
        public string ReportedUserId { get; set; }

        [Required(ErrorMessage = "Please select a reason for your report")]
        public string Reason { get; set; }

        [Required(ErrorMessage = "Please provide details about your report")]
        [StringLength(1000, ErrorMessage = "Details cannot exceed 1000 characters")]
        public string Details { get; set; }

        // Display properties (not submitted)
        public string ApplicantName { get; set; }
        public string ApplicantEmail { get; set; }
        public string JobTitle { get; set; }
        public string CompanyName { get; set; }
    }


}