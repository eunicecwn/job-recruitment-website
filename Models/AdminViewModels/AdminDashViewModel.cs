using System.ComponentModel.DataAnnotations;

namespace Demo.Models.AdminViewModels;

// Main Dashboard ViewModel
public class AdminDashViewModel
{
    public DashboardStatsViewModel Stats { get; set; } = new();
    public ChartDataViewModel ChartData { get; set; } = new();
    public List<TopEmployerViewModel> TopEmployers { get; set; } = new();
    public PerformanceMetricsViewModel PerformanceMetrics { get; set; } = new();
    public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
    public bool HasError { get; set; }
    public string ErrorMessage { get; set; } = "";
}