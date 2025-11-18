using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace JobRecruitment.Models;

public class JobViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Job title is required.")]
    [MaxLength(120, ErrorMessage = "Job title cannot exceed 120 characters.")]
    [Display(Name = "Job Title")]
    public string Title { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    [Display(Name = "Job Description")]
    public string Description { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    [Display(Name = "Location")]
    public string Location { get; set; }

    [Display(Name = "Latitude")]
    public double Latitude { get; set; }

    [Display(Name = "Longitude")]
    public double Longitude { get; set; }

    [Display(Name = "Minimum Salary")]
    [Range(0, double.MaxValue, ErrorMessage = "Minimum salary must be positive.")]
    public decimal MinSalary { get; set; }

    [Display(Name = "Maximum Salary")]
    [Range(0, double.MaxValue, ErrorMessage = "Maximum salary must be positive.")]
    public decimal MaxSalary { get; set; }

    [Required(ErrorMessage = "Job type is required.")]
    [Display(Name = "Job Type")]
    public JobType JobType { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public JobStatus Status { get; set; }

    [Display(Name = "Posted Date")]
    [DataType(DataType.Date)]
    public DateTime PostedDate { get; set; } = DateTime.Today;

    [Display(Name = "Closing Date")]
    [DataType(DataType.Date)]
    [DateAfter(nameof(PostedDate), ErrorMessage = "Closing date must be after posted date.")]
    public DateTime? ClosingDate { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    [Display(Name = "Category")]
    public string CategoryId { get; set; }
    public string EmployerId { get; set; }

    public bool IsActive { get; set; }

    // Dropdown sources
    public List<SelectListItem> JobTypes { get; set; } = new();
    public List<SelectListItem> StatusOptions { get; set; } = new();
    public List<SelectListItem> Categories { get; set; } = new();

    public void InitializeDropdowns()
    {
        JobTypes = GetEnumSelectList<JobType>();
        StatusOptions = GetEnumSelectList<JobStatus>();
    }

    private List<SelectListItem> GetEnumSelectList<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T))
            .Cast<T>()
            .Select(e => new SelectListItem
            {
                Value = e.ToString(),
                Text = GetEnumDisplayName(e)
            }).ToList();
    }

    private string GetEnumDisplayName(Enum value)
    {
        return value.GetType()
            .GetMember(value.ToString())
            .First()
            .GetCustomAttribute<DisplayAttribute>()
            ?.GetName() ?? value.ToString();
    }
}

public class DateAfterAttribute : ValidationAttribute
{
    private readonly string _comparisonProperty;

    public DateAfterAttribute(string comparisonProperty)
    {
        _comparisonProperty = comparisonProperty;
    }
    protected override ValidationResult IsValid(object value, ValidationContext context)
    {
        var currentValue = (DateTime?)value;
        if (currentValue == null) return ValidationResult.Success; // Allow null

        var property = context.ObjectType.GetProperty(_comparisonProperty);
        var comparisonValue = (DateTime)property.GetValue(context.ObjectInstance);

        if (currentValue <= comparisonValue)
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}

// Add to your View Model.cs file
public class MultiQuestionViewModel
{
    public List<string> SelectedJobIds { get; set; } = new List<string>();
    public List<SelectListItem> Jobs { get; set; } = new List<SelectListItem>();
    public List<QuestionItem> Questions { get; set; } = new List<QuestionItem>();
}

public class QuestionItem
{
    public string Text { get; set; }
}