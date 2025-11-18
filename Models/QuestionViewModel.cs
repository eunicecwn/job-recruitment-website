using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobRecruitment.Models
{
    public class QuestionSetViewModel
    {
        [Required(ErrorMessage = "Please provide a name for your question set")]
        [StringLength(100, ErrorMessage = "Question set name cannot exceed 100 characters")]
        public string Name { get; set; }

        public string? Description { get; set; }

        public string? EmployerId { get; set; }

        public string? Id { get; set; }

        public bool IsActive { get; set; }

        public List<QuestionViewModel> Questions { get; set; } = new List<QuestionViewModel>();
    }

    public class QuestionViewModel
    {
        public string? Id { get; set; }
        public string? EmployerId { get; set; }
        public string? QuestionSetId { get; set; }

        [Required(ErrorMessage = "Question text is required")]
        [StringLength(500, ErrorMessage = "Question text cannot exceed 500 characters")]
        public string Text { get; set; }

        [Required(ErrorMessage = "Please select a question type")]
        public QuestionType Type { get; set; }

        public bool IsRequired { get; set; }

        public string? Options { get; set; }

        [Range(1, 5000, ErrorMessage = "Max length must be between 1 and 5000 characters")]
        public int? MaxLength { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Order must be a positive number")]
        public int Order { get; set; }

        public string? TempId { get; set; }

        // Helper property for client-side operations
        public bool IsDeleted { get; set; }
    }

    public class AssignQuestionSetViewModel
    {
        public string QuestionSetId { get; set; }
        public string QuestionSetName { get; set; }
        public bool QuestionSetIsActive { get; set; } // Add this property
        public List<JobInfoViewModel> AvailableJobs { get; set; }
        public string? JobsToUnassign { get; set; } = string.Empty;

    }


    public class JobInfoViewModel
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool HasQuestionSet { get; set; }
        public string? CurrentQuestionSetId { get; set; }
        public bool Selected { get; set; } // This property is required
    }

    public class ToggleStatusRequest
    {
        public string Id { get; set; }
        public bool IsActive { get; set; }
    }
}
