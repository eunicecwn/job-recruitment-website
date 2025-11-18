using System.Security.Claims;
using JobRecruitment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "JobSeeker")]
    [Route("[controller]")]
    public class NotificationsController : Controller
    {
        private readonly DB _db;
        public NotificationsController(DB db) => _db = db;

        // GET /Notifications/Recent
        [HttpGet("Recent")]
        public async Task<IActionResult> Recent()
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var items = await _db.Applications
                .Include(a => a.Job).ThenInclude(j => j.Employer)
                .Where(a => a.JobSeekerId == uid)
                .OrderByDescending(a => a.AppliedDate)
                .Take(5)
                .Select(a => new
                {
                    applicationId = a.Id,
                    when = a.AppliedDate,
                    status = a.Status.ToString(),
                    title = a.Job != null ? a.Job.Title : "(Job removed)",
                    company = a.Job != null && a.Job.Employer != null ? a.Job.Employer.CompanyName : "",
                    link = Url.Action("DetailsRoute", "Applications", new { id = a.Id })
                })
                .ToListAsync();

            return Json(new { success = true, items });
        }
    }
}
