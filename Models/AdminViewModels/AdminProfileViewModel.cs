using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.ViewModels;

public class AdminProfileViewModel
{
    public string Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; }

    [MaxLength(10)]
    public string Gender { get; set; }

    [Required, MaxLength(100)]
    public string Email { get; set; }

    [MaxLength(20)]
    public string Phone { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public string? ProfilePhotoFileName { get; set; }

    public IFormFile? ProfilePhoto { get; set; } // For upload
}