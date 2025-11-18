using System.Collections.Generic;
using System.Linq;

namespace JobRecruitment.Models
{
    public class EmployerReportsViewModel
    {
        public Employer Employer { get; set; }
        public List<Job> Jobs { get; set; }
        public List<Application> Applications { get; set; }
        public List<TimelineData> TimelineData { get; set; }
        public List<StatusCountData> StatusCounts { get; set; }
        public string TimelineLabelsJson => System.Text.Json.JsonSerializer.Serialize(TimelineData.Select(t => t.Month));
        public string TimelineCountsJson => System.Text.Json.JsonSerializer.Serialize(TimelineData.Select(t => t.Count));
        public string StatusLabelsJson => System.Text.Json.JsonSerializer.Serialize(StatusCounts.Select(s => s.Status));
        public string StatusCountsJson => System.Text.Json.JsonSerializer.Serialize(StatusCounts.Select(s => s.Count));
    }

    public class TimelineData
    {
        public string Month { get; set; }
        public int Count { get; set; }
    }

    public class StatusCountData
    {
        public string Status { get; set; }
        public int Count { get; set; }
    }
}