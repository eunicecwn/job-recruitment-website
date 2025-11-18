namespace JobRecruitment.Models.ViewModels;

public class JobReportViewModel
{
    public string Id { get; set; }
    public string JobId { get; set; }
    public string JobTitle { get; set; }
    public string EmployerName { get; set; }
    public string Reason { get; set; }
    public DateTime DateReported { get; set; }
    public bool IsActive { get; set; }
}