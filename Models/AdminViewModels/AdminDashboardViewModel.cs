using System.ComponentModel.DataAnnotations;

namespace Demo.Models.AdminViewModels;

// Main Dashboard ViewModel
public class AdminDashboardViewModel
{
    public DashboardStatsViewModel Stats { get; set; } = new();
    public ChartDataViewModel ChartData { get; set; } = new();
    public List<TopEmployerViewModel> TopEmployers { get; set; } = new();
    public PerformanceMetricsViewModel PerformanceMetrics { get; set; } = new();
    public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
    public bool HasError { get; set; }
    public string ErrorMessage { get; set; } = "";
}

// Dashboard Statistics
public class DashboardStatsViewModel
{
    public int TotalUsers { get; set; }
    public int TotalEmployers { get; set; }
    public int TotalJobSeekers { get; set; }
    public int TotalAdmins { get; set; }
    public int TotalJobs { get; set; }
    public int TotalApplications { get; set; }
    public int PendingEmployers { get; set; }
}

// Chart Data
public class ChartDataViewModel
{
    public List<string> MonthLabels { get; set; } = new();
    public List<int> JobSeekerTrends { get; set; } = new();
    public List<int> EmployerTrends { get; set; } = new();
    public List<int> ApplicationTrends { get; set; } = new();
    public JobStatsViewModel JobStats { get; set; } = new();
}

public class JobStatsViewModel
{
    public int ActiveJobs { get; set; }
    public int ClosedJobs { get; set; }
    public int DraftJobs { get; set; }
    public int PendingJobs { get; set; }
}

// Top Employers
public class TopEmployerViewModel
{
    public string CompanyName { get; set; } = "";
    public int JobsPosted { get; set; }
    public int TotalApplications { get; set; }
    public int SuccessfulHires { get; set; }
}

// Performance Metrics
public class PerformanceMetricsViewModel
{
    public int UserEngagementRate { get; set; }
    public int JobPostingSuccessRate { get; set; }
    public int ApplicationCompletionRate { get; set; }
    public string DailyActiveUsers { get; set; } = "0";
    public int PlatformSatisfaction { get; set; }
    public string AvgLoadTime { get; set; } = "0.0s";
    public string Uptime { get; set; } = "0.0";
}

// Recent Activities
public class RecentActivityViewModel
{
    public string ActivityType { get; set; } = "";
    public string ActivityTypeDisplay { get; set; } = "";
    public string UserOrCompany { get; set; } = "";
    public string Description { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}

// Filter Request Models
public class TopUsersFilterRequest
{
    [Range(1, 50)]
    public int Count { get; set; } = 10;
    public string UserType { get; set; } = "all";
    public string SortBy { get; set; } = "applications";
    public string Order { get; set; } = "desc";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
    [Range(1, 365)]
    public int DateRange { get; set; } = 30;
}

public class ActivityFilterRequest
{
    [Range(1, 100)]
    public int Count { get; set; } = 10;
    public string ActivityType { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
    [Range(1, 365)]
    public int DateRange { get; set; } = 30;
}

public class EmployerFilterRequest
{
    [Range(1, 100)]
    public int Count { get; set; } = 10;
    public string SortBy { get; set; } = "success_rate";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
    [Range(1, 365)]
    public int DateRange { get; set; } = 30;
}

public class JobSeekerFilterRequest
{
    [Range(1, 100)]
    public int Count { get; set; } = 10;
    public string SortBy { get; set; } = "most_active";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
    [Range(1, 365)]
    public int DateRange { get; set; } = 30;
}

public class DashboardFilterRequest
{
    public object DateRange { get; set; } = "all";
    public string UserType { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
}

public class FilteredDashboardResponse
{
    public DashboardStatsViewModel Stats { get; set; } = new();
    public ChartDataViewModel ChartData { get; set; } = new();
    public List<TopEmployerViewModel> TopEmployers { get; set; } = new();
    public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
    public string FilterSummary { get; set; } = "";
}

// Helper Data Classes
public class TrendsDataResult
{
    public List<int> JobSeekerTrends { get; set; } = new();
    public List<int> EmployerTrends { get; set; } = new();
    public List<int> ApplicationTrends { get; set; } = new();
}

public class UserActivityData
{
    public string UserDisplayName { get; set; } = "";
    public int ActivityScore { get; set; }
    public string UserType { get; set; } = "";
}

public class EmployerRankingData
{
    public string CompanyName { get; set; } = "";
    public int JobsPosted { get; set; }
    public int TotalApplications { get; set; }
    public decimal SuccessRate { get; set; }
    public string AverageResponseTime { get; set; } = "";
}

public class JobSeekerRankingData
{
    public string FullName { get; set; } = "";
    public int TotalApplications { get; set; }
    public decimal InterviewRate { get; set; }
    public DateTime LastActivity { get; set; }
    public int ProfileCompletionPercentage { get; set; }
}