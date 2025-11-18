using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace JobRecruitment.Models
{
    public class Application_View_Model
    {
        public string ApplicationId { get; set; }
        public string JobId { get; set; }
        public string JobSeekerId { get; set; }

        // Application fields
        public DateTime AppliedDate { get; set; }
        public ApplicationStatusEnum Status { get; set; }
        public DateTime? InterviewDate { get; set; }
        public string? InterviewLocation { get; set; }
        public string? InterviewNotes { get; set; }
        public string? InterviewerInfo { get; set; }

    }

    public class ScheduleInterviewViewModel
    {
        public string ApplicationId { get; set; }

        [Required]
        public DateTime InterviewStartDate { get; set; }

        [Required]
        public TimeSpan StartTime { get; set; }
        [Required]
        public TimeSpan EndTime { get; set; }

        public string InterviewLocation { get; set; }
        public string InterviewNotes { get; set; }
        public string InterviewerInfo { get; set; }
    }

    public class EmailVM
    {
        [StringLength(100)]
        [EmailAddress]
        public string Email { get; set; }

        public string Subject { get; set; }

        public string Body { get; set; }

        public bool IsBodyHtml { get; set; }
    }

    public class InterviewCalendarViewModel
    {
        public Dictionary<DateOnly, List<Application>> CalendarData { get; set; }
        public bool IsEdit { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public string ViewType { get; set; }
        public string SelectedDate { get; set; }
        public int? ApplicationId { get; set; }
        public SelectList MonthList { get; set; }
        public SelectList YearList { get; set; }
        public List<DateOnly> DateRange { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int MinYear { get; set; }
        public int MaxYear { get; set; }
    }

    public class InterviewCalendarPageViewModel
    {
        public Dictionary<DateOnly, List<Application>>? Calendar { get; set; }
        public ScheduleInterviewViewModel Form { get; set; }
        public Application? Application { get; set; }
    }

    public class InterviewCalendarEvent
    {
        public string Id { get; set; }
        public DateTime InterviewStartDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Title { get; set; }
        public string JobSeekerName { get; set; }
        public string JobTitle { get; set; }
    }

    public class InterviewEmailModel
    {
        public string JobTitle { get; set; }
        public DateTime InterviewDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string InterviewLocation { get; set; }
        public string InterviewerInfo { get; set; }
        public string InterviewNotes { get; set; }
        public string CompanyName { get; set; }
    }

    public class InterviewCancellationModel
    {
        public string CandidateName { get; set; }
        public string JobTitle { get; set; }
        public string CompanyName { get; set; }
        public DateTime? OriginalDate { get; set; }
        public string OriginalLocation { get; set; }
        public string CancellationReason { get; set; }
        public string ContactEmail { get; set; }
    }

    public class CancelInterviewRequest
    {
        public string ApplicationId { get; set; }
        public string CancellationReason { get; set; }
    }
}

public class ApplicationDetailsViewModel
{
    public Application Application { get; set; }
    public double CompletenessRate { get; set; }
}