using System.ComponentModel.DataAnnotations;
using JobRecruitment.Models;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class ApplicationDetailsVm
    {
        [Required] public string Id { get; set; } = default!;             // Application Id
        [Required] public string JobId { get; set; } = default!;
        public Application Application { get; set; } = default!;
        public Job Job { get; set; } = default!;
        public string? CoverLetter { get; set; }
        public string? ResumeFileName { get; set; }

        // Question set (if any)
        public bool HasQuestionSet { get; set; }
        public string? QuestionSetName { get; set; }
        public List<QuestionAnswerVm> Questions { get; set; } = new();

        public class QuestionAnswerVm
        {
            [Required] public string QuestionId { get; set; } = default!;
            [Required] public string QuestionText { get; set; } = default!;
            [Required] public QuestionType Type { get; set; }
            public bool IsRequired { get; set; }
            public string? OptionsCsv { get; set; }
            public int? MaxLength { get; set; }
            // The candidate's answer (string; for Checkbox we persist comma-separated)
            public string? Answer { get; set; }
        }
    }
}
