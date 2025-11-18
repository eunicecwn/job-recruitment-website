// Create this in your Models/AdminViewModels folder or appropriate location

using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.ViewModels;

public class LockedUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [Display(Name = "Failed Attempts")]
    public int FailedLoginAttempts { get; set; }

    [Display(Name = "Account Status")]
    public bool IsActive { get; set; }

    [Display(Name = "Created Date")]
    public DateTime CreatedDate { get; set; }

    [Display(Name = "Status")]
    public string DisplayStatus =>
        FailedLoginAttempts >= 5 ? "🔒 Locked" : "✅ Active";

    [Display(Name = "Account Type")]
    public string DisplayAccountType =>
        Role == "Employer" && !string.IsNullOrEmpty(CompanyName)
            ? $"Employer ({CompanyName})"
            : Role;
}