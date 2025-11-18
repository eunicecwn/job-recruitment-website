using Demo.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using JobRecruitment.Models.ViewModels;
using JobRecruitment.Services;
using JobRecruitment.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;

namespace Demo.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly DB _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AdminController> _logger;
        private readonly OpenAIService _openAIService;
        private readonly IEmailSender _emailSender;

        public AdminController(DB db, IWebHostEnvironment env, ILogger<AdminController> logger, OpenAIService openAIService, IEmailSender emailSender)
        {
            _db = db;
            _env = env;
            _logger = logger;
            _openAIService = openAIService;
            _emailSender = emailSender;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            SetCacheHeaders();
            SetViewBagUserInfo();
            base.OnActionExecuting(context);
        }

        // ========== DASHBOARD ==========
        public async Task<IActionResult> AdminDashboard()
        {
            try
            {
                _logger.LogInformation($"Admin dashboard accessed by user: {GetCurrentUserName()}");

                var dashboardData = new AdminDashboardViewModel
                {
                    Stats = await GetDashboardStats(),
                    ChartData = await GetChartDataSafely(),
                    TopEmployers = await GetTopEmployersSafely(),
                    PerformanceMetrics = await GetPerformanceMetricsSafely(),
                    RecentActivities = await GetRecentActivitiesSafely()
                };

                return View(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                return View(CreateErrorDashboard("Error loading dashboard data"));
            }
        }

        // ========== ENHANCED FILTERING API ENDPOINTS (NEW) ==========

        [HttpPost]
        public async Task<IActionResult> GetTopUsersChart([FromBody] TopUsersFilterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid request parameters" });
                }

                var dateRange = GetDateRangeFromDays(request.DateRange);
                var data = await BuildTopUsersChartDataFromDB(request, dateRange);
                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top users chart data");
                return Json(new { success = false, message = "Failed to load chart data" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetActivityChart([FromBody] ActivityFilterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid request parameters" });
                }

                var dateRange = GetDateRangeFromDays(request.DateRange);
                var activityData = await GetUserActivityFromDB(request, dateRange);

                var chartData = new
                {
                    labels = activityData.Select(a => a.UserDisplayName).ToArray(),
                    datasets = new[]
                    {
                        new
                        {
                            label = "Activity Score",
                            data = activityData.Select(a => a.ActivityScore).ToArray(),
                            borderColor = "#06B6D4",
                            backgroundColor = "#06B6D420",
                            tension = 0.4,
                            fill = true
                        }
                    }
                };

                return Json(new { success = true, data = chartData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting activity chart data");
                return Json(new { success = false, message = "Failed to load activity data" });
            }
        }
// Payment 主页面
        // Full page load (with layout, filters, etc.)
        [HttpGet]
        public IActionResult Payments(string searchTerm = "", string status = "", int page = 1, int pageSize = 10)
        {
            var query = _db.Payments
                .Include(p => p.User)
                .Include(p => p.Subscription)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p =>
                    p.Id.ToString().Contains(searchTerm) ||
                    p.User.FullName.Contains(searchTerm) ||
                    p.PaymentMethod.Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var total = query.Count();
            var payments = query
                .OrderByDescending(p => p.PaymentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // summary stats
            ViewBag.TotalPayments = total;
            ViewBag.CompletedPayments = _db.Payments.Count(p => p.Status == "Completed");
            ViewBag.PendingPayments = _db.Payments.Count(p => p.Status == "Pending");
            ViewBag.FailedPayments = _db.Payments.Count(p => p.Status == "Failed");

            // keep current filter values
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.SearchTerm = searchTerm;
            ViewBag.FilterStatus = status;

            return View(payments); // returns Payments.cshtml (with layout)
        }

        // AJAX-only endpoint (returns just the table)
        [HttpGet]
        public IActionResult LoadPayments(string searchTerm = "", string status = "", int page = 1, int pageSize = 10)
        {
            var query = _db.Payments
                .Include(p => p.User)
                .Include(p => p.Subscription)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p =>
                    p.Id.ToString().Contains(searchTerm) ||
                    p.User.FullName.Contains(searchTerm) ||
                    p.PaymentMethod.Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var total = query.Count();
            var payments = query
                .OrderByDescending(p => p.PaymentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.SearchTerm = searchTerm;
            ViewBag.FilterStatus = status;

            return PartialView("_PaymentsTable", payments); // only table part
        }

        public IActionResult ExportPaymentsPdf(string searchTerm, string status)
{
    var query = _db.Payments
        .Include(p => p.User)
        .Include(p => p.Subscription)
        .AsQueryable();

    if (!string.IsNullOrEmpty(searchTerm))
    {
        query = query.Where(p =>
            p.Id.ToString().Contains(searchTerm) ||
            p.User.FullName.Contains(searchTerm) ||
            p.PaymentMethod.Contains(searchTerm));
    }

    if (!string.IsNullOrEmpty(status))
    {
        query = query.Where(p => p.Status == status);
    }

    var payments = query.OrderByDescending(p => p.PaymentDate).ToList();

    using (var ms = new MemoryStream())
    {
        Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
        PdfWriter.GetInstance(doc, ms);
        doc.Open();

        // Title
        var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
        var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

        doc.Add(new Paragraph("Payment Report", titleFont));
        doc.Add(new Paragraph($"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
        doc.Add(new Paragraph(" "));

        // Table
        PdfPTable table = new PdfPTable(6);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 15, 20, 15, 15, 15, 20 });

        // Header
        string[] headers = { "Payment ID", "User", "Amount", "Method", "Status", "Date" };
        foreach (var header in headers)
        {
            PdfPCell cell = new PdfPCell(new Phrase(header, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LIGHT_GRAY,
                HorizontalAlignment = Element.ALIGN_CENTER
            };
            table.AddCell(cell);
            }// Rows// Rows
foreach (var p in payments)
{
    table.AddCell(new Phrase(p.Id.ToString(), normalFont)); // ✅ Ensure string
    table.AddCell(new Phrase(p.User?.FullName ?? "N/A", normalFont));
    table.AddCell(new Phrase(p.Amount.ToString("C"), normalFont));
    table.AddCell(new Phrase(p.PaymentMethod ?? "N/A", normalFont));
    table.AddCell(new Phrase(p.Status ?? "N/A", normalFont));
    table.AddCell(new Phrase(p.PaymentDate.ToString("yyyy-MM-dd HH:mm"), normalFont));
}



            doc.Add(table);
        doc.Close();

        return File(ms.ToArray(), "application/pdf", "PaymentsReport.pdf");
    }
}

        // Payment Detail
        public IActionResult PaymentDetail(string id)
        {
            var payment = _db.Payments
                .Include(p => p.User)
                .Include(p => p.Subscription)
                .FirstOrDefault(p => p.Id == id);

            if (payment == null) return NotFound();

            return View(payment);
        }
        [HttpPost]
        public async Task<IActionResult> GetEmployerRankings([FromBody] EmployerFilterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid request parameters" });
                }

                var dateRange = GetDateRangeFromDays(request.DateRange);
                var employers = await GetEmployerRankingsFromDB(request, dateRange);

                var result = employers.Select((emp, index) => new
                {
                    rank = index + 1,
                    company = emp.CompanyName,
                    jobsPosted = emp.JobsPosted,
                    applications = emp.TotalApplications,
                    successRate = emp.SuccessRate,
                    responseTime = emp.AverageResponseTime
                });

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting employer rankings");
                return Json(new { success = false, message = "Failed to load employer data" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetJobSeekerRankings([FromBody] JobSeekerFilterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid request parameters" });
                }

                var dateRange = GetDateRangeFromDays(request.DateRange);
                var jobSeekers = await GetJobSeekerRankingsFromDB(request, dateRange);

                var result = jobSeekers.Select((seeker, index) => new
                {
                    rank = index + 1,
                    name = seeker.FullName,
                    applications = seeker.TotalApplications,
                    interviewRate = seeker.InterviewRate,
                    lastActivity = GetTimeAgo(seeker.LastActivity),
                    profileCompletion = seeker.ProfileCompletionPercentage
                });

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job seeker rankings");
                return Json(new { success = false, message = "Failed to load job seeker data" });
            }
        }

        // ========== ENHANCED DATABASE QUERY METHODS (NEW) ==========

        private async Task<object> BuildTopUsersChartDataFromDB(TopUsersFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            return request.UserType.ToLower() switch
            {
                "employers" => await GetTopEmployersChartFromDB(request, dateRange),
                "jobseekers" => await GetTopJobSeekersChartFromDB(request, dateRange),
                _ => await GetMixedTopUsersChartFromDB(request, dateRange)
            };
        }

        private async Task<object> GetTopEmployersChartFromDB(TopUsersFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var employersQuery = _db.Employers
                .Where(e => e.IsActive && e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate)
                .Include(e => e.Jobs)
                .ThenInclude(j => j.Applications);

            var employers = await employersQuery.ToListAsync();

            var employerData = employers.Select(e => new
            {
                CompanyName = e.CompanyName,
                TotalApplications = e.Jobs.SelectMany(j => j.Applications)
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate),
                JobsPosted = e.Jobs.Count(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate),
                SuccessRate = CalculateEmployerSuccessRateFromDB(e, dateRange),
                RecentActivityScore = CalculateRecentActivityScore(e.CreatedDate)
            }).Where(e => e.JobsPosted > 0 || e.TotalApplications > 0).ToList();

            // Apply sorting
            employerData = request.SortBy switch
            {
                "applications" => request.Order == "desc"
                    ? employerData.OrderByDescending(e => e.TotalApplications).ToList()
                    : employerData.OrderBy(e => e.TotalApplications).ToList(),
                "success_rate" => request.Order == "desc"
                    ? employerData.OrderByDescending(e => e.SuccessRate).ToList()
                    : employerData.OrderBy(e => e.SuccessRate).ToList(),
                "jobs_posted" => request.Order == "desc"
                    ? employerData.OrderByDescending(e => e.JobsPosted).ToList()
                    : employerData.OrderBy(e => e.JobsPosted).ToList(),
                "recent_activity" => request.Order == "desc"
                    ? employerData.OrderByDescending(e => e.RecentActivityScore).ToList()
                    : employerData.OrderBy(e => e.RecentActivityScore).ToList(),
                _ => employerData.OrderByDescending(e => e.TotalApplications).ToList()
            };

            var topEmployers = employerData.Take(request.Count);

            return new
            {
                labels = topEmployers.Select(e => TruncateText(e.CompanyName, 12)).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = FormatSortByLabel(request.SortBy),
                        data = topEmployers.Select(e => GetEmployerValueBySort(e, request.SortBy)).ToArray(),
                        backgroundColor = "#4F46E5",
                        borderRadius = 4
                    }
                }
            };
        }

        private async Task<object> GetTopJobSeekersChartFromDB(TopUsersFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var jobSeekersQuery = _db.JobSeekers
                .Where(js => js.IsActive && js.CreatedDate >= dateRange.StartDate && js.CreatedDate <= dateRange.EndDate)
                .Include(js => js.Applications);

            var jobSeekers = await jobSeekersQuery.ToListAsync();

            var jobSeekerData = jobSeekers.Select(js => new
            {
                FullName = js.FullName,
                TotalApplications = js.Applications
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate),
                InterviewRate = CalculateInterviewRateFromDB(js, dateRange),
                RecentActivityScore = CalculateRecentActivityScore(js.CreatedDate)
            }).Where(js => js.TotalApplications > 0).ToList();

            // Apply sorting
            jobSeekerData = request.SortBy switch
            {
                "applications" => request.Order == "desc"
                    ? jobSeekerData.OrderByDescending(js => js.TotalApplications).ToList()
                    : jobSeekerData.OrderBy(js => js.TotalApplications).ToList(),
                "success_rate" => request.Order == "desc"
                    ? jobSeekerData.OrderByDescending(js => js.InterviewRate).ToList()
                    : jobSeekerData.OrderBy(js => js.InterviewRate).ToList(),
                "recent_activity" => request.Order == "desc"
                    ? jobSeekerData.OrderByDescending(js => js.RecentActivityScore).ToList()
                    : jobSeekerData.OrderBy(js => js.RecentActivityScore).ToList(),
                _ => jobSeekerData.OrderByDescending(js => js.TotalApplications).ToList()
            };

            var topJobSeekers = jobSeekerData.Take(request.Count);

            return new
            {
                labels = topJobSeekers.Select(js => TruncateText(js.FullName, 15)).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = FormatSortByLabel(request.SortBy),
                        data = topJobSeekers.Select(js => GetJobSeekerValueBySort(js, request.SortBy)).ToArray(),
                        backgroundColor = "#10B981",
                        borderRadius = 4
                    }
                }
            };
        }

        private async Task<object> GetMixedTopUsersChartFromDB(TopUsersFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var mixedUsers = new List<dynamic>();

            // Get employers with their activity scores
            var employers = await _db.Employers
                .Where(e => e.IsActive && e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate)
                .Include(e => e.Jobs)
                .ThenInclude(j => j.Applications)
                .ToListAsync();

            foreach (var employer in employers)
            {
                var applications = employer.Jobs.SelectMany(j => j.Applications)
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate);

                if (applications > 0)
                {
                    mixedUsers.Add(new
                    {
                        DisplayName = employer.CompanyName,
                        Score = applications,
                        UserType = "Employer"
                    });
                }
            }

            // Get job seekers with their activity scores
            var jobSeekers = await _db.JobSeekers
                .Where(js => js.IsActive && js.CreatedDate >= dateRange.StartDate && js.CreatedDate <= dateRange.EndDate)
                .Include(js => js.Applications)
                .ToListAsync();

            foreach (var jobSeeker in jobSeekers)
            {
                var applications = jobSeeker.Applications
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate);

                if (applications > 0)
                {
                    mixedUsers.Add(new
                    {
                        DisplayName = jobSeeker.FullName,
                        Score = applications,
                        UserType = "JobSeeker"
                    });
                }
            }

            // Sort and take top
            var sortedUsers = request.Order == "desc"
                ? mixedUsers.OrderByDescending(u => u.Score).Take(request.Count)
                : mixedUsers.OrderBy(u => u.Score).Take(request.Count);

            return new
            {
                labels = sortedUsers.Select(u => TruncateText(u.DisplayName, 12)).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = "Activity Score",
                        data = sortedUsers.Select(u => u.Score).ToArray(),
                        backgroundColor = sortedUsers.Select(u => u.UserType == "Employer" ? "#4F46E5" : "#10B981").ToArray(),
                        borderRadius = 4
                    }
                }
            };
        }

        private async Task<List<UserActivityData>> GetUserActivityFromDB(ActivityFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var activityData = new List<UserActivityData>();

            if (request.ActivityType == "all" || request.ActivityType == "applications")
            {
                var jobSeekers = await _db.JobSeekers
                    .Where(js => js.IsActive)
                    .Include(js => js.Applications.Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate))
                    .ToListAsync();

                activityData.AddRange(jobSeekers
                    .Where(js => js.Applications.Any())
                    .Select(js => new UserActivityData
                    {
                        UserDisplayName = js.FullName,
                        ActivityScore = js.Applications.Count(),
                        UserType = "JobSeeker"
                    }));
            }

            if (request.ActivityType == "all" || request.ActivityType == "job_posts")
            {
                var employers = await _db.Employers
                    .Where(e => e.IsActive)
                    .Include(e => e.Jobs.Where(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate))
                    .ToListAsync();

                activityData.AddRange(employers
                    .Where(e => e.Jobs.Any())
                    .Select(e => new UserActivityData
                    {
                        UserDisplayName = e.CompanyName,
                        ActivityScore = e.Jobs.Count(),
                        UserType = "Employer"
                    }));
            }

            if (request.ActivityType == "logins")
            {
                // If you track login activity, implement here
                // For now, we'll use creation date as a proxy
                var users = await _db.Users
                    .Where(u => u.IsActive && u.CreatedDate >= dateRange.StartDate && u.CreatedDate <= dateRange.EndDate)
                    .ToListAsync();

                activityData.AddRange(users.Select(u => new UserActivityData
                {
                    UserDisplayName = u.FullName,
                    ActivityScore = CalculateLoginActivityScore(u),
                    UserType = u.Role
                }));
            }

            // Sort and limit
            var sortedData = activityData
                .OrderByDescending(a => a.ActivityScore)
                .Take(request.Count == 999 ? activityData.Count : request.Count)
                .ToList();

            return sortedData;
        }

        private async Task<List<EmployerRankingData>> GetEmployerRankingsFromDB(EmployerFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var employersQuery = _db.Employers
                .Where(e => e.IsActive && e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate)
                .Include(e => e.Jobs)
                .ThenInclude(j => j.Applications);

            var employers = await employersQuery.ToListAsync();

            var employerData = employers.Select(e => new EmployerRankingData
            {
                CompanyName = e.CompanyName,
                JobsPosted = e.Jobs.Count(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate),
                TotalApplications = e.Jobs.SelectMany(j => j.Applications)
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate),
                SuccessRate = CalculateEmployerSuccessRateFromDB(e, dateRange),
                AverageResponseTime = CalculateAverageResponseTimeFromDB(e, dateRange)
            }).Where(e => e.JobsPosted > 0 || e.TotalApplications > 0).ToList();

            // Apply sorting
            employerData = request.SortBy switch
            {
                "success_rate" => employerData.OrderByDescending(e => e.SuccessRate).ToList(),
                "total_applications" => employerData.OrderByDescending(e => e.TotalApplications).ToList(),
                "jobs_posted" => employerData.OrderByDescending(e => e.JobsPosted).ToList(),
                _ => employerData.OrderByDescending(e => e.SuccessRate).ToList()
            };

            return employerData.Take(request.Count == 999 ? employerData.Count : request.Count).ToList();
        }

        private async Task<List<JobSeekerRankingData>> GetJobSeekerRankingsFromDB(JobSeekerFilterRequest request, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var jobSeekersQuery = _db.JobSeekers
                .Where(js => js.IsActive && js.CreatedDate >= dateRange.StartDate && js.CreatedDate <= dateRange.EndDate)
                .Include(js => js.Applications);

            var jobSeekers = await jobSeekersQuery.ToListAsync();

            var jobSeekerData = jobSeekers.Select(js => new JobSeekerRankingData
            {
                FullName = js.FullName,
                TotalApplications = js.Applications
                    .Count(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate),
                InterviewRate = CalculateInterviewRateFromDB(js, dateRange),
                LastActivity = GetLastActivityDate(js),
                ProfileCompletionPercentage = CalculateProfileCompletionFromDB(js)
            }).Where(js => js.TotalApplications > 0).ToList();

            // Apply sorting
            jobSeekerData = request.SortBy switch
            {
                "most_active" => jobSeekerData.OrderByDescending(js => js.TotalApplications).ToList(),
                "least_active" => jobSeekerData.OrderBy(js => js.TotalApplications).ToList(),
                "highest_success" => jobSeekerData.OrderByDescending(js => js.InterviewRate).ToList(),
                "most_applications" => jobSeekerData.OrderByDescending(js => js.TotalApplications).ToList(),
                "recent_joins" => jobSeekerData.OrderByDescending(js => js.LastActivity).ToList(),
                _ => jobSeekerData.OrderByDescending(js => js.TotalApplications).ToList()
            };

            return jobSeekerData.Take(request.Count == 999 ? jobSeekerData.Count : request.Count).ToList();
        }

        // ========== ENHANCED CALCULATION HELPER METHODS (NEW) ==========

        private decimal CalculateEmployerSuccessRateFromDB(Employer employer, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var applications = employer.Jobs.SelectMany(j => j.Applications)
                .Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate)
                .ToList();

            if (!applications.Any()) return 0;

            var successfulHires = applications.Count(a => a.Status == ApplicationStatusEnum.Hired);
            return Math.Round((decimal)successfulHires / applications.Count * 100, 1);
        }

        private decimal CalculateInterviewRateFromDB(JobSeeker jobSeeker, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var applications = jobSeeker.Applications
                .Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate)
                .ToList();

            if (!applications.Any()) return 0;

            var interviews = applications.Count(a =>
                a.Status == ApplicationStatusEnum.Hired ||
                a.Status == ApplicationStatusEnum.OfferSent);

            return Math.Round((decimal)interviews / applications.Count * 100, 1);
        }

        private int CalculateRecentActivityScore(DateTime createdDate)
        {
            var daysSinceCreation = (DateTime.Now - createdDate).Days;
            return Math.Max(0, 100 - daysSinceCreation);
        }

        private string CalculateAverageResponseTimeFromDB(Employer employer, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var applications = employer.Jobs.SelectMany(j => j.Applications)
                .Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate)
                .ToList();

            if (!applications.Any()) return "N/A";

            // Calculate based on status changes - you might need to track this differently
            var avgDays = applications.Where(a => a.Status != ApplicationStatusEnum.Pending)
                .Select(a => (DateTime.Now - a.AppliedDate).Days)
                .DefaultIfEmpty(0)
                .Average();

            return $"{avgDays:F1} days";
        }

        private int CalculateProfileCompletionFromDB(JobSeeker jobSeeker)
        {
            var completionScore = 0;
            var totalFields = 5;

            if (!string.IsNullOrEmpty(jobSeeker.FullName)) completionScore++;
            if (!string.IsNullOrEmpty(jobSeeker.Email)) completionScore++;
            if (!string.IsNullOrEmpty(jobSeeker.Phone)) completionScore++;
            if (jobSeeker.DateOfBirth.HasValue) completionScore++;
            if (!string.IsNullOrEmpty(jobSeeker.ProfilePhotoFileName)) completionScore++;

            return (int)Math.Round((double)completionScore / totalFields * 100);
        }

        private DateTime GetLastActivityDate(JobSeeker jobSeeker)
        {
            var lastApplication = jobSeeker.Applications
                .OrderByDescending(a => a.AppliedDate)
                .FirstOrDefault();

            return lastApplication?.AppliedDate ?? jobSeeker.CreatedDate;
        }

        private int CalculateLoginActivityScore(UserBase user)
        {
            // If you have login tracking, implement here
            // For now, return a score based on account age
            var daysSinceCreation = (DateTime.Now - user.CreatedDate).Days;
            return Math.Max(1, Math.Min(100, 100 - daysSinceCreation));
        }

        private decimal GetEmployerValueBySort(dynamic employer, string sortBy)
        {
            return sortBy switch
            {
                "applications" => employer.TotalApplications,
                "success_rate" => employer.SuccessRate,
                "jobs_posted" => employer.JobsPosted,
                "recent_activity" => employer.RecentActivityScore,
                _ => employer.TotalApplications
            };
        }

        private decimal GetJobSeekerValueBySort(dynamic jobSeeker, string sortBy)
        {
            return sortBy switch
            {
                "applications" => jobSeeker.TotalApplications,
                "success_rate" => jobSeeker.InterviewRate,
                "recent_activity" => jobSeeker.RecentActivityScore,
                _ => jobSeeker.TotalApplications
            };
        }

        private (DateTime StartDate, DateTime EndDate) GetDateRangeFromDays(int days)
        {
            var endDate = DateTime.Now;
            var startDate = endDate.AddDays(-days);
            return (startDate, endDate);
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "Unknown";
            return text.Length > maxLength ? text.Substring(0, maxLength - 3) + "..." : text;
        }

        private string FormatSortByLabel(string sortBy)
        {
            return sortBy switch
            {
                "applications" => "Applications",
                "success_rate" => "Success Rate (%)",
                "jobs_posted" => "Jobs Posted",
                "recent_activity" => "Activity Score",
                _ => "Count"
            };
        }

        // ========== ORIGINAL FILTERING API ENDPOINTS (UNCHANGED) ==========
        [HttpPost]
        public async Task<IActionResult> GetFilteredDashboardData([FromBody] DashboardFilterRequest request)
        {
            try
            {
                var response = new FilteredDashboardResponse
                {
                    Stats = await GetFilteredDashboardStats(request),
                    ChartData = await GetFilteredChartData(request),
                    TopEmployers = await GetFilteredTopEmployers(request),
                    RecentActivities = await GetFilteredRecentActivities(request),
                    FilterSummary = GenerateFilterSummary(request)
                };

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting filtered dashboard data");
                return Json(new { error = "Failed to load filtered data" });
            }
        }

        // ========== FILTERED DASHBOARD DATA METHODS (UNCHANGED) ==========
        private async Task<DashboardStatsViewModel> GetFilteredDashboardStats(DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            var userQuery = ApplyUserFilters(_db.Users.AsQueryable(), filter);
            var employerQuery = ApplyEmployerFilters(_db.Employers.AsQueryable(), filter);
            var jobSeekerQuery = ApplyJobSeekerFilters(_db.JobSeekers.AsQueryable(), filter);

            return new DashboardStatsViewModel
            {
                TotalUsers = await userQuery.CountAsync(),
                TotalEmployers = await employerQuery.CountAsync(),
                TotalJobSeekers = await jobSeekerQuery.CountAsync(),
                TotalAdmins = await ApplyAdminFilters(_db.Admins.AsQueryable(), filter).CountAsync(),
                TotalJobs = await ApplyJobFilters(_db.Jobs.AsQueryable(), filter).CountAsync(),
                TotalApplications = await ApplyApplicationFilters(_db.Applications.AsQueryable(), filter).CountAsync(),
                PendingEmployers = await employerQuery.CountAsync(e => e.ApprovalStatus == "Pending")
            };
        }

        private async Task<ChartDataViewModel> GetFilteredChartData(DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            var startDate = dateRange.StartDate;
            var endDate = dateRange.EndDate;

            // Generate month labels based on filter
            var monthLabels = GenerateFilteredMonthLabels(filter);
            var trendsData = await GetFilteredTrendsData(filter, startDate, endDate);

            return new ChartDataViewModel
            {
                MonthLabels = monthLabels,
                JobSeekerTrends = trendsData.JobSeekerTrends,
                EmployerTrends = trendsData.EmployerTrends,
                ApplicationTrends = trendsData.ApplicationTrends,
                JobStats = await GetFilteredJobStats(filter)
            };
        }

        private async Task<TrendsDataResult> GetFilteredTrendsData(DashboardFilterRequest filter, DateTime startDate, DateTime endDate)
        {
            var result = new TrendsDataResult();

            // Get filtered data with date range
            var jobSeekerQuery = ApplyJobSeekerFilters(_db.JobSeekers.AsQueryable(), filter)
                .Where(js => js.CreatedDate >= startDate && js.CreatedDate <= endDate);

            var employerQuery = ApplyEmployerFilters(_db.Employers.AsQueryable(), filter)
                .Where(e => e.CreatedDate >= startDate && e.CreatedDate <= endDate);

            var applicationQuery = ApplyApplicationFilters(_db.Applications.AsQueryable(), filter)
                .Where(a => a.AppliedDate >= startDate && a.AppliedDate <= endDate);

            var jobSeekerData = await jobSeekerQuery
                .GroupBy(js => new { js.CreatedDate.Year, js.CreatedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            var employerData = await employerQuery
                .GroupBy(e => new { e.CreatedDate.Year, e.CreatedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            var applicationData = await applicationQuery
                .GroupBy(a => new { a.AppliedDate.Year, a.AppliedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            // Fill trend arrays based on filter period
            var periodMonths = GetPeriodMonths(filter);
            for (int i = 0; i < periodMonths; i++)
            {
                var targetDate = GetTargetDateForPeriod(filter, i);
                var targetKey = new { Year = targetDate.Year, Month = targetDate.Month };

                result.JobSeekerTrends.Add(GetCountForMonth(jobSeekerData, targetKey));
                result.EmployerTrends.Add(GetCountForMonth(employerData, targetKey));
                result.ApplicationTrends.Add(GetCountForMonth(applicationData, targetKey));
            }

            return result;
        }

        private async Task<JobStatsViewModel> GetFilteredJobStats(DashboardFilterRequest filter)
        {
            var jobQuery = ApplyJobFilters(_db.Jobs.AsQueryable(), filter);

            return new JobStatsViewModel
            {
                ActiveJobs = await jobQuery.CountAsync(j => j.Status == JobStatus.Open),
                ClosedJobs = await jobQuery.CountAsync(j => j.Status == JobStatus.Closed),
                DraftJobs = await jobQuery.CountAsync(j => j.Status == JobStatus.Draft),
                PendingJobs = 0
            };
        }

        private async Task<List<TopEmployerViewModel>> GetFilteredTopEmployers(DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            var employerQuery = ApplyEmployerFilters(_db.Employers.AsQueryable(), filter);

            var topEmployers = await employerQuery
                .Where(e => e.IsActive)
                .Select(e => new
                {
                    e.CompanyName,
                    e.Jobs,
                    e.Id
                })
                .ToListAsync();

            var result = new List<TopEmployerViewModel>();

            foreach (var employer in topEmployers)
            {
                var jobs = employer.Jobs.Where(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate);
                var applications = jobs.SelectMany(j => j.Applications).Where(a => a.AppliedDate >= dateRange.StartDate);
                var successfulHires = applications.Count(a => a.Status == ApplicationStatusEnum.Hired);

                // Apply search filter if needed
                if (!string.IsNullOrEmpty(filter.Search) &&
                    !employer.CompanyName.ToLower().Contains(filter.Search.ToLower()))
                    continue;

                var jobsCount = jobs.Count();
                if (jobsCount > 0)
                {
                    result.Add(new TopEmployerViewModel
                    {
                        CompanyName = employer.CompanyName,
                        JobsPosted = jobsCount,
                        TotalApplications = applications.Count(),
                        SuccessfulHires = successfulHires
                    });
                }
            }

            return result.OrderByDescending(te => te.TotalApplications).Take(5).ToList();
        }

        private async Task<List<RecentActivityViewModel>> GetFilteredRecentActivities(DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            var activities = new List<RecentActivityViewModel>();

            // Get filtered activities
            if (filter.UserType == "all" || filter.UserType == "jobseekers")
            {
                activities.AddRange(await GetFilteredJobSeekerActivities(filter, dateRange));
            }

            if (filter.UserType == "all" || filter.UserType == "employers")
            {
                activities.AddRange(await GetFilteredEmployerActivities(filter, dateRange));
                activities.AddRange(await GetFilteredJobActivities(filter, dateRange));
            }

            activities.AddRange(await GetFilteredApplicationActivities(filter, dateRange));

            // Apply search filter
            if (!string.IsNullOrEmpty(filter.Search))
            {
                var searchTerm = filter.Search.ToLower();
                activities = activities.Where(a =>
                    a.UserOrCompany.ToLower().Contains(searchTerm) ||
                    a.Description.ToLower().Contains(searchTerm))
                    .ToList();
            }

            return activities.OrderByDescending(a => a.CreatedDate).Take(10).ToList();
        }

        // ========== FILTER HELPER METHODS (UNCHANGED) ==========
        private IQueryable<UserBase> ApplyUserFilters(IQueryable<UserBase> query, DashboardFilterRequest filter)
        {
            // Apply status filter
            if (filter.Status == "active")
                query = query.Where(u => u.IsActive);
            else if (filter.Status == "inactive")
                query = query.Where(u => !u.IsActive);

            // Apply user type filter
            if (filter.UserType == "jobseekers")
                query = query.Where(u => u.Role == "JobSeeker");
            else if (filter.UserType == "employers")
                query = query.Where(u => u.Role == "Employer");
            else if (filter.UserType == "admins")
                query = query.Where(u => u.Role == "Admin");

            // Apply date range
            var dateRange = GetDateRange(filter);
            query = query.Where(u => u.CreatedDate >= dateRange.StartDate && u.CreatedDate <= dateRange.EndDate);

            return query;
        }

        private IQueryable<Employer> ApplyEmployerFilters(IQueryable<Employer> query, DashboardFilterRequest filter)
        {
            // Apply status filter
            if (filter.Status == "active")
                query = query.Where(e => e.IsActive);
            else if (filter.Status == "inactive")
                query = query.Where(e => !e.IsActive);
            else if (filter.Status == "pending")
                query = query.Where(e => e.ApprovalStatus == "Pending");

            // Apply date range
            var dateRange = GetDateRange(filter);
            query = query.Where(e => e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate);

            return query;
        }

        private IQueryable<JobSeeker> ApplyJobSeekerFilters(IQueryable<JobSeeker> query, DashboardFilterRequest filter)
        {
            // Apply status filter
            if (filter.Status == "active")
                query = query.Where(js => js.IsActive);
            else if (filter.Status == "inactive")
                query = query.Where(js => !js.IsActive);

            // Apply date range
            var dateRange = GetDateRange(filter);
            query = query.Where(js => js.CreatedDate >= dateRange.StartDate && js.CreatedDate <= dateRange.EndDate);

            return query;
        }

        private IQueryable<Admin> ApplyAdminFilters(IQueryable<Admin> query, DashboardFilterRequest filter)
        {
            // Apply status filter
            if (filter.Status == "active")
                query = query.Where(a => a.IsActive);
            else if (filter.Status == "inactive")
                query = query.Where(a => !a.IsActive);

            // Apply date range
            var dateRange = GetDateRange(filter);
            query = query.Where(a => a.CreatedDate >= dateRange.StartDate && a.CreatedDate <= dateRange.EndDate);

            return query;
        }

        private IQueryable<Job> ApplyJobFilters(IQueryable<Job> query, DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            query = query.Where(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate);

            return query;
        }

        private IQueryable<Application> ApplyApplicationFilters(IQueryable<Application> query, DashboardFilterRequest filter)
        {
            var dateRange = GetDateRange(filter);
            query = query.Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate);

            return query;
        }

        private (DateTime StartDate, DateTime EndDate) GetDateRange(DashboardFilterRequest filter)
        {
            if (filter.CustomStartDate.HasValue && filter.CustomEndDate.HasValue)
            {
                return (filter.CustomStartDate.Value, filter.CustomEndDate.Value.AddDays(1)); // Include end date
            }

            var endDate = DateTime.Now;
            DateTime startDate;

            // Handle no date filter (null)
            if (filter.DateRange == null)
            {
                // No date filter - get all data from earliest possible date
                startDate = new DateTime(2020, 1, 1); // Or use: _db.Users.Min(u => u.CreatedDate)
            }
            else if (int.TryParse(filter.DateRange.ToString(), out int days))
            {
                startDate = endDate.AddDays(-days);
            }
            else
            {
                // Fallback to 7 days if parsing fails
                startDate = endDate.AddDays(-7);
            }

            return (startDate, endDate);
        }

        private List<string> GenerateFilteredMonthLabels(DashboardFilterRequest filter)
        {
            if (filter.DateRange <= 30)
            {
                // For short periods, show days
                return Enumerable.Range(0, Math.Min(filter.DateRange, 30))
                    .Select(i => DateTime.Now.AddDays(-i).ToString("MM/dd"))
                    .Reverse()
                    .ToList();
            }
            else
            {
                // For longer periods, show months
                var months = filter.DateRange / 30;
                return Enumerable.Range(0, Math.Min(months, 12))
                    .Select(i => DateTime.Now.AddMonths(-i).ToString("MMM yyyy"))
                    .Reverse()
                    .ToList();
            }
        }

        private int GetPeriodMonths(DashboardFilterRequest filter)
        {
            if (filter.DateRange <= 30) return Math.Min(filter.DateRange, 30);
            return Math.Min(filter.DateRange / 30, 12);
        }

        private DateTime GetTargetDateForPeriod(DashboardFilterRequest filter, int index)
        {
            if (filter.DateRange <= 30)
            {
                return DateTime.Now.AddDays(-(filter.DateRange - 1 - index));
            }
            else
            {
                var months = filter.DateRange / 30;
                return DateTime.Now.AddMonths(-(months - 1 - index));
            }
        }

        private async Task<List<RecentActivityViewModel>> GetFilteredJobSeekerActivities(DashboardFilterRequest filter, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var query = _db.JobSeekers.AsQueryable();

            if (filter.Status == "active")
                query = query.Where(js => js.IsActive);
            else if (filter.Status == "inactive")
                query = query.Where(js => !js.IsActive);

            var jobSeekers = await query
                .Where(js => js.CreatedDate >= dateRange.StartDate && js.CreatedDate <= dateRange.EndDate)
                .OrderByDescending(js => js.CreatedDate)
                .Take(5)
                .Select(js => new { js.FullName, js.CreatedDate })
                .ToListAsync();

            return jobSeekers.Select(js => new RecentActivityViewModel
            {
                ActivityType = "Registration",
                ActivityTypeDisplay = "Registration",
                UserOrCompany = js.FullName,
                Description = "New job seeker registered",
                TimeAgo = GetTimeAgo(js.CreatedDate),
                Status = "Active",
                StatusDisplay = "Active",
                CreatedDate = js.CreatedDate
            }).ToList();
        }

        private async Task<List<RecentActivityViewModel>> GetFilteredJobActivities(DashboardFilterRequest filter, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var jobs = await _db.Jobs
                .Include(j => j.Employer)
                .Where(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate)
                .OrderByDescending(j => j.PostedDate)
                .Take(5)
                .Select(j => new { j.Title, CompanyName = j.Employer.CompanyName, j.PostedDate })
                .ToListAsync();

            return jobs.Select(j => new RecentActivityViewModel
            {
                ActivityType = "JobPosted",
                ActivityTypeDisplay = "Job Posted",
                UserOrCompany = j.CompanyName,
                Description = $"{j.Title} position posted",
                TimeAgo = GetTimeAgo(j.PostedDate),
                Status = "Published",
                StatusDisplay = "Published",
                CreatedDate = j.PostedDate
            }).ToList();
        }

        private async Task<List<RecentActivityViewModel>> GetFilteredApplicationActivities(DashboardFilterRequest filter, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var applications = await _db.Applications
                .Include(a => a.JobSeeker)
                .Include(a => a.Job)
                .Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate)
                .OrderByDescending(a => a.AppliedDate)
                .Take(5)
                .Select(a => new { JobSeekerName = a.JobSeeker.FullName, JobTitle = a.Job.Title, a.AppliedDate })
                .ToListAsync();

            return applications.Select(a => new RecentActivityViewModel
            {
                ActivityType = "Application",
                ActivityTypeDisplay = "Application",
                UserOrCompany = a.JobSeekerName,
                Description = $"Applied for {a.JobTitle} role",
                TimeAgo = GetTimeAgo(a.AppliedDate),
                Status = "Pending",
                StatusDisplay = "Pending",
                CreatedDate = a.AppliedDate
            }).ToList();
        }

        private async Task<List<RecentActivityViewModel>> GetFilteredEmployerActivities(DashboardFilterRequest filter, (DateTime StartDate, DateTime EndDate) dateRange)
        {
            var query = _db.Employers.AsQueryable();

            if (filter.Status == "pending")
                query = query.Where(e => e.ApprovalStatus == "Pending");

            var employers = await query
                .Where(e => e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate)
                .OrderByDescending(e => e.CreatedDate)
                .Take(3)
                .Select(e => new { e.CompanyName, e.CreatedDate })
                .ToListAsync();

            return employers.Select(e => new RecentActivityViewModel
            {
                ActivityType = "CompanyUpdate",
                ActivityTypeDisplay = "Company Registration",
                UserOrCompany = e.CompanyName,
                Description = "New employer registration pending approval",
                TimeAgo = GetTimeAgo(e.CreatedDate),
                Status = "Pending",
                StatusDisplay = "Needs Approval",
                CreatedDate = e.CreatedDate
            }).ToList();
        }

        private string GenerateFilterSummary(DashboardFilterRequest filter)
        {
            var summary = "Showing ";

            // User type
            summary += filter.UserType switch
            {
                "jobseekers" => "job seekers ",
                "employers" => "employers ",
                "admins" => "admins ",
                _ => "all users "
            };

            // Status
            if (filter.Status != "all")
            {
                summary += $"({filter.Status}) ";
            }

            // Date range
            if (filter.CustomStartDate.HasValue && filter.CustomEndDate.HasValue)
            {
                summary += $"from {filter.CustomStartDate.Value:MMM dd} to {filter.CustomEndDate.Value:MMM dd}";
            }
            else
            {
                summary += $"for last {filter.DateRange} days";
            }

            // Search
            if (!string.IsNullOrEmpty(filter.Search))
            {
                summary += $" matching '{filter.Search}'";
            }

            return summary;
        }

        // ========== ORIGINAL DASHBOARD DATA METHODS (UNCHANGED) ==========
        private async Task<DashboardStatsViewModel> GetDashboardStats()
        {
            return new DashboardStatsViewModel
            {
                TotalUsers = await _db.Users.CountAsync(),
                TotalEmployers = await _db.Employers.CountAsync(),
                TotalJobSeekers = await _db.JobSeekers.CountAsync(),
                TotalAdmins = await _db.Admins.CountAsync(),
                TotalJobs = await _db.Jobs.CountAsync(),
                TotalApplications = await _db.Applications.CountAsync(),
                PendingEmployers = await _db.Employers.CountAsync(e => e.ApprovalStatus == "Pending")
            };
        }

        private async Task<ChartDataViewModel> GetChartDataSafely()
        {
            try
            {
                return await GetChartData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chart data");
                return CreateEmptyChartData();
            }
        }

        private async Task<ChartDataViewModel> GetChartData()
        {
            var monthLabels = GenerateMonthLabels();
            var trendsData = await GetTrendsData();

            return new ChartDataViewModel
            {
                MonthLabels = monthLabels,
                JobSeekerTrends = trendsData.JobSeekerTrends,
                EmployerTrends = trendsData.EmployerTrends,
                ApplicationTrends = trendsData.ApplicationTrends,
                JobStats = await GetJobStats()
            };
        }

        private async Task<TrendsDataResult> GetTrendsData()
        {
            var result = new TrendsDataResult();
            var sixMonthsAgo = DateTime.Now.AddMonths(-6);

            // Get all data in one go for efficiency
            var jobSeekerData = await _db.JobSeekers
                .Where(js => js.CreatedDate >= sixMonthsAgo)
                .GroupBy(js => new { js.CreatedDate.Year, js.CreatedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            var employerData = await _db.Employers
                .Where(e => e.CreatedDate >= sixMonthsAgo)
                .GroupBy(e => new { e.CreatedDate.Year, e.CreatedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            var applicationData = await _db.Applications
                .Where(a => a.AppliedDate >= sixMonthsAgo)
                .GroupBy(a => new { a.AppliedDate.Year, a.AppliedDate.Month })
                .Select(g => new { YearMonth = g.Key, Count = g.Count() })
                .ToListAsync();

            // Fill the trend arrays for 6 months
            for (int i = 0; i < 6; i++)
            {
                var targetDate = DateTime.Now.AddMonths(-5 + i);
                var targetKey = new { Year = targetDate.Year, Month = targetDate.Month };

                result.JobSeekerTrends.Add(GetCountForMonth(jobSeekerData, targetKey));
                result.EmployerTrends.Add(GetCountForMonth(employerData, targetKey));
                result.ApplicationTrends.Add(GetCountForMonth(applicationData, targetKey));
            }

            return result;
        }

        private async Task<JobStatsViewModel> GetJobStats()
        {
            return new JobStatsViewModel
            {
                ActiveJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Open),
                ClosedJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Closed),
                DraftJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Draft),
                PendingJobs = 0
            };
        }

        private async Task<List<TopEmployerViewModel>> GetTopEmployersSafely()
        {
            try
            {
                return await GetTopEmployers();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top employers");
                return new List<TopEmployerViewModel>();
            }
        }

        private async Task<List<TopEmployerViewModel>> GetTopEmployers()
        {
            return await _db.Employers
                .Where(e => e.IsActive)
                .Select(e => new TopEmployerViewModel
                {
                    CompanyName = e.CompanyName,
                    JobsPosted = e.Jobs.Count(),
                    TotalApplications = e.Jobs.SelectMany(j => j.Applications).Count(),
                    SuccessfulHires = e.Jobs.SelectMany(j => j.Applications)
                                          .Count(a => a.Status == ApplicationStatusEnum.Hired)
                })
                .Where(te => te.JobsPosted > 0)
                .OrderByDescending(te => te.TotalApplications)
                .Take(5)
                .ToListAsync();
        }

        private async Task<PerformanceMetricsViewModel> GetPerformanceMetricsSafely()
        {
            try
            {
                return await GetPerformanceMetrics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance metrics");
                return CreateDefaultPerformanceMetrics();
            }
        }

        private async Task<PerformanceMetricsViewModel> GetPerformanceMetrics()
        {
            var metrics = new PerformanceMetricsViewModel();

            // Calculate engagement rate
            var totalUsers = await _db.Users.CountAsync();
            var activeUsers = await _db.Users.CountAsync(u => u.IsActive);
            metrics.UserEngagementRate = CalculatePercentage(activeUsers, totalUsers);

            // Calculate job success rate
            var totalJobs = await _db.Jobs.CountAsync();
            var activeJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Open);
            metrics.JobPostingSuccessRate = CalculatePercentage(activeJobs, totalJobs);

            // Calculate application completion rate
            var totalApplications = await _db.Applications.CountAsync();
            var completedApplications = await _db.Applications
                .CountAsync(a => a.Status == ApplicationStatusEnum.Hired || a.Status == ApplicationStatusEnum.OfferSent);
            metrics.ApplicationCompletionRate = CalculatePercentage(completedApplications, totalApplications);

            // Set other metrics
            metrics.PlatformSatisfaction = 89; // Mock data
            metrics.AvgLoadTime = "4.2s";
            metrics.Uptime = "99.8";

            var dailyActiveUsers = await _db.Users.CountAsync(u => u.CreatedDate >= DateTime.Today);
            metrics.DailyActiveUsers = FormatNumber(dailyActiveUsers);

            return metrics;
        }

        private async Task<List<RecentActivityViewModel>> GetRecentActivitiesSafely()
        {
            try
            {
                return await GetRecentActivities();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent activities");
                return new List<RecentActivityViewModel>();
            }
        }

        private async Task<List<RecentActivityViewModel>> GetRecentActivities()
        {
            var activities = new List<RecentActivityViewModel>();

            // Get recent activities from different sources
            activities.AddRange(await GetRecentJobSeekerActivities());
            activities.AddRange(await GetRecentJobActivities());
            activities.AddRange(await GetRecentApplicationActivities());
            activities.AddRange(await GetRecentEmployerActivities());

            return activities.OrderByDescending(a => a.CreatedDate).Take(10).ToList();
        }

        // FIXED: GetTimeAgo no longer in LINQ query
        private async Task<List<RecentActivityViewModel>> GetRecentJobSeekerActivities()
        {
            var jobSeekers = await _db.JobSeekers
                .OrderByDescending(js => js.CreatedDate)
                .Take(3)
                .Select(js => new
                {
                    js.FullName,
                    js.CreatedDate
                })
                .ToListAsync();

            return jobSeekers.Select(js => new RecentActivityViewModel
            {
                ActivityType = "Registration",
                ActivityTypeDisplay = "Registration",
                UserOrCompany = js.FullName,
                Description = "New job seeker registered",
                TimeAgo = GetTimeAgo(js.CreatedDate),
                Status = "Active",
                StatusDisplay = "Active",
                CreatedDate = js.CreatedDate
            }).ToList();
        }

        // FIXED: GetTimeAgo no longer in LINQ query
        private async Task<List<RecentActivityViewModel>> GetRecentJobActivities()
        {
            var jobs = await _db.Jobs
                .Include(j => j.Employer)
                .OrderByDescending(j => j.PostedDate)
                .Take(3)
                .Select(j => new
                {
                    j.Title,
                    CompanyName = j.Employer.CompanyName,
                    j.PostedDate
                })
                .ToListAsync();

            return jobs.Select(j => new RecentActivityViewModel
            {
                ActivityType = "JobPosted",
                ActivityTypeDisplay = "Job Posted",
                UserOrCompany = j.CompanyName,
                Description = $"{j.Title} position posted",
                TimeAgo = GetTimeAgo(j.PostedDate),
                Status = "Published",
                StatusDisplay = "Published",
                CreatedDate = j.PostedDate
            }).ToList();
        }

        // FIXED: GetTimeAgo no longer in LINQ query
        private async Task<List<RecentActivityViewModel>> GetRecentApplicationActivities()
        {
            var applications = await _db.Applications
                .Include(a => a.JobSeeker)
                .Include(a => a.Job)
                .OrderByDescending(a => a.AppliedDate)
                .Take(3)
                .Select(a => new
                {
                    JobSeekerName = a.JobSeeker.FullName,
                    JobTitle = a.Job.Title,
                    a.AppliedDate
                })
                .ToListAsync();

            return applications.Select(a => new RecentActivityViewModel
            {
                ActivityType = "Application",
                ActivityTypeDisplay = "Application",
                UserOrCompany = a.JobSeekerName,
                Description = $"Applied for {a.JobTitle} role",
                TimeAgo = GetTimeAgo(a.AppliedDate),
                Status = "Pending",
                StatusDisplay = "Pending",
                CreatedDate = a.AppliedDate
            }).ToList();
        }

        // FIXED: GetTimeAgo no longer in LINQ query
        private async Task<List<RecentActivityViewModel>> GetRecentEmployerActivities()
        {
            var employers = await _db.Employers
                .Where(e => e.ApprovalStatus == "Pending")
                .OrderByDescending(e => e.CreatedDate)
                .Take(2)
                .Select(e => new
                {
                    e.CompanyName,
                    e.CreatedDate
                })
                .ToListAsync();

            return employers.Select(e => new RecentActivityViewModel
            {
                ActivityType = "CompanyUpdate",
                ActivityTypeDisplay = "Company Registration",
                UserOrCompany = e.CompanyName,
                Description = "New employer registration pending approval",
                TimeAgo = GetTimeAgo(e.CreatedDate),
                Status = "Pending",
                StatusDisplay = "Needs Approval",
                CreatedDate = e.CreatedDate
            }).ToList();
        }

        // ========== PROFILE MANAGEMENT (UNCHANGED) ==========
        public async Task<IActionResult> AdminProfile()
        {
            var adminId = GetCurrentUserId();
            if (string.IsNullOrEmpty(adminId))
                return RedirectToAction("Login", "Account");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId);
            if (user == null)
                return NotFound("Admin user not found");

            var viewModel = MapUserToProfileViewModel(user);
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminProfile(JobRecruitment.ViewModels.AdminProfileViewModel model)
        {
            var adminId = GetCurrentUserId();
            if (string.IsNullOrEmpty(adminId))
                return RedirectToAction("Login", "Account");

            // Remove validation for disabled fields
            ModelState.Remove("Username");
            ModelState.Remove("ProfilePhoto");

            if (!ModelState.IsValid)
                return View(model);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId && u.Role == "Admin");
            if (user == null)
                return NotFound();

            // Validate unique email
            if (await IsEmailTaken(model.Email, adminId))
            {
                ModelState.AddModelError("Email", "Email already exists");
                return View(model);
            }

            try
            {
                // Update user properties
                user.FullName = model.FullName;
                user.Gender = model.Gender;
                user.Email = model.Email;
                user.Phone = model.Phone;
                user.DateOfBirth = model.DateOfBirth;

                // Handle photo upload separately - don't fail if photo fails
                if (model.ProfilePhoto != null)
                {
                    try
                    {
                        var photoResult = await ProcessProfilePhoto(model.ProfilePhoto, user);
                        // Continue even if photo fails
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogError(photoEx, "Photo upload failed");
                        // Continue with other updates
                    }
                }

                // Save changes
                await _db.SaveChangesAsync();

                // Refresh claims
                await RefreshUserClaims(user);

                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction("AdminProfile");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating admin profile for user {UserId}", adminId);
                TempData["Error"] = "An error occurred while updating your profile.";
                return View(model);
            }
        }

        // ========== EMPLOYER MANAGEMENT (UNCHANGED) ==========
        [HttpGet]
        public IActionResult CreateEmployer() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEmployer(Employer employer)
        {
            if (!ModelState.IsValid)
                return View(employer);

            // Check for duplicate email/username
            if (await _db.Users.AnyAsync(u => u.Email == employer.Email))
            {
                ModelState.AddModelError("Email", "Email already exists");
                return View(employer);
            }

            if (await _db.Users.AnyAsync(u => u.Username == employer.Username))
            {
                ModelState.AddModelError("Username", "Username already exists");
                return View(employer);
            }

            try
            {
                employer.Id = GenerateUniqueId();
                employer.Password = BCrypt.Net.BCrypt.HashPassword(employer.Password);
                employer.Role = "Employer";
                employer.CreatedDate = DateTime.Now;
                employer.IsActive = true;
                employer.IsEmailVerified = true;
                employer.ApprovalStatus = "Approved";
                employer.FailedLoginAttempts = 0;

                _db.Employers.Add(employer);
                await _db.SaveChangesAsync();

                _logger.LogInformation($"Employer {employer.CompanyName} created by admin {GetCurrentUserName()}");
                TempData["Success"] = "Employer account created successfully!";
                return RedirectToAction("AdminDashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employer");
                TempData["Error"] = "Error creating employer account";
                return View(employer);
            }
        }

        public async Task<IActionResult> ApproveEmployer(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["Error"] = "Invalid employer ID.";
                return RedirectToAction("AdminDashboard");
            }

            var employer = await _db.Users.OfType<Employer>().FirstOrDefaultAsync(u => u.Id == id);
            if (employer == null)
            {
                TempData["Error"] = "Employer not found.";
                return RedirectToAction("AdminDashboard");
            }

            var adminIdString = GetCurrentUserId();
            if (string.IsNullOrEmpty(adminIdString))
            {
                TempData["Error"] = "Unable to determine current admin user.";
                return RedirectToAction("AdminDashboard");
            }

            employer.ApprovalStatus = "Approved";
            employer.ApprovedByAdminId = adminIdString;

            await _db.SaveChangesAsync();

            _logger.LogInformation($"Employer {employer.CompanyName} approved by admin {GetCurrentUserName()}");
            TempData["Success"] = $"Employer {employer.CompanyName} has been approved!";
            return RedirectToAction("AdminDashboard");
        }

        public async Task<IActionResult> RejectEmployer(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["Error"] = "Invalid employer ID.";
                return RedirectToAction("AdminDashboard");
            }

            var employer = await _db.Users.OfType<Employer>().FirstOrDefaultAsync(u => u.Id == id);
            if (employer == null)
            {
                TempData["Error"] = "Employer not found.";
                return RedirectToAction("AdminDashboard");
            }

            employer.ApprovalStatus = "Rejected";
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Employer {employer.CompanyName} rejected by admin {GetCurrentUserName()}");
            TempData["Success"] = $"Employer {employer.CompanyName} has been rejected.";
            return RedirectToAction("AdminDashboard");
        }

        // ========== JOB SEEKER MANAGEMENT (UNCHANGED) ==========
        [HttpGet]
        public IActionResult CreateJobSeeker() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateJobSeeker(JobSeeker jobSeeker)
        {
            if (!ModelState.IsValid)
                return View(jobSeeker);

            // Check for duplicate email/username
            if (await _db.Users.AnyAsync(u => u.Email == jobSeeker.Email))
            {
                ModelState.AddModelError("Email", "Email already exists");
                return View(jobSeeker);
            }

            if (await _db.Users.AnyAsync(u => u.Username == jobSeeker.Username))
            {
                ModelState.AddModelError("Username", "Username already exists");
                return View(jobSeeker);
            }

            try
            {
                jobSeeker.Id = GenerateUniqueId();
                jobSeeker.Password = BCrypt.Net.BCrypt.HashPassword(jobSeeker.Password);
                jobSeeker.Role = "JobSeeker";
                jobSeeker.CreatedDate = DateTime.Now;
                jobSeeker.IsActive = true;
                jobSeeker.IsEmailVerified = true;
                jobSeeker.FailedLoginAttempts = 0;

                _db.JobSeekers.Add(jobSeeker);
                await _db.SaveChangesAsync();

                _logger.LogInformation($"Job Seeker {jobSeeker.FullName} created by admin {GetCurrentUserName()}");
                TempData["Success"] = "Job Seeker account created successfully!";
                return RedirectToAction("AdminDashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job seeker");
                TempData["Error"] = "Error creating job seeker account";
                return View(jobSeeker);
            }
        }

        // ========== USER MANAGEMENT (UNCHANGED) ==========
        public async Task<IActionResult> ManageUsers(string role = "All", string search = "", int page = 1, int pageSize = 10)
        {
            try
            {
                var filter = new UserFilterViewModel
                {
                    Role = role,
                    Search = search?.Trim(),
                    Page = page,
                    PageSize = pageSize
                };

                var result = await GetFilteredUsers(filter);
                SetPaginationViewBag(result, filter);

                return View(result.Users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading users for management");
                TempData["Error"] = "Error loading users.";
                return View(new List<UserBase>());
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteJobReport(string id)
        {
            try
            {
                var report = await _db.JobReports.FindAsync(id);
                if (report == null)
                {
                    return Json(new { success = false, message = "Report not found" });
                }

                _db.JobReports.Remove(report);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Report deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting report {id}");
                return Json(new { success = false, message = "Error deleting report" });
            }
        }


        [HttpPost]
        public async Task<IActionResult> ToggleJobStatus(string jobId)
        {
            var job = await _db.Jobs.FindAsync(jobId);
            if (job == null)
                return Json(new { success = false, message = "Job not found" });

            job.IsActive = !job.IsActive;
            await _db.SaveChangesAsync();

            return Json(new { success = true, newStatus = job.IsActive ? "Active" : "Blocked" });
        }
        // ========== USER ACTIONS (UNCHANGED) ==========
        public async Task<IActionResult> BlockUser(string id)
        {
            var result = await ToggleUserStatus(id, false, "blocked");
            if (result.Success)
                TempData["Success"] = result.Message;
            else
                TempData["Error"] = result.ErrorMessage;

            return RedirectToAction("ManageUsers");
        }

        public async Task<IActionResult> UnblockUser(string id)
        {
            var result = await ToggleUserStatus(id, true, "unblocked");
            if (result.Success)
                TempData["Success"] = result.Message;
            else
                TempData["Error"] = result.ErrorMessage;

            return RedirectToAction("ManageUsers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var result = await DeleteUserById(id);
            if (result.Success)
                TempData["Success"] = result.Message;
            else
                TempData["Error"] = result.ErrorMessage;

            return RedirectToAction("ManageUsers");
        }

        // ========== JOB MANAGEMENT (UNCHANGED) ==========
        public IActionResult JobListingManagement(string search = "", string status = "All", int page = 1, int pageSize = 10)
        {
            try
            {
                var query = _db.Jobs
                    .Include(j => j.Employer)
                    .Include(j => j.Category)
                    .AsQueryable();

                // Search functionality
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(j =>
                        j.Title.Contains(search) ||
                        j.Employer.CompanyName.Contains(search) ||
                        j.Description.Contains(search));
                }

                // Filter by status
                if (!string.IsNullOrEmpty(status) && status != "All")
                {
                    if (Enum.TryParse<JobStatus>(status, out var jobStatus))
                    {
                        query = query.Where(j => j.Status == jobStatus);
                    }
                }

                var totalJobs = query.Count();
                var jobs = query
                    .OrderByDescending(j => j.PostedDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.TotalJobs = totalJobs;
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = (int)Math.Ceiling((double)totalJobs / pageSize);
                ViewBag.CurrentStatus = status;
                ViewBag.CurrentSearch = search;

                return View(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading job listings");
                TempData["Error"] = "Error loading job listings.";
                return View(new List<Job>());
            }
        }

        public IActionResult JobDetails(string id)
        {
            try
            {
                var job = _db.Jobs
                    .Include(j => j.Employer)
                    .Include(j => j.Category)
                    .Include(j => j.Applications)
                    .ThenInclude(a => a.JobSeeker)
                    .FirstOrDefault(j => j.Id == id);

                if (job == null)
                {
                    TempData["Error"] = "Job not found.";
                    return RedirectToAction("JobListingManagement");
                }

                return View(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading job details for id {id}");
                TempData["Error"] = "Error loading job details.";
                return RedirectToAction("JobListingManagement");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteJob(string id)
        {
            try
            {
                var job = _db.Jobs.Find(id);
                if (job != null)
                {
                    _db.Jobs.Remove(job);
                    _db.SaveChanges();

                    _logger.LogInformation($"Job {job.Title} deleted by admin {GetCurrentUserName()}");
                    TempData["Success"] = "Job listing deleted successfully.";
                }
                else
                {
                    TempData["Error"] = "Job not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting job with id {id}");
                TempData["Error"] = "Error deleting job listing.";
            }

            return RedirectToAction("JobListingManagement");
        }

        // ========== REPORTS MANAGEMENT (UNCHANGED) ==========
        [HttpPost]
        public IActionResult ToggleBlock(string entityId, string role)
        {
            if (role == "Employer")
            {
                var employer = _db.Employers.Find(entityId);
                if (employer != null)
                    employer.IsActive = !employer.IsActive;
            }
            else
            {
                var user = _db.Users.Find(entityId);
                if (user != null)
                    user.IsActive = !user.IsActive;
            }

            _db.SaveChanges();
            return RedirectToAction("Reports");
        }

        public async Task<IActionResult> Reports(string searchTerm = "", string filterType = "", string filterStatus = "", int page = 1, int pageSize = 10)
        {
            var reportViewModels = await GetReportsAsync(searchTerm, filterType, filterStatus, page, pageSize);

            // Calculate statistics
            var totalCount = await _db.Set<ReportBase>().CountAsync();
            var userReportCount = await _db.Set<ReportBase>().OfType<UserReport>().CountAsync();
            var employerReportCount = await _db.Set<ReportBase>().OfType<EmployerReport>().CountAsync();
            var recentReports = await _db.Set<ReportBase>()
                .Where(r => r.DateReported >= DateTime.Now.AddDays(-7))
                .CountAsync();

            ViewBag.TotalReports = totalCount;
            ViewBag.UserReportCount = userReportCount;
            ViewBag.EmployerReportCount = employerReportCount;
            ViewBag.RecentReports = recentReports;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)reportViewModels.TotalCount / pageSize);
            ViewBag.SearchTerm = searchTerm;
            ViewBag.FilterType = filterType;
            ViewBag.FilterStatus = filterStatus;

            return View(reportViewModels.Reports);
        }

        // AJAX endpoint for loading reports
        [HttpGet]
        public async Task<IActionResult> LoadReports(string searchTerm = "", string filterType = "", string filterStatus = "", int page = 1, int pageSize = 10)
        {
            var reportViewModels = await GetReportsAsync(searchTerm, filterType, filterStatus, page, pageSize);

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)reportViewModels.TotalCount / pageSize);
            ViewBag.SearchTerm = searchTerm;
            ViewBag.FilterType = filterType;
            ViewBag.FilterStatus = filterStatus;

            return PartialView("_ReportsTable", reportViewModels.Reports);
        }
        private async Task<(List<UnifiedReportViewModel> Reports, int TotalCount)> GetReportsAsync(string searchTerm, string filterType, string filterStatus, int page, int pageSize)
        {
            var query = _db.Set<ReportBase>().AsQueryable();

            // Include related entities
            query = query.Include(r => ((UserReport)r).ReportedUser)
                         .Include(r => ((UserReport)r).Employer)
                         .Include(r => ((EmployerReport)r).Employer)
                         .Include(r => ((EmployerReport)r).Reporter);

            // Apply search filter
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(r => r.Reason.Contains(searchTerm) ||
                                       (r is UserReport && ((UserReport)r).ReportedUser.FullName.Contains(searchTerm)) ||
                                       (r is EmployerReport && ((EmployerReport)r).Employer.CompanyName.Contains(searchTerm)));
            }

            // Apply type filter
            if (!string.IsNullOrEmpty(filterType))
            {
                if (filterType == "UserReport")
                    query = query.Where(r => r is UserReport);
                else if (filterType == "EmployerReport")
                    query = query.Where(r => r is EmployerReport);
            }

            // Apply status filter based on associated user/employer
            if (!string.IsNullOrEmpty(filterStatus))
            {
                bool isActive = filterStatus == "Active";
                query = query.Where(r =>
                    (r is UserReport && ((UserReport)r).ReportedUser.IsActive == isActive) ||
                    (r is EmployerReport && ((EmployerReport)r).Employer.IsActive == isActive)
                );
            }

            var totalCount = await query.CountAsync();
            var reports = await query
                .OrderByDescending(r => r.DateReported)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Convert to UnifiedReportViewModel
            var reportViewModels = reports.Select(r => new UnifiedReportViewModel
            {
                ReportId = r.Id,
                ReportType = r is UserReport ? "User" : "Employer",
                Reason = r.Reason,
                DateReported = r.DateReported,
                ReporterName = r is UserReport ur ? ur.Employer?.CompanyName ?? "Unknown" :
                              r is EmployerReport er ? er.Reporter?.FullName ?? "Unknown" : "Unknown",
                ReportedEntityName = r is UserReport ur2 ? ur2.ReportedUser?.FullName ?? "Unknown User" :
                                    r is EmployerReport er2 ? er2.Employer?.CompanyName ?? "Unknown Employer" : "Unknown",
                ReportedEntityId = r is UserReport ur3 ? ur3.ReportedUserId :
                                  r is EmployerReport er3 ? er3.EmployerId : "",
                ReportedRole = r is UserReport ? "User" : "Employer",
                IsBlocked = r is UserReport ur4 ? !(ur4.ReportedUser?.IsActive ?? true) :
                           r is EmployerReport er4 ? !(er4.Employer?.IsActive ?? true) : false
            }).ToList();

            return (reportViewModels, totalCount);
        }



        // GET: Admin/ReportDetails/5
        public async Task<IActionResult> UserReportDetails(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var report = await _db.Set<ReportBase>()
                .Include(r => ((UserReport)r).ReportedUser)
                .Include(r => ((UserReport)r).Employer)
                .Include(r => ((EmployerReport)r).Employer)
                .Include(r => ((EmployerReport)r).Reporter)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
            {
                return NotFound();
            }

            return View(report);
        }

        // POST: Admin/ProcessReport
        [HttpPost]
        public async Task<IActionResult> ProcessReport(string reportId, string action, string adminNotes = "")
        {
            var report = await _db.Set<ReportBase>().FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Report not found" });
            }

            try
            {
                switch (action.ToLower())
                {
                    case "approve":
                        // Handle report approval logic
                        await LogAdminAction($"Approved report {reportId}", $"Report: {report.Reason}. Notes: {adminNotes}");
                        break;

                    case "reject":
                        // Handle report rejection logic
                        await LogAdminAction($"Rejected report {reportId}", $"Report: {report.Reason}. Notes: {adminNotes}");
                        break;

                    case "block":
                        // Handle blocking the reported entity
                        if (report is UserReport userReport)
                        {
                            var user = await _db.Users.FindAsync(userReport.ReportedUserId);
                            if (user != null)
                            {
                                user.IsActive = false;
                                _db.Update(user);
                            }
                        }
                        else if (report is EmployerReport employerReport)
                        {
                            var employer = await _db.Employers.FindAsync(employerReport.EmployerId);
                            if (employer != null)
                            {
                                employer.IsActive = false;
                                _db.Update(employer);
                            }
                        }
                        await LogAdminAction($"Blocked entity from report {reportId}", $"Report: {report.Reason}. Notes: {adminNotes}");
                        break;
                }

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"Report {action}d successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error processing report: {ex.Message}" });
            }
        }

        // POST: Admin/DeleteReport
        [HttpPost]
        public async Task<IActionResult> DeleteReport(string id)
        {
            var report = await _db.Set<ReportBase>().FindAsync(id);
            if (report == null)
            {
                return Json(new { success = false, message = "Report not found" });
            }

            try
            {
                _db.Set<ReportBase>().Remove(report);
                await LogAdminAction($"Deleted report {id}", $"Report reason: {report.Reason}");
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Report deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting report: {ex.Message}" });
            }
        }
        [HttpGet]
        public async Task<IActionResult> ExportJobReportPdf(string id)
        {
            var report = await _db.JobReports
                .Include(r => r.Job)
                .ThenInclude(j => j.Employer)
                .FirstOrDefaultAsync(r => r.Id == id.ToString());

            if (report == null)
                return NotFound();

            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                doc.Add(new Paragraph("Job Report", titleFont));
                doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
                doc.Add(new Paragraph("\n"));

                var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);

                doc.Add(new Paragraph($"Report ID: {report.Id}", normalFont));
                doc.Add(new Paragraph($"Job Title: {report.Job.Title}", normalFont));
                doc.Add(new Paragraph($"Employer: {report.Job.Employer.CompanyName}", normalFont));
                doc.Add(new Paragraph($"Reason: {report.Reason}", normalFont));
                doc.Add(new Paragraph($"Date Reported: {report.DateReported:yyyy-MM-dd HH:mm}", normalFont));

                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"JobReport_{report.Id}.pdf");
            }
        }



        // ✅ Export as PDF
        [HttpGet]
        public async Task<IActionResult> ExportJobReport(string id)
        {
            var report = await _db.JobReports
                .Include(r => r.Job)
                .ThenInclude(j => j.Employer)
                .FirstOrDefaultAsync(r => r.Id == id.ToString());

            if (report == null)
                return NotFound();

            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // Title
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                doc.Add(new Paragraph("Job Report", titleFont));
                doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
                doc.Add(new Paragraph("\n"));

                // Report Details
                var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);

                doc.Add(new Paragraph($"Report ID: {report.Id}", normalFont));
                doc.Add(new Paragraph($"Job Title: {report.Job.Title}", normalFont));
                doc.Add(new Paragraph($"Employer: {report.Job.Employer.CompanyName}", normalFont));
                doc.Add(new Paragraph($"Reason: {report.Reason}", normalFont));
                doc.Add(new Paragraph($"Date Reported: {report.DateReported:yyyy-MM-dd HH:mm}", normalFont));

                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"JobReport_{report.Id}.pdf");
            }
        }
        // GET: Admin/ExportReports
        public async Task<IActionResult> ExportReports(string format = "csv")
        {
            var reports = await _db.Set<ReportBase>()
                .Include(r => ((UserReport)r).ReportedUser)
                .Include(r => ((UserReport)r).Employer)
                .Include(r => ((EmployerReport)r).Employer)
                .Include(r => ((EmployerReport)r).Reporter)
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = GenerateReportsCsv(reports);
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
                return File(bytes, "text/csv", $"reports_{DateTime.Now:yyyyMMdd}.csv");
            }

            return BadRequest("Unsupported format");
        }

        private string GenerateReportsCsv(List<ReportBase> reports)
        {
            var csv = new StringBuilder();
            csv.AppendLine("ID,Type,Reason,Date Reported,Reported Entity,Reporter");

            foreach (var report in reports)
            {
                var type = report is UserReport ? "User Report" : "Employer Report";
                var reportedEntity = "";
                var reporter = "";

                if (report is UserReport userReport)
                {
                    reportedEntity = userReport.ReportedUser?.FullName ?? "Unknown";
                    reporter = userReport.Employer?.CompanyName ?? "Unknown";
                }
                else if (report is EmployerReport employerReport)
                {
                    reportedEntity = employerReport.Employer?.CompanyName ?? "Unknown";
                    reporter = employerReport.Reporter?.FullName ?? "Unknown";
                }

                csv.AppendLine($"{report.Id},{type},\"{report.Reason.Replace("\"", "\"\"")}\",{report.DateReported:yyyy-MM-dd},{reportedEntity},{reporter}");
            }

            return csv.ToString();
        }

        private async Task LogAdminAction(string action, string details)
        {
            // Get current admin ID
            var adminId = GetCurrentUserId();

            var log = new AdminLog
            {
                Id = GenerateId(),
                AdminId = adminId,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            _db.AdminLogs.Add(log);
        }

        private string GenerateId()
        {
            return Guid.NewGuid().ToString("N")[..10].ToUpper();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockEntity(BlockRequest request)
        {
            try
            {
                if (request.Type == "Employer")
                {
                    var employer = await _db.Employers.FirstOrDefaultAsync(e => e.Id == request.Id);
                    if (employer != null)
                    {
                        employer.IsActive = false;
                        await _db.SaveChangesAsync();
                        return Json(new { success = true, message = "Employer blocked successfully" });
                    }
                }
                else if (request.Type == "User")
                {
                    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.Id);
                    if (user != null)
                    {
                        user.IsActive = false;
                        await _db.SaveChangesAsync();
                        return Json(new { success = true, message = "User blocked successfully" });
                    }
                }

                return Json(new { success = false, message = "Entity not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error blocking {request.Type} with id {request.Id}");
                return Json(new { success = false, message = "Error blocking entity" });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockEntity(BlockRequest request) // no [FromBody]
        {
            try
            {
                if (request.Type == "Employer")
                {
                    var employer = await _db.Employers.FirstOrDefaultAsync(e => e.Id == request.Id);
                    if (employer != null)
                    {
                        employer.IsActive = true;
                        employer.FailedLoginAttempts = 0;
                        await _db.SaveChangesAsync();
                        return Json(new { success = true, message = "Employer unblocked successfully" });
                    }
                }
                else if (request.Type == "User")
                {
                    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.Id);
                    if (user != null)
                    {
                        user.IsActive = true;
                        user.FailedLoginAttempts = 0;
                        await _db.SaveChangesAsync();
                        return Json(new { success = true, message = "User unblocked successfully" });
                    }
                }

                return Json(new { success = false, message = "Entity not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unblocking {request.Type} with id {request.Id}");
                return Json(new { success = false, message = "Error unblocking entity" });
            }
        }


        // ========== HELPER METHODS (UNCHANGED) ==========
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
                ViewBag.AdminName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.AdminEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.AdminFullName = User.FindFirstValue("FullName");
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.AdminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }

        private string GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private string GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

        private List<string> GenerateMonthLabels()
        {
            return Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-5 + i).ToString("MMM yyyy"))
                .ToList();
        }

        private int GetCountForMonth<T>(IList<T> data, dynamic targetKey) where T : class
        {
            foreach (var item in data)
            {
                var yearMonth = (dynamic)item.GetType().GetProperty("YearMonth").GetValue(item);
                if (yearMonth.Year == targetKey.Year && yearMonth.Month == targetKey.Month)
                {
                    return (int)item.GetType().GetProperty("Count").GetValue(item);
                }
            }
            return 0;
        }

        private int CalculatePercentage(int numerator, int denominator)
        {
            return denominator > 0 ? (int)Math.Round((double)numerator / denominator * 100) : 0;
        }

        private string FormatNumber(int number)
        {
            if (number >= 1000000) return $"{number / 1000000.0:F1}M";
            if (number >= 1000) return $"{number / 1000.0:F1}K";
            return number.ToString();
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1) return "Just now";
            if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} minutes ago";
            if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hours ago";
            if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays} days ago";

            return dateTime.ToString("MMM dd, yyyy");
        }

        private AdminDashboardViewModel CreateErrorDashboard(string errorMessage)
        {
            return new AdminDashboardViewModel
            {
                Stats = new DashboardStatsViewModel(),
                HasError = true,
                ErrorMessage = errorMessage
            };
        }

        private ChartDataViewModel CreateEmptyChartData()
        {
            return new ChartDataViewModel
            {
                MonthLabels = GenerateMonthLabels(),
                JobSeekerTrends = Enumerable.Repeat(0, 6).ToList(),
                EmployerTrends = Enumerable.Repeat(0, 6).ToList(),
                ApplicationTrends = Enumerable.Repeat(0, 6).ToList(),
                JobStats = new JobStatsViewModel()
            };
        }

        private PerformanceMetricsViewModel CreateDefaultPerformanceMetrics()
        {
            return new PerformanceMetricsViewModel
            {
                UserEngagementRate = 0,
                JobPostingSuccessRate = 0,
                ApplicationCompletionRate = 0,
                PlatformSatisfaction = 0,
                AvgLoadTime = "N/A",
                Uptime = "N/A",
                DailyActiveUsers = "0"
            };
        }

        // FIXED: Explicitly specify ViewModels namespace to resolve ambiguity
        private JobRecruitment.ViewModels.AdminProfileViewModel MapUserToProfileViewModel(UserBase user)
        {
            return new JobRecruitment.ViewModels.AdminProfileViewModel
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Gender = user.Gender,
                Email = user.Email,
                Phone = user.Phone,
                DateOfBirth = user.DateOfBirth,
                ProfilePhotoFileName = user.ProfilePhotoFileName
            };
        }

        private async Task<bool> IsEmailTaken(string email, string currentUserId)
        {
            return await _db.Users.AnyAsync(u => u.Email == email && u.Id != currentUserId);
        }

        // FIXED: Explicitly specify ViewModels namespace to resolve ambiguity
        private void UpdateUserFromModel(UserBase user, JobRecruitment.ViewModels.AdminProfileViewModel model)
        {
            user.Username = model.Username;
            user.FullName = model.FullName;
            user.Gender = model.Gender;
            user.Email = model.Email;
            user.Phone = model.Phone;
            user.DateOfBirth = model.DateOfBirth;
        }

        private async Task<FileUploadResult> ProcessProfilePhoto(IFormFile photo, UserBase user)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                return new FileUploadResult { Success = false, ErrorMessage = "Invalid file type" };

            if (photo.Length > 5 * 1024 * 1024)
                return new FileUploadResult { Success = false, ErrorMessage = "File too large (max 5MB)" };

            try
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "profilepics");
                Directory.CreateDirectory(uploadsFolder);

                // Delete old photo
                if (!string.IsNullOrEmpty(user.ProfilePhotoFileName))
                {
                    var oldPath = Path.Combine(uploadsFolder, user.ProfilePhotoFileName);
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(photo.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await photo.CopyToAsync(stream);

                user.ProfilePhotoFileName = fileName;
                return new FileUploadResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing profile photo");
                return new FileUploadResult { Success = false, ErrorMessage = "Error uploading photo" };
            }
        }

        private async Task RefreshUserClaims(UserBase user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email ?? ""),
                new("FullName", user.FullName ?? ""),
                new("ProfilePhotoFileName", user.ProfilePhotoFileName ?? ""),
                new(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
        }

        private async Task<UserFilterResult> GetFilteredUsers(UserFilterViewModel filter)
        {
            var query = _db.Users.AsQueryable();

            if (!string.IsNullOrEmpty(filter.Role) && filter.Role != "All")
                query = query.Where(u => u.Role == filter.Role);

            if (!string.IsNullOrEmpty(filter.Search))
            {
                var searchTerm = filter.Search.ToLower();
                query = query.Where(u =>
                    u.Username.ToLower().Contains(searchTerm) ||
                    u.Email.ToLower().Contains(searchTerm) ||
                    u.FullName.ToLower().Contains(searchTerm));
            }

            var totalCount = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedDate)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new UserFilterResult
            {
                Users = users,
                TotalCount = totalCount,
                Filter = filter
            };
        }


        private async Task<OperationResult> ToggleUserStatus(string id, bool isActive, string action)
        {
            if (string.IsNullOrEmpty(id))
                return new OperationResult { Success = false, ErrorMessage = "Invalid user ID" };

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return new OperationResult { Success = false, ErrorMessage = "User not found" };

            if (user.Id == GetCurrentUserId())
                return new OperationResult { Success = false, ErrorMessage = "You cannot modify your own account" };

            user.IsActive = isActive;
            if (isActive) user.FailedLoginAttempts = 0;

            await _db.SaveChangesAsync();

            _logger.LogInformation($"User {user.Username} {action} by admin {GetCurrentUserName()}");
            return new OperationResult { Success = true, Message = $"User {user.Username} has been {action}" };
        }

        private async Task<OperationResult> DeleteUserById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return new OperationResult { Success = false, ErrorMessage = "Invalid user ID" };

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return new OperationResult { Success = false, ErrorMessage = "User not found" };

            if (user.Id == GetCurrentUserId())
                return new OperationResult { Success = false, ErrorMessage = "You cannot delete your own account" };

            if (user.Role == "Admin")
                return new OperationResult { Success = false, ErrorMessage = "Cannot delete admin accounts" };

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"User {user.Username} deleted by admin {GetCurrentUserName()}");
            return new OperationResult { Success = true, Message = $"User {user.Username} has been deleted" };
        }

        private string GenerateUniqueId()
        {
            return Guid.NewGuid().ToString("N")[..12].ToUpper();
        }

        public IActionResult Logout() => RedirectToAction("Logout", "Account");

        public class BlockRequest
        {
            public string Type { get; set; }
            public string Id { get; set; }
        }

        public async Task<IActionResult> JobReports(string searchTerm, string sortOrder = "desc", string statusFilter = "all", int page = 1, int pageSize = 10)
        {
            var query = _db.JobReports
                .Include(jr => jr.Job)
                .ThenInclude(j => j.Employer)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(jr =>
                    jr.Job.Title.Contains(searchTerm) ||
                    jr.Reason.Contains(searchTerm) ||
                    jr.Job.Employer.CompanyName.Contains(searchTerm));
            }

            // ✅ Status filter
            if (statusFilter == "active")
                query = query.Where(jr => jr.Job.IsActive);
            else if (statusFilter == "blocked")
                query = query.Where(jr => !jr.Job.IsActive);

            query = sortOrder == "asc"
                ? query.OrderBy(jr => jr.DateReported)
                : query.OrderByDescending(jr => jr.DateReported);

            var totalReports = await query.CountAsync();

            var reports = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(jr => new JobReportViewModel
                {
                    Id = jr.Id,
                    JobId = jr.JobId,
                    JobTitle = jr.Job.Title,
                    EmployerName = jr.Job.Employer.CompanyName,
                    Reason = jr.Reason,
                    DateReported = jr.DateReported,
                    IsActive = jr.Job.IsActive
                })
                .ToListAsync();

            ViewBag.TotalReports = totalReports;
            ViewBag.SearchTerm = searchTerm;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.SortOrder = sortOrder;
            ViewBag.StatusFilter = statusFilter;

            return View(reports);
        }


        public async Task<IActionResult> LoadJobReports(string searchTerm, string sortOrder = "desc", string statusFilter = "all", int page = 1, int pageSize = 10)
        {
            var query = _db.JobReports
                .Include(jr => jr.Job)
                .ThenInclude(j => j.Employer)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(jr =>
                    jr.Job.Title.Contains(searchTerm) ||
                    jr.Reason.Contains(searchTerm) ||
                    jr.Job.Employer.CompanyName.Contains(searchTerm));
            }

            // ✅ Status filter
            if (statusFilter == "active")
                query = query.Where(jr => jr.Job.IsActive);
            else if (statusFilter == "blocked")
                query = query.Where(jr => !jr.Job.IsActive);

            // ✅ Sorting
            query = sortOrder == "asc"
                ? query.OrderBy(jr => jr.DateReported)
                : query.OrderByDescending(jr => jr.DateReported);

            var reports = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(jr => new JobReportViewModel
                {
                    Id = jr.Id,
                    JobId = jr.JobId,
                    JobTitle = jr.Job.Title,
                    EmployerName = jr.Job.Employer.CompanyName,
                    Reason = jr.Reason,
                    DateReported = jr.DateReported,
                    IsActive = jr.Job.IsActive
                })
                .ToListAsync();

            ViewBag.SortOrder = sortOrder;

            return PartialView("_JobReportsTable", reports);
        }
        private void SetPaginationViewBag(UserFilterResult result, UserFilterViewModel filter)
        {
            ViewBag.TotalUsers = result.TotalCount;
            ViewBag.CurrentPage = filter.Page;
            ViewBag.PageSize = filter.PageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)result.TotalCount / filter.PageSize);
            ViewBag.CurrentRole = filter.Role;
            ViewBag.CurrentSearch = filter.Search;
        }

        [HttpPost]
        public async Task<IActionResult> ExportEmployersPdf([FromBody] EmployerFilterRequest request)
        {
            try
            {
                var dateRange = GetDateRangeFromDays(request.DateRange);
                var employers = await GetEmployerRankingsFromDB(request, dateRange);

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    // Title and metadata
                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("Top Performing Employers Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                    doc.Add(new Paragraph($"Date Range: Last {request.DateRange} days", normalFont));
                    doc.Add(new Paragraph($"Total Records: {employers.Count}", normalFont));
                    doc.Add(new Paragraph("\n"));

                    // Create table
                    PdfPTable table = new PdfPTable(5);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1f, 3f, 2f, 2f, 2f });

                    // Add headers
                    table.AddCell(new Phrase("Rank", headerFont));
                    table.AddCell(new Phrase("Company Name", headerFont));
                    table.AddCell(new Phrase("Jobs Posted", headerFont));
                    table.AddCell(new Phrase("Applications", headerFont));
                    table.AddCell(new Phrase("Success Rate", headerFont));

                    // Add data
                    int rank = 1;
                    foreach (var employer in employers)
                    {
                        table.AddCell(new Phrase(rank.ToString(), normalFont));
                        table.AddCell(new Phrase(employer.CompanyName, normalFont));
                        table.AddCell(new Phrase(employer.JobsPosted.ToString(), normalFont));
                        table.AddCell(new Phrase(employer.TotalApplications.ToString(), normalFont));
                        table.AddCell(new Phrase($"{employer.SuccessRate:F1}%", normalFont));
                        rank++;
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"EmployersReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting employers PDF");
                return Json(new { success = false, message = "Error generating PDF" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportJobSeekersPdf([FromBody] JobSeekerFilterRequest request)
        {
            try
            {
                var dateRange = GetDateRangeFromDays(request.DateRange);
                var jobSeekers = await GetJobSeekerRankingsFromDB(request, dateRange);

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    // Title and metadata
                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("Job Seekers Performance Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                    doc.Add(new Paragraph($"Date Range: Last {request.DateRange} days", normalFont));
                    doc.Add(new Paragraph($"Total Records: {jobSeekers.Count}", normalFont));
                    doc.Add(new Paragraph("\n"));

                    // Create table
                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1f, 3f, 2f, 2f, 2f, 2f });

                    // Add headers
                    table.AddCell(new Phrase("Rank", headerFont));
                    table.AddCell(new Phrase("Full Name", headerFont));
                    table.AddCell(new Phrase("Applications", headerFont));
                    table.AddCell(new Phrase("Interview Rate", headerFont));
                    table.AddCell(new Phrase("Last Activity", headerFont));
                    table.AddCell(new Phrase("Profile %", headerFont));

                    // Add data
                    int rank = 1;
                    foreach (var seeker in jobSeekers)
                    {
                        table.AddCell(new Phrase(rank.ToString(), normalFont));
                        table.AddCell(new Phrase(seeker.FullName, normalFont));
                        table.AddCell(new Phrase(seeker.TotalApplications.ToString(), normalFont));
                        table.AddCell(new Phrase($"{seeker.InterviewRate:F1}%", normalFont));
                        table.AddCell(new Phrase(seeker.LastActivity.ToString("yyyy-MM-dd"), normalFont));
                        table.AddCell(new Phrase($"{seeker.ProfileCompletionPercentage}%", normalFont));
                        rank++;
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"JobSeekersReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting job seekers PDF");
                return Json(new { success = false, message = "Error generating PDF" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportRecentActivitiesPdf([FromBody] DashboardFilterRequest request)
        {
            try
            {
                var activities = await GetFilteredRecentActivities(request);

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    // Title and metadata
                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("Recent Platform Activity Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                    doc.Add(new Paragraph($"Date Range: Last {request.DateRange} days", normalFont));
                    doc.Add(new Paragraph($"Total Records: {activities.Count}", normalFont));
                    doc.Add(new Paragraph("\n"));

                    // Create table
                    PdfPTable table = new PdfPTable(5);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 2f, 3f, 4f, 2f, 2f });

                    // Add headers
                    table.AddCell(new Phrase("Activity Type", headerFont));
                    table.AddCell(new Phrase("User/Company", headerFont));
                    table.AddCell(new Phrase("Description", headerFont));
                    table.AddCell(new Phrase("Date", headerFont));
                    table.AddCell(new Phrase("Status", headerFont));

                    // Add data
                    foreach (var activity in activities)
                    {
                        table.AddCell(new Phrase(activity.ActivityTypeDisplay ?? "Unknown", normalFont));
                        table.AddCell(new Phrase(activity.UserOrCompany ?? "Unknown", normalFont));
                        table.AddCell(new Phrase(activity.Description ?? "No description", normalFont));
                        table.AddCell(new Phrase(activity.CreatedDate.ToString("yyyy-MM-dd HH:mm"), normalFont));
                        table.AddCell(new Phrase(activity.StatusDisplay ?? "Unknown", normalFont));
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"RecentActivitiesReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting recent activities PDF");
                return Json(new { success = false, message = "Error generating PDF" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportUserTrendsChartPdf([FromBody] DashboardFilterRequest request)
        {
            try
            {
                var response = await GetFilteredDashboardData(request);
                var result = response as JsonResult;
                var data = result?.Value as FilteredDashboardResponse;

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30); // Landscape
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("User Registration Trends Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));

                    if (data?.FilterSummary != null)
                    {
                        doc.Add(new Paragraph($"Filters Applied: {data.FilterSummary}", normalFont));
                    }

                    doc.Add(new Paragraph("\n"));

                    // Add chart data as table
                    if (data?.ChartData?.MonthLabels != null)
                    {
                        var chartData = data.ChartData;
                        PdfPTable table = new PdfPTable(3);
                        table.WidthPercentage = 70;
                        table.SetWidths(new float[] { 2f, 2f, 2f });

                        table.AddCell(new Phrase("Period", headerFont));
                        table.AddCell(new Phrase("Job Seekers", headerFont));
                        table.AddCell(new Phrase("Employers", headerFont));

                        for (int i = 0; i < chartData.MonthLabels.Count && i < chartData.JobSeekerTrends.Count && i < chartData.EmployerTrends.Count; i++)
                        {
                            table.AddCell(new Phrase(chartData.MonthLabels[i], normalFont));
                            table.AddCell(new Phrase(chartData.JobSeekerTrends[i].ToString(), normalFont));
                            table.AddCell(new Phrase(chartData.EmployerTrends[i].ToString(), normalFont));
                        }

                        doc.Add(table);

                        // Add summary statistics
                        doc.Add(new Paragraph("\n"));
                        doc.Add(new Paragraph("Summary Statistics:", headerFont));

                        var totalJobSeekers = chartData.JobSeekerTrends.Sum();
                        var totalEmployers = chartData.EmployerTrends.Sum();
                        var avgJobSeekers = chartData.JobSeekerTrends.Any() ? chartData.JobSeekerTrends.Average() : 0;
                        var avgEmployers = chartData.EmployerTrends.Any() ? chartData.EmployerTrends.Average() : 0;

                        doc.Add(new Paragraph($"Total Job Seekers Registered: {totalJobSeekers}", normalFont));
                        doc.Add(new Paragraph($"Total Employers Registered: {totalEmployers}", normalFont));
                        doc.Add(new Paragraph($"Average Job Seekers per Period: {avgJobSeekers:F1}", normalFont));
                        doc.Add(new Paragraph($"Average Employers per Period: {avgEmployers:F1}", normalFont));
                    }

                    doc.Close();
                    return File(ms.ToArray(), "application/pdf", $"UserTrendsReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting user trends chart PDF");
                return Json(new { success = false, message = "Error generating chart PDF" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportUserDistributionChartPdf([FromBody] DashboardFilterRequest request)
        {
            try
            {
                var stats = await GetFilteredDashboardStats(request);

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("User Distribution Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                    doc.Add(new Paragraph("\n"));

                    // Create distribution table
                    PdfPTable table = new PdfPTable(3);
                    table.WidthPercentage = 70;
                    table.SetWidths(new float[] { 2f, 2f, 2f });

                    table.AddCell(new Phrase("User Type", headerFont));
                    table.AddCell(new Phrase("Count", headerFont));
                    table.AddCell(new Phrase("Percentage", headerFont));

                    // Calculate percentages
                    var total = stats.TotalJobSeekers + stats.TotalEmployers + stats.TotalAdmins;
                    var jsPercent = total > 0 ? (stats.TotalJobSeekers * 100.0 / total) : 0;
                    var empPercent = total > 0 ? (stats.TotalEmployers * 100.0 / total) : 0;
                    var adminPercent = total > 0 ? (stats.TotalAdmins * 100.0 / total) : 0;

                    table.AddCell(new Phrase("Job Seekers", normalFont));
                    table.AddCell(new Phrase(stats.TotalJobSeekers.ToString(), normalFont));
                    table.AddCell(new Phrase($"{jsPercent:F1}%", normalFont));

                    table.AddCell(new Phrase("Employers", normalFont));
                    table.AddCell(new Phrase(stats.TotalEmployers.ToString(), normalFont));
                    table.AddCell(new Phrase($"{empPercent:F1}%", normalFont));

                    table.AddCell(new Phrase("Admins", normalFont));
                    table.AddCell(new Phrase(stats.TotalAdmins.ToString(), normalFont));
                    table.AddCell(new Phrase($"{adminPercent:F1}%", normalFont));

                    doc.Add(table);

                    doc.Add(new Paragraph("\n"));
                    doc.Add(new Paragraph("Summary:", headerFont));
                    doc.Add(new Paragraph($"Total Users: {total}", normalFont));
                    doc.Add(new Paragraph($"Most Common User Type: {(stats.TotalJobSeekers >= stats.TotalEmployers ? "Job Seekers" : "Employers")}", normalFont));

                    doc.Close();
                    return File(ms.ToArray(), "application/pdf", $"UserDistributionReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting user distribution chart PDF");
                return Json(new { success = false, message = "Error generating chart PDF" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportJobStatusChartPdf([FromBody] DashboardFilterRequest request)
        {
            try
            {
                var jobStats = await GetFilteredJobStats(request);

                using (MemoryStream ms = new MemoryStream())
                {
                    Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    doc.Add(new Paragraph("Job Status Overview Report", titleFont));
                    doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                    doc.Add(new Paragraph("\n"));

                    PdfPTable table = new PdfPTable(3);
                    table.WidthPercentage = 70;
                    table.SetWidths(new float[] { 2f, 2f, 2f });

                    table.AddCell(new Phrase("Job Status", headerFont));
                    table.AddCell(new Phrase("Count", headerFont));
                    table.AddCell(new Phrase("Percentage", headerFont));

                    var total = jobStats.ActiveJobs + jobStats.ClosedJobs + jobStats.DraftJobs;
                    var activePercent = total > 0 ? (jobStats.ActiveJobs * 100.0 / total) : 0;
                    var closedPercent = total > 0 ? (jobStats.ClosedJobs * 100.0 / total) : 0;
                    var draftPercent = total > 0 ? (jobStats.DraftJobs * 100.0 / total) : 0;

                    table.AddCell(new Phrase("Active Jobs", normalFont));
                    table.AddCell(new Phrase(jobStats.ActiveJobs.ToString(), normalFont));
                    table.AddCell(new Phrase($"{activePercent:F1}%", normalFont));

                    table.AddCell(new Phrase("Closed Jobs", normalFont));
                    table.AddCell(new Phrase(jobStats.ClosedJobs.ToString(), normalFont));
                    table.AddCell(new Phrase($"{closedPercent:F1}%", normalFont));

                    table.AddCell(new Phrase("Draft Jobs", normalFont));
                    table.AddCell(new Phrase(jobStats.DraftJobs.ToString(), normalFont));
                    table.AddCell(new Phrase($"{draftPercent:F1}%", normalFont));

                    doc.Add(table);

                    doc.Add(new Paragraph("\n"));
                    doc.Add(new Paragraph("Insights:", headerFont));
                    doc.Add(new Paragraph($"Total Jobs: {total}", normalFont));

                    if (total > 0)
                    {
                        var completionRate = (jobStats.ClosedJobs * 100.0 / (jobStats.ActiveJobs + jobStats.ClosedJobs));
                        doc.Add(new Paragraph($"Job Completion Rate: {completionRate:F1}%", normalFont));
                    }

                    if (jobStats.DraftJobs > 0)
                    {
                        doc.Add(new Paragraph($"Jobs Pending Publication: {jobStats.DraftJobs} ({draftPercent:F1}%)", normalFont));
                    }

                    doc.Close();
                    return File(ms.ToArray(), "application/pdf", $"JobStatusReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting job status chart PDF");
                return Json(new { success = false, message = "Error generating chart PDF" });
            }
        }
                [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LockedUsers()
        {
            try
            {
                // Get all users with failed login attempts >= 5 (locked users)
                var lockedUsers = await _db.Users
                    .Where(u => u.FailedLoginAttempts >= 5)
                    .OrderByDescending(u => u.FailedLoginAttempts)
                    .ThenBy(u => u.Username)
                    .Select(u => new LockedUserViewModel
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.FullName,
                        Role = u.Role,
                        FailedLoginAttempts = u.FailedLoginAttempts,
                        IsActive = u.IsActive,
                        CreatedDate = u.CreatedDate,
                        CompanyName = u is Employer ? ((Employer)u).CompanyName : null
                    })
                    .ToListAsync();

                _logger.LogInformation("Admin {AdminUsername} accessed locked users page - Found {Count} locked users",
                    User.Identity?.Name, lockedUsers.Count);

                return View(lockedUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading locked users for admin {AdminUsername}", User.Identity?.Name);
                TempData["Error"] = "An error occurred while loading locked users.";
                return RedirectToAction("AdminDashboard");
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockUser(string userId, string reason = "")
        {
            var adminUsername = User.Identity?.Name;
            var clientIp = GetClientIpAddress();

            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "Invalid user ID" });
                }

                var user = await _db.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("Admin {AdminUsername} attempted to unlock non-existent user ID {UserId}",
                        adminUsername, userId);
                    return Json(new { success = false, message = "User not found" });
                }

                if (user.FailedLoginAttempts == 0)
                {
                    return Json(new { success = false, message = "User is not currently locked" });
                }

                // Reset failed login attempts
                var previousAttempts = user.FailedLoginAttempts;
                user.FailedLoginAttempts = 0;
                _db.Entry(user).Property(u => u.FailedLoginAttempts).IsModified = true;
                await _db.SaveChangesAsync();

                // Log the unlock action
                _logger.LogInformation("Admin {AdminUsername} unlocked user {Username} (ID: {UserId}) from IP {IP}. " +
                                     "Previous failed attempts: {PreviousAttempts}. Reason: {Reason}",
                                     adminUsername, user.Username, userId, clientIp, previousAttempts,
                                     string.IsNullOrEmpty(reason) ? "No reason provided" : reason);

                // Optional: Send notification email to the unlocked user
                try
                {
                    if (!string.IsNullOrEmpty(user.Email) && _emailSender != null)
                    {
                        var emailSubject = "Account Unlocked - HireRight Portal";
                        var emailBody = CreateAccountUnlockedEmailBody(user.FullName, adminUsername, DateTime.UtcNow);
                        await _emailSender.SendEmailAsync(user.Email, emailSubject, emailBody);

                        _logger.LogInformation("Account unlock notification sent to {Email}", user.Email);
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send unlock notification email to {Email}", user.Email);
                    // Don't fail the entire operation if email fails
                }

                return Json(new
                {
                    success = true,
                    message = $"User {user.Username} has been successfully unlocked.",
                    userDetails = new
                    {
                        username = user.Username,
                        email = user.Email,
                        previousAttempts = previousAttempts
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unlocking user {UserId} by admin {AdminUsername}", userId, adminUsername);
                return Json(new { success = false, message = "An error occurred while unlocking the user" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUnlockUsers(List<string> userIds, string reason = "")
        {
            var adminUsername = User.Identity?.Name;
            var clientIp = GetClientIpAddress();

            try
            {
                if (userIds == null || !userIds.Any())
                {
                    return Json(new { success = false, message = "No users selected" });
                }

                var users = await _db.Users
                    .Where(u => userIds.Contains(u.Id) && u.FailedLoginAttempts >= 5)
                    .ToListAsync();

                if (!users.Any())
                {
                    return Json(new { success = false, message = "No locked users found in selection" });
                }

                var unlockedCount = 0;
                var unlockedUsers = new List<string>();

                using var transaction = await _db.Database.BeginTransactionAsync();

                try
                {
                    foreach (var user in users)
                    {
                        var previousAttempts = user.FailedLoginAttempts;
                        user.FailedLoginAttempts = 0;
                        unlockedCount++;
                        unlockedUsers.Add(user.Username);

                        _logger.LogInformation("Admin {AdminUsername} bulk unlocked user {Username} (ID: {UserId}). " +
                                             "Previous failed attempts: {PreviousAttempts}",
                                             adminUsername, user.Username, user.Id, previousAttempts);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Admin {AdminUsername} completed bulk unlock of {Count} users from IP {IP}. " +
                                         "Users: {UsersList}. Reason: {Reason}",
                                         adminUsername, unlockedCount, clientIp, string.Join(", ", unlockedUsers),
                                         string.IsNullOrEmpty(reason) ? "No reason provided" : reason);

                    return Json(new
                    {
                        success = true,
                        message = $"Successfully unlocked {unlockedCount} user(s).",
                        unlockedCount = unlockedCount,
                        unlockedUsers = unlockedUsers
                    });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk unlock by admin {AdminUsername}", adminUsername);
                return Json(new { success = false, message = "An error occurred during bulk unlock operation" });
            }
        }

                private string GetClientIpAddress()
        {
            try
            {
                var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xForwardedFor))
                {
                    return xForwardedFor.Split(',')[0].Trim();
                }

                var xRealIp = Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xRealIp))
                {
                    return xRealIp;
                }

                return Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        // Helper method to create unlock notification email
        private string CreateAccountUnlockedEmailBody(string fullName, string adminUsername, DateTime unlockedAt)
        {
            return $@"
    <!DOCTYPE html>
    <html>
    <head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Account Unlocked - HireRight Portal</title>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 0; background-color: #f8f9fa; line-height: 1.6; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background-color: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); }}
        .header {{ text-align: center; padding: 30px 0; border-bottom: 2px solid #e9ecef; }}
        .success-icon {{ font-size: 64px; color: #28a745; margin-bottom: 20px; }}
        .main-content {{ padding: 40px 20px; }}
        .unlock-info {{ background: #d4edda; border-left: 4px solid #28a745; padding: 20px; margin: 30px 0; border-radius: 0 8px 8px 0; }}
        .footer {{ text-align: center; padding: 30px 20px 20px; border-top: 1px solid #e9ecef; font-size: 12px; color: #6c757d; }}
    </style>
    </head>
    <body>
    <div class='container'>
        <div class='header'>
            <div class='success-icon'>🔓</div>
            <h2 style='color: #28a745; margin: 0;'>Account Unlocked</h2>
        </div>
        
        <div class='main-content'>
            <p>Hello {fullName},</p>
            <p>Your HireRightPro account has been unlocked by our administrator and you can now log in again.</p>
            
            <div class='unlock-info'>
                <h4>Unlock Details:</h4>
                <p><strong>Unlocked by:</strong> Administrator</p>
                <p><strong>Date & Time:</strong> {unlockedAt:F} UTC</p>
            </div>
            
            <p>You can now proceed to <a href='{Request.Scheme}://{Request.Host}/Account/Login' style='color: #667eea;'>login to your account</a> using your existing credentials.</p>
            
            <p>If you continue to experience login issues, please contact our support team.</p>
        </div>
        
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} HireRightPro. All rights reserved.</p>
        </div>
    </div>
    </body>
    </html>";
        }
        public IActionResult JobReportDetails(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var report = _db.JobReports
                            .Include(r => r.Job)
                                .ThenInclude(j => j.Employer)
                            .Where(r => r.Id == id)
                            .Select(r => new JobReportViewModel
                            {
                                Id = r.Id,
                                Reason = r.Reason,
                                DateReported = r.DateReported,
                                JobId = r.JobId,
                                JobTitle = r.Job.Title,           // Now this will work
                                EmployerName = r.Job.Employer.CompanyName,  // And this too
                                IsActive = r.Job.IsActive
                            })
                            .FirstOrDefault();

            if (report == null)
            {
                return NotFound();
            }

            return View(report);
        }
        // AI-Integration Services

        // ========== AI INTEGRATION METHODS (NEW) ==========

        [HttpPost]
        public async Task<IActionResult> GenerateAIInsights([FromBody] AIInsightRequest request)
        {
            try
            {
                _logger.LogInformation("Generating AI insights for report type: {ReportType}", request.ReportType);

                var prompt = _openAIService.BuildAnalysisPrompt(request.Data, request.ReportType);
                var insights = await _openAIService.GenerateInsightsAsync(prompt, request.ReportType);

                return Json(new { success = true, insights = insights });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI insights for report type: {ReportType}", request.ReportType);
                return Json(new { success = false, message = "Failed to generate AI insights" });
            }
        }
        [HttpPost]
        public async Task<IActionResult> GenerateAIEnhancedReport([FromBody] AIReportRequest request)
        {
            try
            {
                _logger.LogInformation("Generating AI-enhanced report for type: {ReportType}", request.ReportType);

                // Get data based on report type
                var reportData = await GetReportData(request);

                // Generate AI insights
                var prompt = _openAIService.BuildAnalysisPrompt(reportData, request.ReportType);
                var aiInsights = await _openAIService.GenerateInsightsAsync(prompt, request.ReportType);

                // Generate enhanced PDF
                var pdfBytes = await GenerateAIEnhancedPdf(reportData, aiInsights, request);

                var fileName = $"AI_Enhanced_{request.ReportType}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI-enhanced report");
                return Json(new { success = false, message = "Failed to generate AI report" });
            }
        }

        private async Task<object> GetReportData(AIReportRequest request)
        {
            var dateRange = GetDateRangeFromDays(request.DateRange);

            return request.ReportType switch
            {
                "employers" => await GetEmployerRankingsFromDB(new EmployerFilterRequest
                {
                    DateRange = request.DateRange,
                    Count = request.Count,
                    SortBy = request.SortBy ?? "success_rate"
                }, dateRange),

                "jobSeekers" => await GetJobSeekerRankingsFromDB(new JobSeekerFilterRequest
                {
                    DateRange = request.DateRange,
                    Count = request.Count,
                    SortBy = request.SortBy ?? "most_active"
                }, dateRange),

                "dashboard" => new
                {
                    Stats = await GetFilteredDashboardStats(new DashboardFilterRequest
                    {
                        DateRange = request.DateRange,
                        UserType = request.UserType ?? "all",
                        Status = request.Status ?? "all"
                    }),
                    Activities = await GetFilteredRecentActivities(new DashboardFilterRequest
                    {
                        DateRange = request.DateRange
                    })
                },

                _ => throw new ArgumentException($"Unknown report type: {request.ReportType}")
            };
        }

        private async Task<byte[]> GenerateAIEnhancedPdf(object data, string aiInsights, AIReportRequest request)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // Fonts
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18, BaseColor.DARK_GRAY);
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14);
                var subHeaderFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 11);
                var aiFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.BLUE);

                // Title
                doc.Add(new Paragraph($"🤖 AI-Enhanced {request.ReportType.ToUpperInvariant()} Analysis Report", titleFont));
                doc.Add(new Paragraph($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                doc.Add(new Paragraph($"Analysis Period: Last {request.DateRange} days", normalFont));
                doc.Add(new Paragraph("\n"));

                // AI Insights Section
                doc.Add(new Paragraph("🧠 AI Business Intelligence Analysis", headerFont));
                doc.Add(new Paragraph("─".PadRight(50, '─'), normalFont));

                // Format AI insights with better styling
                var insightsParagraph = new Paragraph(aiInsights, aiFont)
                {
                    SpacingAfter = 15f
                };
                doc.Add(insightsParagraph);

                // Data visualization section
                doc.Add(new Paragraph("📊 Detailed Data Analysis", headerFont));
                doc.Add(new Paragraph("─".PadRight(50, '─'), normalFont));

                // Add data based on report type
                await AddDataSection(doc, data, request.ReportType, normalFont, subHeaderFont);

                // Key recommendations section
                doc.Add(new Paragraph("\n"));
                doc.Add(new Paragraph("💡 Key Recommendations", headerFont));
                doc.Add(new Paragraph("─".PadRight(50, '─'), normalFont));

                var recommendations = GenerateRecommendations(request.ReportType, data);
                foreach (var recommendation in recommendations)
                {
                    doc.Add(new Paragraph($"• {recommendation}", normalFont) { SpacingAfter = 5f });
                }

                // Footer
                doc.Add(new Paragraph("\n"));
                doc.Add(new Paragraph("─".PadRight(50, '─'), normalFont));
                doc.Add(new Paragraph("This report was generated using AI analysis to provide actionable business insights.", normalFont));

                doc.Close();
                return ms.ToArray();
            }
        }

        private async Task AddDataSection(Document doc, object data, string reportType, Font normalFont, Font subHeaderFont)
        {
            switch (reportType)
            {
                case "employers":
                    var employers = data as List<EmployerRankingData>;
                    if (employers?.Any() == true)
                    {
                        doc.Add(new Paragraph("Top Performing Employers", subHeaderFont));

                        PdfPTable table = new PdfPTable(5);
                        table.WidthPercentage = 100;

                        // Headers
                        table.AddCell("Rank");
                        table.AddCell("Company");
                        table.AddCell("Jobs Posted");
                        table.AddCell("Applications");
                        table.AddCell("Success Rate");

                        // Data
                        int rank = 1;
                        foreach (var employer in employers.Take(10))
                        {
                            table.AddCell(rank++.ToString());
                            table.AddCell(employer.CompanyName);
                            table.AddCell(employer.JobsPosted.ToString());
                            table.AddCell(employer.TotalApplications.ToString());
                            table.AddCell($"{employer.SuccessRate:F1}%");
                        }

                        doc.Add(table);
                    }
                    break;

                case "jobSeekers":
                    var seekers = data as List<JobSeekerRankingData>;
                    if (seekers?.Any() == true)
                    {
                        doc.Add(new Paragraph("Job Seekers Performance", subHeaderFont));

                        PdfPTable table = new PdfPTable(4);
                        table.WidthPercentage = 100;

                        table.AddCell("Rank");
                        table.AddCell("Name");
                        table.AddCell("Applications");
                        table.AddCell("Success Rate");

                        int rank = 1;
                        foreach (var seeker in seekers.Take(10))
                        {
                            table.AddCell(rank++.ToString());
                            table.AddCell(seeker.FullName);
                            table.AddCell(seeker.TotalApplications.ToString());
                            table.AddCell($"{seeker.InterviewRate:F1}%");
                        }

                        doc.Add(table);
                    }
                    break;
            }
        }

        private List<string> GenerateRecommendations(string reportType, object data)
        {
            return reportType switch
            {
                "employers" => new List<string>
        {
            "Focus on supporting employers with lower success rates",
            "Analyze top performers to identify best practices",
            "Consider implementing employer training programs",
            "Improve job posting quality guidelines"
        },
                "jobSeekers" => new List<string>
        {
            "Encourage profile completion for better match rates",
            "Provide application tips to less active users",
            "Implement skill development recommendations",
            "Create user engagement campaigns"
        },
                _ => new List<string>
        {
            "Monitor platform health metrics regularly",
            "Focus on balanced growth between user types",
            "Implement data-driven improvement strategies"
        }
            };
        }

        public class AIReportRequest
        {
            public string ReportType { get; set; } = "dashboard";
            public int DateRange { get; set; } = 30;
            public string UserType { get; set; } = "all";
            public string Status { get; set; } = "all";
            public int Count { get; set; } = 10;
            public string SortBy { get; set; }
            public bool IncludeCharts { get; set; } = true;
        }
        [HttpPost]
        public async Task<IActionResult> TestAISetup()
        {
            try
            {
                // Test basic AI integration
                var testData = new { message = "Testing AI integration", timestamp = DateTime.Now };
                var prompt = _openAIService.BuildAnalysisPrompt(testData, "test");
                var insights = await _openAIService.GenerateInsightsAsync(prompt, "test");

                return Json(new
                {
                    success = true,
                    message = "AI integration working successfully!",
                    insights = insights.Length > 100 ? insights.Substring(0, 100) + "..." : insights
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI integration test failed");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Enhanced Employers PDF with AI insights
        [HttpPost]
        public async Task<IActionResult> ExportEnhancedEmployersPdf([FromBody] EnhancedPdfRequest request)
        {
            try
            {
                _logger.LogInformation("Generating enhanced employers PDF with AI insights");

                var dateRange = GetDateRangeFromDays(request.DateRange);

                // Create the employerRequest FIRST
                var employerRequest = new EmployerFilterRequest
                {
                    DateRange = request.DateRange,
                    UserType = request.UserType,
                    Count = request.Count,
                    SortBy = request.SortBy,
                    Order = "desc"
                };

                // THEN use it in the method call
                var employersData = await GetEmployerRankingsFromDB(employerRequest, dateRange);

                // Generate AI insights
                var prompt = _openAIService.BuildAnalysisPrompt(employersData, "employers");
                var aiInsights = await _openAIService.GenerateInsightsAsync(prompt, "employers");

                // Generate enhanced PDF
                var pdfBytes = await GenerateEnhancedEmployersPdf(employersData, aiInsights, request);

                var fileName = $"AI_Enhanced_Employers_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating enhanced employers PDF");
                return Json(new { success = false, message = "Failed to generate enhanced PDF" });
            }
        }

        // Helper method for enhanced PDF generation
        private async Task<byte[]> GenerateEnhancedEmployersPdf(List<EmployerRankingData> employersData, string aiInsights, EnhancedPdfRequest request)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // Title and metadata
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                var aiFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.DARK_GRAY);

                doc.Add(new Paragraph("🤖 AI-Enhanced Employers Performance Report", titleFont));
                doc.Add(new Paragraph($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", normalFont));
                doc.Add(new Paragraph($"Date Range: Last {request.DateRange} days", normalFont));
                doc.Add(new Paragraph($"Total Records: {employersData.Count}", normalFont));
                doc.Add(new Paragraph("\n"));

                // AI Insights Section
                doc.Add(new Paragraph("AI Business Intelligence Analysis", headerFont));
                doc.Add(new Paragraph("────────────────────────────────────", normalFont));
                doc.Add(new Paragraph(aiInsights, aiFont));
                doc.Add(new Paragraph("\n"));

                // Data Table Section
                doc.Add(new Paragraph("Detailed Rankings", headerFont));
                doc.Add(new Paragraph("────────────────────────────────────", normalFont));

                // Create table
                PdfPTable table = new PdfPTable(5);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 1f, 3f, 2f, 2f, 2f });

                // Add headers
                table.AddCell(new Phrase("Rank", headerFont));
                table.AddCell(new Phrase("Company Name", headerFont));
                table.AddCell(new Phrase("Jobs Posted", headerFont));
                table.AddCell(new Phrase("Applications", headerFont));
                table.AddCell(new Phrase("Success Rate", headerFont));

                // Add data
                int rank = 1;
                foreach (var employer in employersData.Take(20))
                {
                    table.AddCell(new Phrase(rank.ToString(), normalFont));
                    table.AddCell(new Phrase(employer.CompanyName, normalFont));
                    table.AddCell(new Phrase(employer.JobsPosted.ToString(), normalFont));
                    table.AddCell(new Phrase(employer.TotalApplications.ToString(), normalFont));
                    table.AddCell(new Phrase($"{employer.SuccessRate:F1}%", normalFont));
                    rank++;
                }

                doc.Add(table);
                doc.Close();

                return ms.ToArray();
            }
        }

        // Request models for AI integration
        public class AIInsightRequest
        {
            public string ReportType { get; set; }
            public object Data { get; set; }
        }

        public class EnhancedPdfRequest : EmployerFilterRequest
        {
            public int DateRange { get; set; } = 30;
            public string UserType { get; set; } = "all";
            public string Status { get; set; } = "all";
            public int Count { get; set; } = 10;
            public string SortBy { get; set; } = "success_rate";
            public bool IncludeCharts { get; set; } = true;
        }

        // ========== ENHANCED FILTERING REQUEST MODELS (NEW) ==========

        public class TopUsersFilterRequest
        {
            [Range(1, 50, ErrorMessage = "Count must be between 1 and 50")]
            public int Count { get; set; } = 10;

            [Required]
            public string SortBy { get; set; } = "applications";

            [Required]
            public string Order { get; set; } = "desc";

            [Range(1, 365, ErrorMessage = "Date range must be between 1 and 365 days")]
            public int DateRange { get; set; } = 30;

            [Required]
            public string UserType { get; set; } = "all";
        }

        public class ActivityFilterRequest
        {
            [Range(1, 100, ErrorMessage = "Count must be between 1 and 100")]
            public int Count { get; set; } = 10;

            [Required]
            public string ActivityType { get; set; } = "all";

            [Range(1, 365, ErrorMessage = "Date range must be between 1 and 365 days")]
            public int DateRange { get; set; } = 30;
        }

        public class EmployerFilterRequest
        {
            [Range(1, 100, ErrorMessage = "Count must be between 1 and 100")]
            public int Count { get; set; } = 10;

            [Required]
            public string SortBy { get; set; } = "success_rate";

            [Required]
            public string Order { get; set; } = "desc";

            [Range(1, 365, ErrorMessage = "Date range must be between 1 and 365 days")]
            public int DateRange { get; set; } = 30;

            [Required]
            public string UserType { get; set; } = "all";
        }

        public class JobSeekerFilterRequest
        {
            [Range(1, 100, ErrorMessage = "Count must be between 1 and 100")]
            public int Count { get; set; } = 10;

            [Required]
            public string SortBy { get; set; } = "most_active";

            [Required]
            public string Order { get; set; } = "desc";

            [Range(1, 365, ErrorMessage = "Date range must be between 1 and 365 days")]
            public int DateRange { get; set; } = 30;
        }

        // ========== ENHANCED FILTERING DATA MODELS (NEW) ==========

        public class UserActivityData
        {
            public string UserDisplayName { get; set; }
            public int ActivityScore { get; set; }
            public string UserType { get; set; }
        }

        public class EmployerRankingData
        {
            public string CompanyName { get; set; }
            public int JobsPosted { get; set; }
            public int TotalApplications { get; set; }
            public decimal SuccessRate { get; set; }
            public string AverageResponseTime { get; set; }
        }

        public class JobSeekerRankingData
        {
            public string FullName { get; set; }
            public int TotalApplications { get; set; }
            public decimal InterviewRate { get; set; }
            public DateTime LastActivity { get; set; }
            public int ProfileCompletionPercentage { get; set; }
        }


        // ========== EXISTING HELPER CLASSES (UNCHANGED) ==========
        public class TrendsDataResult
        {
            public List<int> JobSeekerTrends { get; set; } = new();
            public List<int> EmployerTrends { get; set; } = new();
            public List<int> ApplicationTrends { get; set; } = new();
        }

        public class FileUploadResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class OperationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class UserFilterResult
        {
            public List<UserBase> Users { get; set; }
            public int TotalCount { get; set; }
            public UserFilterViewModel Filter { get; set; }
        }
        public class DashboardFilterRequest
        {
            public int DateRange { get; set; } = 7;
            public string UserType { get; set; } = "all";
            public string Status { get; set; } = "all";
            public string Search { get; set; } = "";
            public DateTime? CustomStartDate { get; set; }
            public DateTime? CustomEndDate { get; set; }
        }
    }
}