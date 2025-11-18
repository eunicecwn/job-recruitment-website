using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.Models.JobSeekerViewModels
{
    public class JobSearchVm
    {
        // Filters
        public string? Q { get; set; }
        public string? CategoryId { get; set; }
        public string? Location { get; set; }
        public int? JobType { get; set; }

        [Range(0, 1_000_000)]
        public decimal? MinSalary { get; set; }

        [Range(0, 1_000_000)]
        public decimal? MaxSalary { get; set; }

        public string? SortBy { get; set; } = "recent";

        // Paging (PageSize fixed in controller)
        public int Page { get; set; } = 1;
        public int Total { get; set; }
        public int TotalPages { get; set; }            // set by controller
        public int CurrentPageSize { get; set; } = 12; // set by controller

        // Dropdowns
        public List<(string Id, string Name)> Categories { get; set; } = new();
        public List<(int Id, string Name)> JobTypes { get; set; } = new();

        // Results
        public List<JobSearchResultItemVm> Results { get; set; } = new();
    }

    public class JobSearchResultItemVm
    {
        public string JobId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal? MinSalary { get; set; }
        public decimal? MaxSalary { get; set; }
        public string? CategoryName { get; set; }
        public int JobType { get; set; }
        public DateTime PostedDateUtc { get; set; }
    }
}
