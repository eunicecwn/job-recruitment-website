using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

public class AdminDashboardService
{
    private readonly DB _context;

    public AdminDashboardService(DB context)
    {
        _context = context;
    }

    public async Task<List<EmployerRankingData>> GetEmployerRankingsOptimizedAsync(
        EmployerFilterRequest request,
        (DateTime StartDate, DateTime EndDate) dateRange)
    {
        // Get employers with all their jobs and applications (no filtering in Include)
        var employers = await _context.Employers
            .Where(e => e.IsActive && e.CreatedDate >= dateRange.StartDate && e.CreatedDate <= dateRange.EndDate)
            .Include(e => e.Jobs)
            .ThenInclude(j => j.Applications)
            .ToListAsync();

        // Process in memory with proper filtering
        var result = employers
            .Select(e => {
                // Filter jobs and applications in memory
                var relevantJobs = e.Jobs
                    .Where(j => j.PostedDate >= dateRange.StartDate && j.PostedDate <= dateRange.EndDate)
                    .ToList();

                var relevantApplications = relevantJobs
                    .SelectMany(j => j.Applications
                        .Where(a => a.AppliedDate >= dateRange.StartDate && a.AppliedDate <= dateRange.EndDate))
                    .ToList();

                var hiredCount = relevantApplications.Count(a => a.Status == ApplicationStatusEnum.Hired);
                var successRate = relevantApplications.Count > 0 ?
                    (decimal)hiredCount / relevantApplications.Count * 100 : 0;

                return new EmployerRankingData
                {
                    CompanyName = e.CompanyName ?? "Unknown Company",
                    JobsPosted = relevantJobs.Count,
                    TotalApplications = relevantApplications.Count,
                    SuccessRate = Math.Round(successRate, 1),
                    AverageResponseTime = CalculateAverageResponseTime(relevantApplications)
                };
            })
            .Where(e => e.JobsPosted > 0 || e.TotalApplications > 0)
            .ToList();

        // Apply sorting
        result = request.SortBy switch
        {
            "success_rate" => result.OrderByDescending(e => e.SuccessRate).ToList(),
            "total_applications" => result.OrderByDescending(e => e.TotalApplications).ToList(),
            "jobs_posted" => result.OrderByDescending(e => e.JobsPosted).ToList(),
            _ => result.OrderByDescending(e => e.SuccessRate).ToList()
        };

        // Take requested count
        var count = request.Count == 999 ? result.Count : Math.Min(request.Count, result.Count);
        return result.Take(count).ToList();
    }

    private string CalculateAverageResponseTime(List<Application> applications)
    {
        if (!applications.Any()) return "N/A";

        var respondedApplications = applications
            .Where(a => a.Status != ApplicationStatusEnum.Pending)
            .ToList();

        if (!respondedApplications.Any()) return "N/A";

        var avgDays = respondedApplications
            .Select(a => (DateTime.Now - a.AppliedDate).Days)
            .Average();

        if (avgDays < 1)
            return "< 1 day";
        else if (avgDays < 7)
            return $"{avgDays:F1} days";
        else
            return $"{(avgDays / 7):F1} weeks";
    }
}

public class EmployerRankingData
{
    public string CompanyName { get; set; } = "";
    public int JobsPosted { get; set; }
    public int TotalApplications { get; set; }
    public decimal SuccessRate { get; set; }
    public string AverageResponseTime { get; set; } = "";
}

public class EmployerFilterRequest
{
    [Range(1, 999)]
    public int Count { get; set; } = 10;

    public string SortBy { get; set; } = "success_rate";

    [Range(1, 365)]
    public int DateRange { get; set; } = 30;

    public string UserType { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string Order { get; set; } = "desc";
}