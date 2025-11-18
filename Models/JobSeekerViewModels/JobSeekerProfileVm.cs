using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class JobSeekerProfileVm
    {
        [Required, MaxLength(10)]
        public string Id { get; set; } = "";

        [Required, MaxLength(50)]
        public string FullName { get; set; } = "";

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = "";

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        [MaxLength(50)]
        public string? ExperienceLevel { get; set; }

        public string? ProfilePhotoFileName { get; set; }
        public string? ResumeFileName { get; set; }

        [Range(0, 100)]
        public int ProfileCompleteness { get; set; }
    }
}
