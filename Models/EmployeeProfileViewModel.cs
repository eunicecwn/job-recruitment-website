using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.ViewModels
{
    public class EmployeeProfileViewModel
    {
        public string Id { get; set; }

        [Required, MaxLength(50)]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Email Address")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string Email { get; set; }

        [MaxLength(20)]
        [Display(Name = "Phone Number")]
        [Phone(ErrorMessage = "Please enter a valid phone number")]
        public string Phone { get; set; }

        [MaxLength(10)]
        [Display(Name = "Gender")]
        public string Gender { get; set; }

        [Display(Name = "Date of Birth")]
        public DateTime? DateOfBirth { get; set; }

        [MaxLength(100)]
        [Display(Name = "Job Title")]
        public string JobTitle { get; set; }

        [MaxLength(100)]
        [Display(Name = "Department")]
        public string Department { get; set; }

        [Display(Name = "Hire Date")]
        public DateTime? HireDate { get; set; }

        [Display(Name = "Salary")]
        [Range(0, double.MaxValue, ErrorMessage = "Salary must be a positive number")]
        public decimal? Salary { get; set; }

        [MaxLength(50)]
        [Display(Name = "Employment Status")]
        public string EmploymentStatus { get; set; } // Active, Inactive, On Leave, etc.

        [MaxLength(100)]
        [Display(Name = "Manager")]
        public string Manager { get; set; }

        [MaxLength(200)]
        [Display(Name = "Office Location")]
        public string OfficeLocation { get; set; }

        [MaxLength(500)]
        [Display(Name = "Bio/Notes")]
        public string Bio { get; set; }

        [MaxLength(100)]
        [Display(Name = "Emergency Contact Name")]
        public string EmergencyContactName { get; set; }

        [MaxLength(20)]
        [Display(Name = "Emergency Contact Phone")]
        [Phone(ErrorMessage = "Please enter a valid phone number")]
        public string EmergencyContactPhone { get; set; }

        [MaxLength(50)]
        [Display(Name = "Emergency Contact Relationship")]
        public string EmergencyContactRelationship { get; set; }

        public string? ProfilePhotoFileName { get; set; }

        [Display(Name = "Profile Photo")]
        public IFormFile? ProfilePhoto { get; set; } // For upload

        // Additional properties for display purposes
        public int? Age => DateOfBirth.HasValue ? DateTime.Today.Year - DateOfBirth.Value.Year : null;
        public int? YearsOfService => HireDate.HasValue ? DateTime.Today.Year - HireDate.Value.Year : null;
    }
}