using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobRecruitment.Models.ViewModels;
    public class JobReportExportViewModel
    {
        public int Id { get; set; }
        public string JobTitle { get; set; }
        public string EmployerName { get; set; }
        public string Reason { get; set; }
        public DateTime DateReported { get; set; }
        public bool IsActive { get; set; }
    }