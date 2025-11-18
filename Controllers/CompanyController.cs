/*using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using JobRecruitment.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;
using System.Security.Claims;

namespace JobRecruitment.Controllers
{
    public class CompanyController : Controller
    {
        private readonly DB _context;
        private readonly IWebHostEnvironment _environment;

        public CompanyController(DB context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: Company/CompanyProfile (VIEW ONLY FOR USERS)
        public async Task<IActionResult> CompanyProfile(string id = null)
        {
            string employerId;
            bool isEditMode = false;

            if (string.IsNullOrEmpty(id))
            {
                // Edit mode - logged in employer
                employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                isEditMode = true;

                if (string.IsNullOrEmpty(employerId))
                {
                    return RedirectToAction("Login", "Account");
                }
            }
            else
            {
                // View mode - specific company
                employerId = id;
                isEditMode = false;
            }

            var employer = await _context.Employers
                .Include(e => e.Jobs)
                .Include(e => e.Reviews)
                    .ThenInclude(r => r.JobSeeker)
                .Include(e => e.CompanyPhotos)
                .Include(e => e.SocialMediaLinks) // ← KEEP THIS
                .Include(e => e.CompanyFeatures)  // ← KEEP THIS
                .FirstOrDefaultAsync(e => e.Id == employerId);

            if (employer == null)
            {
                return NotFound();
            }

            ViewBag.IsEditMode = isEditMode;
            return View(employer);
        }

        // POST: Company/UpdateSocialMedia (USING EXISTING TABLE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSocialMedia([FromBody] List<SocialMediaLink> socialMediaLinks)
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, error = "Not authenticated" });
            }

            try
            {
                // Remove existing social media links
                var existingLinks = await _context.SocialMediaLinks
                    .Where(s => s.EmployerId == employerId)
                    .ToListAsync();

                _context.SocialMediaLinks.RemoveRange(existingLinks);

                // Add new social media links
                foreach (var link in socialMediaLinks)
                {
                    if (!string.IsNullOrEmpty(link.Url))
                    {
                        link.Id = GenerateId("SML");
                        link.EmployerId = employerId;
                        link.CreatedDate = DateTime.UtcNow;
                        _context.SocialMediaLinks.Add(link);
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Company/UpdateCompanyFeatures (USING EXISTING TABLE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCompanyFeatures([FromBody] List<CompanyFeature> features)
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, error = "Not authenticated" });
            }

            try
            {
                // Remove existing features
                var existingFeatures = await _context.CompanyFeatures
                    .Where(cf => cf.EmployerId == employerId)
                    .ToListAsync();

                _context.CompanyFeatures.RemoveRange(existingFeatures);

                // Add new features
                foreach (var feature in features)
                {
                    if (!string.IsNullOrEmpty(feature.Title))
                    {
                        feature.Id = GenerateId("CF");
                        feature.EmployerId = employerId;
                        feature.CreatedDate = DateTime.UtcNow;
                        feature.IsActive = true;
                        _context.CompanyFeatures.Add(feature);
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Company/UploadPhoto
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(IFormFile file, string photoType, string caption)
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, error = "Not authenticated" });
            }

            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, error = "No file uploaded" });
            }

            // Validate file size (5MB max)
            if (file.Length > 5 * 1024 * 1024)
            {
                return Json(new { success = false, error = "File size must be less than 5MB" });
            }

            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                return Json(new { success = false, error = "Invalid file type. Allowed: JPG, PNG, GIF, WebP" });
            }

            try
            {
                // Create uploads directory
                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "companyphotos");
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                // Generate unique filename
                var fileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Get current max sort order
                var maxOrder = await _context.CompanyPhotos
                    .Where(cp => cp.EmployerId == employerId)
                    .MaxAsync(cp => (int?)cp.SortOrder) ?? -1;

                // Create photo entity
                var companyPhoto = new CompanyPhoto
                {
                    Id = GenerateId("CP"),
                    EmployerId = employerId,
                    FileName = fileName,
                    FileType = fileExtension.Replace(".", "").ToUpper(),
                    FileSize = file.Length,
                    PhotoType = photoType,
                    Caption = caption,
                    SortOrder = maxOrder + 1,
                    UploadDate = DateTime.UtcNow
                };

                _context.CompanyPhotos.Add(companyPhoto);
                await _context.SaveChangesAsync();

                return Json(new { success = true, photoId = companyPhoto.Id, fileName });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Company/ReplyToReview (ONE-TIME ONLY REPLY)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyToReview(string reviewId, string reply)
        {
            try
            {
                var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(employerId))
                {
                    return Json(new { success = false, error = "Not authenticated" });
                }

                var review = await _context.Reviews
                    .FirstOrDefaultAsync(r => r.Id == reviewId && r.EmployerId == employerId);

                if (review == null)
                {
                    return Json(new { success = false, error = "Review not found" });
                }

                // ONE-TIME REPLY CHECK
                if (!string.IsNullOrEmpty(review.EmployerReply))
                {
                    return Json(new { success = false, error = "You have already replied to this review" });
                }

                review.EmployerReply = reply;
                review.ReplyDate = DateTime.UtcNow;

                _context.Reviews.Update(review);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Company/DeletePhoto
        [HttpPost]
        public async Task<IActionResult> DeletePhoto(string id)
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, error = "Not authenticated" });
            }

            try
            {
                var photo = await _context.CompanyPhotos
                    .FirstOrDefaultAsync(cp => cp.Id == id && cp.EmployerId == employerId);

                if (photo == null)
                {
                    return Json(new { success = false, error = "Photo not found" });
                }

                // Delete physical file
                var filePath = Path.Combine(_environment.WebRootPath, "uploads", "companyphotos", photo.FileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                // Delete from database
                _context.CompanyPhotos.Remove(photo);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private string GenerateId(string prefix)
        {
            return $"{prefix}{DateTime.Now:yyyyMMddHHmmssfff}";
        }
    }
}*/