namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class SavedJobItemVm
    {
        public string JobId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string Location { get; set; } = string.Empty;
        public decimal? MinSalary { get; set; }
        public decimal? MaxSalary { get; set; }
        public string? CategoryName { get; set; }
        public DateTime SavedUtc { get; set; }
    }
}
