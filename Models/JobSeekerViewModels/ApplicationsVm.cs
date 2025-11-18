using System.ComponentModel.DataAnnotations;
using JobRecruitment.Models;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class ApplicationsVm
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int Total { get; set; }

        public int TotalPages => (int)Math.Ceiling((double)Total / Math.Max(PageSize, 1));

        public string? Term { get; set; }
        public ApplicationStatusEnum? Status { get; set; }

        public List<ApplicationListItemVm> Items { get; set; } = new();
    }

    public class ApplicationListItemVm
    {
        [Required] public string Id { get; set; } = default!;
        [Required] public string JobId { get; set; } = default!;

        public string JobTitle { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public DateTime AppliedLocal { get; set; }

        public string StatusText { get; set; } = "";
        public ApplicationStatusEnum StatusEnum { get; set; }
        public string BadgeClass { get; set; } = "bg-secondary";
    }

    public class ApplicationReceiptVm
    {
        public string Id { get; set; } = default!;
        public string JobTitle { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public DateTime AppliedLocal { get; set; }
        public string StatusText { get; set; } = "";

        public string? ResumeFileName { get; set; }
        public List<string>? JobQuestions { get; set; }
    }
}
