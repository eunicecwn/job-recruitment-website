using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace JobRecruitment.Models;

public class AdminDashboardViewModel
{
    public DashboardStatsViewModel Stats { get; set; } = new();
    public ChartDataViewModel ChartData { get; set; } = new();
    public List<TopEmployerViewModel> TopEmployers { get; set; } = new();
    public PerformanceMetricsViewModel PerformanceMetrics { get; set; } = new();
    public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
    public bool HasError { get; set; }
    public string ErrorMessage { get; set; }
}

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
    public int PendingJobs { get; set; }
    public int DraftJobs { get; set; }
}

public class TopEmployerViewModel
{
    public string CompanyName { get; set; }
    public int JobsPosted { get; set; }
    public int TotalApplications { get; set; }
    public int SuccessfulHires { get; set; }

    public double SuccessRate => TotalApplications > 0 ?
        (SuccessfulHires * 100.0 / TotalApplications) : 0;
}

public class PerformanceMetricsViewModel
{
    public int UserEngagementRate { get; set; }
    public int JobPostingSuccessRate { get; set; }
    public int ApplicationCompletionRate { get; set; }
    public int PlatformSatisfaction { get; set; }
    public string AvgLoadTime { get; set; } = "0s";
    public string Uptime { get; set; } = "100";
    public string DailyActiveUsers { get; set; } = "0";
}

public class RecentActivityViewModel
{
    public string ActivityType { get; set; }
    public string ActivityTypeDisplay { get; set; }
    public string UserOrCompany { get; set; }
    public string Description { get; set; }
    public string TimeAgo { get; set; }
    public string Status { get; set; }
    public string StatusDisplay { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class AdminProfileViewModel
{
    public string Id { get; set; }

    [Required(ErrorMessage = "Username is required")]
    [StringLength(50, ErrorMessage = "Username cannot exceed 50 characters")]
    public string Username { get; set; }

    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
    public string FullName { get; set; }

    [Required(ErrorMessage = "Gender is required")]
    public string Gender { get; set; }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; }

    [Phone(ErrorMessage = "Invalid phone format")]
    [StringLength(20, ErrorMessage = "Phone cannot exceed 20 characters")]
    public string Phone { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime? DateOfBirth { get; set; }

    public string ProfilePhotoFileName { get; set; }
}

public class UserFilterViewModel
{
    public string Role { get; set; } = "All";
    public string Search { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}


public class DashboardFilterRequest
{
    public int DateRange { get; set; } = 7; // days
    public string UserType { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }
}

public class FilteredDashboardResponse
{
    public DashboardStatsViewModel Stats { get; set; }
    public ChartDataViewModel ChartData { get; set; }
    public List<TopEmployerViewModel> TopEmployers { get; set; }
    public List<RecentActivityViewModel> RecentActivities { get; set; }
    public string FilterSummary { get; set; }
}

public class TrendsData
{
    public List<int> JobSeekerTrends { get; set; } = new();
    public List<int> EmployerTrends { get; set; } = new();
    public List<int> ApplicationTrends { get; set; } = new();
}

public class DashboardStats
{
    public int TotalUsers { get; set; }
    public int TotalEmployers { get; set; }
    public int TotalJobSeekers { get; set; }
    public int TotalAdmins { get; set; }
    public int TotalJobs { get; set; }
    public int TotalApplications { get; set; }
    public int PendingEmployers { get; set; }
}

public class ChartData
{
    public List<string> MonthLabels { get; set; } = new();
    public List<int> JobSeekerTrends { get; set; } = new();
    public List<int> EmployerTrends { get; set; } = new();
    public List<int> ApplicationTrends { get; set; } = new();
    public JobStats JobStats { get; set; } = new();
}

public class JobStats
{
    public int ActiveJobs { get; set; }
    public int ClosedJobs { get; set; }
    public int PendingJobs { get; set; }
    public int DraftJobs { get; set; }
}

public class TopEmployer
{
    public string CompanyName { get; set; }
    public int JobsPosted { get; set; }
    public int TotalApplications { get; set; }
    public int SuccessfulHires { get; set; }
}

public class PerformanceMetrics
{
    public int UserEngagementRate { get; set; }
    public int JobPostingSuccessRate { get; set; }
    public int ApplicationCompletionRate { get; set; }
    public int PlatformSatisfaction { get; set; }
    public string AvgLoadTime { get; set; } = "0s";
    public string Uptime { get; set; } = "100";
    public string DailyActiveUsers { get; set; } = "0";
}

public class RecentActivity
{
    public string ActivityType { get; set; }
    public string ActivityTypeDisplay { get; set; }
    public string UserOrCompany { get; set; }
    public string Description { get; set; }
    public string TimeAgo { get; set; }
    public string Status { get; set; }
    public string StatusDisplay { get; set; }
    public DateTime CreatedDate { get; set; }
}