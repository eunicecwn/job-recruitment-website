using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using JobRecruitment.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding; // BindNever

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class ApplicationsApplyVm
    {
        [Required]
        public string JobId { get; set; } = string.Empty;

        // Display-only in the popup; never posted back
        [BindNever]
        public Job? Job { get; set; }

        [Display(Name = "Cover letter")]
        [StringLength(5000)]
        public string? CoverLetter { get; set; }

        public bool HasQuestions { get; set; }
        public List<QuestionVm> Questions { get; set; } = new();

        public class QuestionVm
        {
            [Required] public string QuestionId { get; set; } = string.Empty;
            [Required] public string QuestionText { get; set; } = string.Empty;
            [Required] public QuestionType Type { get; set; }

            [BindNever] public bool IsRequired { get; set; }     // <-- ignore posts
            public int? MaxLength { get; set; }
            public string? OptionsCsv { get; set; }

            public string? Answer { get; set; }                   // text/date/dropdown/checkbox
            public IFormFile? Upload { get; set; }                // file questions
        }
    }
}
