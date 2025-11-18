using JobRecruitment.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.Models.ViewModels;

public class AdminLoginViewModel
{
    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    public bool RememberMe { get; set; } = false;
}
