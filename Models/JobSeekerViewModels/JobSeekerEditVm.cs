using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class JobSeekerEditVm
    {
        // Basics
        public string Id { get; set; } = "";

        [MaxLength(50)]
        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? ExperienceLevel { get; set; }

        // Files / existing urls
        public string? ExistingPhoto { get; set; }
        public string? ExistingResume { get; set; }

        // Uploads (used by Basic Info modal)
        public IFormFile? ProfilePhoto { get; set; }
        public IFormFile? Resume { get; set; }

        // Summary
        [MaxLength(1500)]
        public string? Summary { get; set; }

        // Work Experience
        public List<ExperienceRow> Experiences { get; set; } = new();
        public class ExperienceRow
        {
            public string Id { get; set; } = "";
            public string Role { get; set; } = "";
            public string? Company { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string? Description { get; set; }
        }

        // Education
        public List<EducationRow> Educations { get; set; } = new();
        public class EducationRow
        {
            public string Id { get; set; } = "";
            public string School { get; set; } = "";
            public string? Degree { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string? Description { get; set; }
        }

        // Skills
        public List<SkillRow> Skills { get; set; } = new();
        public class SkillRow
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
        }

        // Languages
        public List<LanguageRow> Languages { get; set; } = new();
        public class LanguageRow
        {
            public string Id { get; set; } = "";
            public string Language { get; set; } = "";
            public string? Proficiency { get; set; }
        }

        // Licences & Certifications
        public List<LicenseRow> Licenses { get; set; } = new();
        public class LicenseRow
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string? Issuer { get; set; }
            public DateTime? IssuedDate { get; set; }
            public DateTime? ExpiresDate { get; set; }
            public string? CredentialUrl { get; set; }
        }

        // Preferences
        public PreferenceRow? Preferences { get; set; }
        public class PreferenceRow
        {
            public string? Availability { get; set; }
            public string? PreferredWorkTypes { get; set; }
            public string? PreferredLocations { get; set; }
            public decimal? SalaryExpectation { get; set; }
        }
    }
}
