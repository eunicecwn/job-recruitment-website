using ClosedXML.Excel;
using JobRecruitment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "Employer")]
    public class EmployerController : Controller
    {
        private readonly DB db;

        public EmployerController(DB context)
        {
            db = context;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            SetCacheHeaders();
            SetViewBagUserInfo();
            base.OnActionExecuting(context);
        }

        private string GetCurrentEmployerId()
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(employerId))
                throw new InvalidOperationException("User is not authenticated or employer ID not found.");
            return employerId;
        }

        // Employer Dashboard
        public async Task<IActionResult> EmployerDashboard()
        {
            var currentEmployerId = GetCurrentEmployerId();

            // Get the first employer for demo purposes (no authentication)
            var employer = await db.Employers
                .FirstOrDefaultAsync(e => e.Id == currentEmployerId);

            if (employer == null)
            {
                return NotFound("Employer not found");
            }

            // Get all jobs and applications for CURRENT employer only
            var jobs = await db.Jobs
                .Include(j => j.Applications)
                .ThenInclude(a => a.JobSeeker)
                .Where(j => j.EmployerId == currentEmployerId) // Filter by current employer
                .ToListAsync();

            var applications = await db.Applications
                .Include(a => a.Job)
                .Include(a => a.JobSeeker)
                .Where(a => a.Job.EmployerId == currentEmployerId) // Filter by current employer
                .ToListAsync();

            ViewBag.Jobs = jobs;
            ViewBag.Applications = applications;

            return View(employer);
        }

        // Employer Reports - Company Specific
        [HttpGet]
        public async Task<IActionResult> EmployerReports(string dateRange, string jobId, string status)
        {
            var currentEmployerId = GetCurrentEmployerId();

            var employer = await db.Employers
                .FirstOrDefaultAsync(e => e.Id == currentEmployerId);

            if (employer == null)
            {
                return NotFound("Employer not found");
            }

            // Query ONLY applications for THIS employer's jobs
            var applicationsQuery = db.Applications
                .Include(a => a.Job)
                .Include(a => a.JobSeeker)
                .Where(a => a.Job.EmployerId == currentEmployerId) // Filter by current employer
                .AsQueryable();

            // Apply filters if provided
            if (!string.IsNullOrEmpty(dateRange) && int.TryParse(dateRange, out var days))
            {
                var startDate = DateTime.Now.AddDays(-days);
                applicationsQuery = applicationsQuery.Where(a => a.AppliedDate >= startDate);
            }

            if (!string.IsNullOrEmpty(jobId))
            {
                applicationsQuery = applicationsQuery.Where(a => a.JobId == jobId);
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ApplicationStatusEnum>(status, out var statusEnum))
            {
                applicationsQuery = applicationsQuery.Where(a => a.Status == statusEnum);
            }

            var applications = await applicationsQuery.ToListAsync();

            // Get ONLY this employer's jobs
            var jobs = await db.Jobs
                .Where(j => j.EmployerId == employer.Id) // ✅ Filter by employer
                .ToListAsync();

            // Calculate monthly application data for timeline chart
            var now = DateTime.Now;
            var last6Months = Enumerable.Range(0, 6)
                .Select(i => now.AddMonths(-i).ToString("MMM yyyy"))
                .Reverse()
                .ToList();

            var monthlyApplications = applications
                .Where(a => a.AppliedDate >= now.AddMonths(-6))
                .GroupBy(a => new { a.AppliedDate.Year, a.AppliedDate.Month })
                .Select(g => new TimelineData
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    Count = g.Count()
                })
                .ToList();

            // Fill in missing months with zero counts
            var timelineData = last6Months.Select(month => new TimelineData
            {
                Month = month,
                Count = monthlyApplications.FirstOrDefault(m => m.Month == month)?.Count ?? 0
            }).ToList();

            // Create status count data
            var statusCounts = applications
                .GroupBy(a => a.Status)
                .Select(g => new StatusCountData
                {
                    Status = g.Key.ToString(),
                    Count = g.Count()
                })
                .ToList();

            // Create and populate the ViewModel
            var viewModel = new EmployerReportsViewModel
            {
                Employer = employer,
                Jobs = jobs,
                Applications = applications,
                TimelineData = timelineData,
                StatusCounts = statusCounts
            };

            return View(viewModel);
        }

        // Export to PDF - Company Specific
        public async Task<IActionResult> ExportToPDF(string dateRange, string jobId, string status)
        {
            // Get employer info
            var currentEmployerId = GetCurrentEmployerId();
            var employer = await db.Employers.FirstOrDefaultAsync(e => e.Id == currentEmployerId);

            if (employer == null)
            {
                return NotFound("Employer not found");
            }

            // Get filtered applications FOR THIS EMPLOYER ONLY
            var applications = await GetFilteredApplications(dateRange, jobId, status, employer.Id);

            // Get employer's jobs for summary statistics
            var jobs = await db.Jobs
                .Where(j => j.EmployerId == employer.Id)
                .ToListAsync();

            // Calculate summary statistics
            int totalJobs = jobs.Count;
            int totalApplications = applications.Count;
            double interviewRate = totalApplications > 0 ?
                Math.Round((applications.Count(a => a.Status == ApplicationStatusEnum.InterviewScheduled) / (double)totalApplications) * 100, 1) : 0;
            double hireRate = totalApplications > 0 ?
                Math.Round((applications.Count(a => a.Status == ApplicationStatusEnum.Hired) / (double)totalApplications) * 100, 1) : 0;

            // Generate PDF
            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text($"{employer.CompanyName} - Applications Report")
                        .SemiBold().FontSize(24).FontColor(Colors.Blue.Medium);

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(column =>
                        {
                            // Report summary
                            column.Item().Text($"Company: {employer.CompanyName}");
                            column.Item().Text($"Email: {employer.Email}");
                            column.Item().Text($"Report Period: {GetDateRangeText(dateRange)}");
                            column.Item().Text($"Generated on: {DateTime.Now:yyyy-MM-dd HH:mm}");

                            // Add summary statistics section
                            column.Item().PaddingTop(0.5f, Unit.Centimetre).Text("Summary Statistics:").Bold();

                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                // Summary statistics
                                table.Cell().Text("Total Jobs Posted:");
                                table.Cell().Text(totalJobs.ToString());

                                table.Cell().Text("Total Applications:");
                                table.Cell().Text(totalApplications.ToString());

                                table.Cell().Text("Interview Rate:");
                                table.Cell().Text($"{interviewRate}%");

                                table.Cell().Text("Hire Rate:");
                                table.Cell().Text($"{hireRate}%");
                            });

                            // Add status breakdown
                            AddSummaryStatistics(column, applications);

                            // Applications table
                            column.Item().PaddingTop(1, Unit.Centimetre).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(); // Name
                                    columns.RelativeColumn(); // Email
                                    columns.RelativeColumn(); // Job
                                    columns.RelativeColumn(); // Status
                                    columns.RelativeColumn(); // Date
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Applicant");
                                    header.Cell().Text("Email");
                                    header.Cell().Text("Job");
                                    header.Cell().Text("Status");
                                    header.Cell().Text("Applied Date");
                                    header.Cell().ColumnSpan(5).PaddingTop(5).BorderBottom(1).BorderColor(Colors.Black);
                                });

                                foreach (var app in applications.Take(100))
                                {
                                    table.Cell().Text(app.JobSeeker?.FullName ?? "N/A");
                                    table.Cell().Text(app.JobSeeker?.Email ?? "N/A");
                                    table.Cell().Text(app.Job?.Title ?? "N/A");
                                    table.Cell().Text(app.Status.ToString());
                                    table.Cell().Text(app.AppliedDate.ToString("yyyy-MM-dd"));
                                }
                            });
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                        });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"{employer.CompanyName.Replace(" ", "_")}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        }

        // Export to Excel - Company Specific
        public async Task<IActionResult> ExportToExcel(string dateRange, string jobId, string status)
        {
            var currentEmployerId = GetCurrentEmployerId();

            var employer = await db.Employers
                .FirstOrDefaultAsync(e => e.Id == currentEmployerId);

            if (employer == null)
            {
                return NotFound("Employer not found");
            }

            // Get filtered applications FOR CURRENT EMPLOYER ONLY
            var applications = await GetFilteredApplications(dateRange, jobId, status, currentEmployerId);

            // Get employer's jobs for summary statistics
            var jobs = await db.Jobs
                .Where(j => j.EmployerId == employer.Id)
                .ToListAsync();

            // Calculate summary statistics
            int totalJobs = jobs.Count;
            int totalApplications = applications.Count;
            double interviewRate = totalApplications > 0 ?
                Math.Round((applications.Count(a => a.Status == ApplicationStatusEnum.InterviewScheduled) / (double)totalApplications) * 100, 1) : 0;
            double hireRate = totalApplications > 0 ?
                Math.Round((applications.Count(a => a.Status == ApplicationStatusEnum.Hired) / (double)totalApplications) * 100, 1) : 0;

            using (var workbook = new XLWorkbook())
            {
                // Company Info worksheet
                var companySheet = workbook.Worksheets.Add("Company Info");
                companySheet.Cell(1, 1).Value = "Company Report";
                companySheet.Cell(1, 1).Style.Font.Bold = true;
                companySheet.Cell(1, 1).Style.Font.FontSize = 16;

                companySheet.Cell(3, 1).Value = "Company Name";
                companySheet.Cell(3, 2).Value = employer.CompanyName;

                companySheet.Cell(4, 1).Value = "Email";
                companySheet.Cell(4, 2).Value = employer.Email;

                companySheet.Cell(5, 1).Value = "Report Period";
                companySheet.Cell(5, 2).Value = GetDateRangeText(dateRange);

                companySheet.Cell(6, 1).Value = "Generated On";
                companySheet.Cell(6, 2).Value = DateTime.Now;

                // Add summary statistics
                companySheet.Cell(8, 1).Value = "Summary Statistics";
                companySheet.Cell(8, 1).Style.Font.Bold = true;
                companySheet.Cell(8, 1).Style.Font.FontSize = 14;

                companySheet.Cell(9, 1).Value = "Total Jobs Posted";
                companySheet.Cell(9, 2).Value = totalJobs;

                companySheet.Cell(10, 1).Value = "Total Applications";
                companySheet.Cell(10, 2).Value = totalApplications;

                companySheet.Cell(11, 1).Value = "Interview Rate";
                companySheet.Cell(11, 2).Value = interviewRate / 100; // Store as decimal for Excel formatting
                companySheet.Cell(11, 2).Style.NumberFormat.Format = "0.0%";

                companySheet.Cell(12, 1).Value = "Hire Rate";
                companySheet.Cell(12, 2).Value = hireRate / 100; // Store as decimal for Excel formatting
                companySheet.Cell(12, 2).Style.NumberFormat.Format = "0.0%";

                // Applications worksheet
                var worksheet = workbook.Worksheets.Add("Applications");
                worksheet.Cell(1, 1).Value = $"{employer.CompanyName} - Applications";
                worksheet.Cell(1, 1).Style.Font.Bold = true;
                worksheet.Cell(1, 1).Style.Font.FontSize = 16;
                worksheet.Range(1, 1, 1, 5).Merge();

                // Add summary statistics to applications sheet too
                worksheet.Cell(3, 1).Value = "Total Jobs Posted:";
                worksheet.Cell(3, 2).Value = totalJobs;

                worksheet.Cell(4, 1).Value = "Total Applications:";
                worksheet.Cell(4, 2).Value = totalApplications;

                worksheet.Cell(5, 1).Value = "Interview Rate:";
                worksheet.Cell(5, 2).Value = interviewRate / 100;
                worksheet.Cell(5, 2).Style.NumberFormat.Format = "0.0%";

                worksheet.Cell(6, 1).Value = "Hire Rate:";
                worksheet.Cell(6, 2).Value = hireRate / 100;
                worksheet.Cell(6, 2).Style.NumberFormat.Format = "0.0%";

                // Header row (starting at row 8 to leave space for summary)
                worksheet.Cell(8, 1).Value = "Applicant";
                worksheet.Cell(8, 2).Value = "Email";
                worksheet.Cell(8, 3).Value = "Job";
                worksheet.Cell(8, 4).Value = "Status";
                worksheet.Cell(8, 5).Value = "Applied Date";

                // Style header
                var headerRange = worksheet.Range(8, 1, 8, 5);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                // Data rows
                int row = 9;
                foreach (var app in applications)
                {
                    worksheet.Cell(row, 1).Value = app.JobSeeker?.FullName ?? "N/A";
                    worksheet.Cell(row, 2).Value = app.JobSeeker?.Email ?? "N/A";
                    worksheet.Cell(row, 3).Value = app.Job?.Title ?? "N/A";
                    worksheet.Cell(row, 4).Value = app.Status.ToString();
                    worksheet.Cell(row, 5).Value = app.AppliedDate;
                    worksheet.Cell(row, 5).Style.DateFormat.Format = "yyyy-mm-dd";
                    row++;
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();
                companySheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                               $"{employer.CompanyName.Replace(" ", "_")}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
        }

        // Helper method to get filtered applications FOR SPECIFIC EMPLOYER
        private async Task<List<Application>> GetFilteredApplications(string dateRange, string jobId, string status, string employerId = null)
        {
            var currentEmployerId = employerId ?? GetCurrentEmployerId();

            var applicationsQuery = db.Applications
                .Include(a => a.Job)
                .Include(a => a.JobSeeker)
                .Where(a => a.Job.EmployerId == currentEmployerId) // Filter by current employer
                .AsQueryable();

            // ✅ Filter by employer if provided
            if (!string.IsNullOrEmpty(employerId))
            {
                applicationsQuery = applicationsQuery.Where(a => a.Job.EmployerId == employerId);
            }

            // Apply other filters
            if (!string.IsNullOrEmpty(dateRange) && int.TryParse(dateRange, out var days))
            {
                var startDate = DateTime.Now.AddDays(-days);
                applicationsQuery = applicationsQuery.Where(a => a.AppliedDate >= startDate);
            }

            if (!string.IsNullOrEmpty(jobId))
            {
                applicationsQuery = applicationsQuery.Where(a => a.JobId == jobId);
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ApplicationStatusEnum>(status, out var statusEnum))
            {
                applicationsQuery = applicationsQuery.Where(a => a.Status == statusEnum);
            }

            return await applicationsQuery.ToListAsync();
        }

        // Helper method to add summary statistics
        private void AddSummaryStatistics(ColumnDescriptor column, List<Application> applications)
        {
            if (!applications.Any()) return;

            // Calculate status counts
            var statusCounts = applications
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToList();

            column.Item().PaddingTop(0.5f, Unit.Centimetre).Text("Summary by Status:").Bold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                });

                // Table header
                table.Header(header =>
                {
                    header.Cell().Text("Status").SemiBold();
                    header.Cell().Text("Count").SemiBold();
                });

                // Table content
                foreach (var status in statusCounts)
                {
                    table.Cell().Text(status.Status);
                    table.Cell().Text(status.Count.ToString());
                }
            });
        }

        // Helper method to convert dateRange parameter to readable text
        private string GetDateRangeText(string dateRange)
        {
            if (string.IsNullOrEmpty(dateRange)) return "All Time";

            return dateRange switch
            {
                "7" => "Last 7 Days",
                "30" => "Last 30 Days",
                "90" => "Last 90 Days",
                "180" => "Last 180 Days",
                "365" => "Last 365 Days",
                _ => "All Time"
            };
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
    }
}
    
