namespace JobRecruitment.Models.ViewModels
{
    public class UnifiedReportViewModel
    {
        public string ReportId { get; set; }
        public string ReportType { get; set; } // "User" or "Employer"
        public string Reason { get; set; }
        public DateTime DateReported { get; set; }

        public string ReporterName { get; set; }
        public string ReportedEntityName { get; set; }
        public string ReportedEntityId { get; set; }

        public string ReportedRole { get; set; } // JobSeeker / Admin / Employer
        public bool IsBlocked { get; set; }
    }
}