using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class ReportJobVm
    {
        [Required, MaxLength(10)]
        public string JobId { get; set; } = string.Empty;

        public string? JobTitle { get; set; }   // convenience (unused in the modal)

        [Required, EmailAddress, MaxLength(256)]
        public string ReporterEmail { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Reason { get; set; } = string.Empty;

        // Bumped to 1500 as requested
        [MaxLength(1500)]
        public string? Details { get; set; }
    }
}
