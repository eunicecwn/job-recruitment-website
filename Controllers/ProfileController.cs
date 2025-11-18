using DocumentFormat.OpenXml.InkML;
using JobRecruitment.Models;
using JobRecruitment.Models.JobSeekerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "JobSeeker")]
    [Route("[controller]/[action]")]
    public class ProfileController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly DB _db;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            SetCacheHeaders();
            SetViewBagUserInfo();
            base.OnActionExecuting(context);
        }

        private void SetCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "-1";
        }

        private void SetViewBagUserInfo()
        {
            if (User.Identity.IsAuthenticated)
            {
                ViewBag.UserName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.UserEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }
        public ProfileController(DB db, IWebHostEnvironment environment)
        {
            _db = db;
            _environment = environment;
        }

        // ----------------- helpers -----------------
        private static bool LooksLikePdf(IFormFile f)
        {
            if (f == null) return false;
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (ext == ".pdf") return true;
            if (string.Equals(f.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool LooksLikeImage(IFormFile f)
        {
            if (f == null) return false;
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            return new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(ext);
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- PROFILE (VIEW) -----------------
        [HttpGet]
        public async Task<IActionResult> JobSeeker()
        {
            try
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(uid)) return Forbid();

                var js = await _db.JobSeekers
                    .Include(x => x.Experiences)
                    .Include(x => x.Educations)
                    .FirstOrDefaultAsync(x => x.Id == uid);

                if (js is null)
                {
                    TempData["Error"] = "Job seeker profile not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Get skills with their actual IDs
                var skills = await _db.JobSeekerSkills
                    .Where(s => s.JobSeekerId == uid)
                    .OrderBy(s => s.SkillName)
                    .Select(s => new JobSeekerEditVm.SkillRow
                    {
                        Id = s.Id,  // Use the actual ID
                        Name = s.SkillName
                    })
                    .ToListAsync();

                var langs = await _db.Languages
                    .Where(s => s.JobSeekerId == uid)
                    .OrderBy(s => s.Name)
                    .ToListAsync();

                var licenses = await _db.Licenses
                    .Where(s => s.JobSeekerId == uid)
                    .OrderByDescending(s => s.IssuedDate)
                    .ToListAsync();

                // dropdown helpers
                ViewBag.LanguageOptions = await _db.LanguageOptions
                    .Where(o => o.IsActive)
                    .OrderBy(o => o.Name)
                    .ToListAsync();

                ViewBag.Categories = await _db.JobCategories.OrderBy(c => c.Name).ToListAsync();

                var vm = new JobSeekerEditVm
                {
                    Id = js.Id,
                    FullName = js.FullName,
                    Email = js.Email,
                    Phone = js.Phone,
                    Address = js.Address,
                    ExperienceLevel = js.ExperienceLevel,
                    ExistingPhoto = string.IsNullOrWhiteSpace(js.ProfilePhotoFileName)
                        ? null
                        : "/uploads/profilepics/" + js.ProfilePhotoFileName,
                    ExistingResume = js.ResumeFileName,
                    Summary = js.Summary,

                    Experiences = js.Experiences
                        .OrderByDescending(e => e.StartDate)
                        .Select(e => new JobSeekerEditVm.ExperienceRow
                        {
                            Id = e.Id,
                            Role = e.Role,
                            Company = e.Company,
                            StartDate = e.StartDate,
                            EndDate = e.EndDate,
                            Description = e.Description
                        }).ToList(),

                    Educations = js.Educations
                        .OrderByDescending(e => e.StartDate)
                        .Select(e => new JobSeekerEditVm.EducationRow
                        {
                            Id = e.Id,
                            School = e.School,
                            Degree = e.Degree,
                            StartDate = e.StartDate,
                            EndDate = e.EndDate,
                            Description = e.Description
                        }).ToList(),

                    Skills = skills, // Use the correctly populated skills list

                    Languages = langs.Select(l => new JobSeekerEditVm.LanguageRow
                    {
                        Id = l.Id,
                        Language = l.Name,
                        Proficiency = l.Proficiency
                    }).ToList(),

                    Licenses = licenses.Select(c => new JobSeekerEditVm.LicenseRow
                    {
                        Id = c.Id,
                        Title = c.Title,
                        Issuer = c.Issuer,
                        IssuedDate = c.IssuedDate,
                        ExpiresDate = c.ExpiresDate,
                        CredentialUrl = c.CredentialUrl
                    }).ToList(),
                };
                // --- Profile completeness (single source of truth) ---
                var skillCount = await _db.JobSeekerSkills.CountAsync(s => s.JobSeekerId == js.Id);
                var expCount = js.Experiences?.Count ?? 0;
                var eduCount = js.Educations?.Count ?? 0;
                var languageCount = await _db.Languages.CountAsync(l => l.JobSeekerId == js.Id);
                var licenseCount = await _db.Licenses.CountAsync(c => c.JobSeekerId == js.Id);

                ViewBag.ProfileCompleteness = ProfileMeter.Compute(
                    js, skillCount, expCount, eduCount, languageCount, licenseCount
                );


                return View("~/Views/Profile/JobSeeker.cshtml", vm);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while loading your profile.";
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveResume(string Id, IFormFile ResumeFile)
        {
            var jobSeeker = await _db.JobSeekers.FindAsync(Id);
            if (jobSeeker == null)
            {
                return NotFound();
            }

            if (ResumeFile != null && ResumeFile.Length > 0)
            {
                // Validate file
                if (ResumeFile.ContentType != "application/pdf" || ResumeFile.Length > 5 * 1024 * 1024)
                {
                    TempData["Error"] = "Please upload a PDF file under 5MB";
                    return RedirectToAction(nameof(JobSeeker));
                }

                try
                {
                    var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "resumes");
                    if (!Directory.Exists(uploadsDir))
                        Directory.CreateDirectory(uploadsDir);

                    var fileName = $"{Id}_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(ResumeFile.FileName)}";
                    var filePath = Path.Combine(uploadsDir, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await ResumeFile.CopyToAsync(stream);
                    }

                    jobSeeker.ResumeFileName = fileName;
                    await _db.SaveChangesAsync();

                    TempData["Success"] = "Resume uploaded successfully";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error uploading resume: " + ex.Message;
                }
            }

            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBasic(JobSeekerEditVm model, IFormFile Photo, bool RemovePhoto = false)
        {
            var jobSeeker = await _db.JobSeekers.FindAsync(model.Id);
            if (jobSeeker == null)
            {
                return NotFound();
            }

            // Handle cropped photo upload
            if (Photo != null && Photo.Length > 0)
            {
                if (Photo.Length > 2 * 1024 * 1024)
                {
                    TempData["Error"] = "Photo must be under 2MB";
                    return RedirectToAction(nameof(JobSeeker));
                }

                if (!LooksLikeImage(Photo))
                {
                    TempData["Error"] = "Please upload a valid image file (JPG, PNG, GIF, WEBP)";
                    return RedirectToAction(nameof(JobSeeker));
                }

                try
                {
                    var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "profilepics");
                    if (!Directory.Exists(uploadsDir))
                        Directory.CreateDirectory(uploadsDir);

                    // Delete old photo if exists
                    if (!string.IsNullOrEmpty(jobSeeker.ProfilePhotoFileName))
                    {
                        var oldPhotoPath = Path.Combine(uploadsDir, jobSeeker.ProfilePhotoFileName);
                        if (System.IO.File.Exists(oldPhotoPath))
                        {
                            System.IO.File.Delete(oldPhotoPath);
                        }
                    }

                    var fileName = $"{model.Id}_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(Photo.FileName)}";
                    var filePath = Path.Combine(uploadsDir, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await Photo.CopyToAsync(stream);
                    }

                    jobSeeker.ProfilePhotoFileName = fileName;
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error uploading photo: " + ex.Message;
                    return RedirectToAction(nameof(JobSeeker));
                }
            }

            // Handle photo removal
            if (RemovePhoto && !string.IsNullOrEmpty(jobSeeker.ProfilePhotoFileName))
            {
                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "profilepics");
                var oldPhotoPath = Path.Combine(uploadsDir, jobSeeker.ProfilePhotoFileName);
                if (System.IO.File.Exists(oldPhotoPath))
                {
                    System.IO.File.Delete(oldPhotoPath);
                }
                jobSeeker.ProfilePhotoFileName = null;
            }

            // Update other fields
            jobSeeker.FullName = model.FullName;
            jobSeeker.Phone = model.Phone;
            jobSeeker.Address = model.Address;
            jobSeeker.ExperienceLevel = model.ExperienceLevel;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Profile updated successfully";

            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- SUMMARY -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSummary(string Summary)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            if (!string.IsNullOrEmpty(Summary) && Summary.Length > 1500)
            {
                TempData["Error"] = "Personal summary cannot exceed 1500 characters.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var js = await _db.JobSeekers.FirstOrDefaultAsync(x => x.Id == uid);
            if (js is null)
            {
                TempData["Error"] = "Profile not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            js.Summary = string.IsNullOrWhiteSpace(Summary) ? null : Summary.Trim();
            await _db.SaveChangesAsync();

            TempData["Success"] = "Your personal summary has been updated.";
            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- EXPERIENCE -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddExperience([FromForm] string Role, [FromForm] string? Company, [FromForm] DateTime StartDate, [FromForm] DateTime? EndDate, [FromForm] string? Description)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            if (string.IsNullOrWhiteSpace(Role))
            {
                TempData["Error"] = "Role is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var exp = new WorkExperience
            {
                Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                JobSeekerId = uid,
                Role = Role.Trim(),
                Company = string.IsNullOrWhiteSpace(Company) ? null : Company.Trim(),
                StartDate = new DateTime(StartDate.Year, StartDate.Month, 1),
                EndDate = EndDate.HasValue ? new DateTime(EndDate.Value.Year, EndDate.Value.Month, 1) : null,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
            };

            _db.WorkExperiences.Add(exp);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Experience added.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditExperience(string id, [FromForm] string Role, [FromForm] string? Company, [FromForm] DateTime StartDate, [FromForm] DateTime? EndDate, [FromForm] string? Description)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var exp = await _db.WorkExperiences.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (exp is null)
            {
                TempData["Error"] = "Experience not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            if (string.IsNullOrWhiteSpace(Role))
            {
                TempData["Error"] = "Role is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            exp.Role = Role.Trim();
            exp.Company = string.IsNullOrWhiteSpace(Company) ? null : Company.Trim();
            exp.StartDate = new DateTime(StartDate.Year, StartDate.Month, 1);
            exp.EndDate = EndDate.HasValue ? new DateTime(EndDate.Value.Year, EndDate.Value.Month, 1) : null;
            exp.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

            await _db.SaveChangesAsync();
            TempData["Success"] = "Experience updated.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteExperience(string id)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var exp = await _db.WorkExperiences.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (exp is null)
            {
                TempData["Error"] = "Experience not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            _db.WorkExperiences.Remove(exp);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Experience removed.";
            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- EDUCATION -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEducation([FromForm] string School, [FromForm] string Degree, [FromForm] DateTime StartDate, [FromForm] DateTime? EndDate, [FromForm] string? Description)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            if (string.IsNullOrWhiteSpace(School) || string.IsNullOrWhiteSpace(Degree))
            {
                TempData["Error"] = "Institution and course/qualification are required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var ed = new Education
            {
                Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                JobSeekerId = uid,
                School = School.Trim(),
                Degree = Degree.Trim(),
                StartDate = new DateTime(StartDate.Year, StartDate.Month, 1),
                EndDate = EndDate.HasValue ? new DateTime(EndDate.Value.Year, EndDate.Value.Month, 1) : null,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
            };

            _db.Educations.Add(ed);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Education added.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEducation(string id, [FromForm] string School, [FromForm] string Degree, [FromForm] DateTime StartDate, [FromForm] DateTime? EndDate, [FromForm] string? Description)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var edu = await _db.Educations.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (edu is null)
            {
                TempData["Error"] = "Education not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            if (string.IsNullOrWhiteSpace(School) || string.IsNullOrWhiteSpace(Degree))
            {
                TempData["Error"] = "Institution and course/qualification are required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            edu.School = School.Trim();
            edu.Degree = Degree.Trim();
            edu.StartDate = new DateTime(StartDate.Year, StartDate.Month, 1);
            edu.EndDate = EndDate.HasValue ? new DateTime(EndDate.Value.Year, EndDate.Value.Month, 1) : null;
            edu.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

            await _db.SaveChangesAsync();
            TempData["Success"] = "Education updated.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEducation(string id)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var edu = await _db.Educations.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (edu is null)
            {
                TempData["Error"] = "Education not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            _db.Educations.Remove(edu);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Education removed.";
            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- SKILLS -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSkill([FromForm] string Name)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            if (string.IsNullOrWhiteSpace(Name))
            {
                TempData["Error"] = "Skill name is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var skillName = Name.Trim();

            // Check if skill already exists
            var existingSkill = await _db.JobSeekerSkills
                .FirstOrDefaultAsync(s => s.JobSeekerId == uid && s.SkillName == skillName);

            if (existingSkill != null)
            {
                TempData["Error"] = "Skill already exists.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var newSkill = new JobSeekerSkill
            {
                Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                JobSeekerId = uid,
                SkillName = skillName
            };

            _db.JobSeekerSkills.Add(newSkill);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Skill added successfully.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSkill([FromForm] string oldName, [FromForm] string Name)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(Name))
            {
                TempData["Error"] = "Skill name is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var existing = await _db.JobSeekerSkills
                .FirstOrDefaultAsync(x => x.JobSeekerId == uid && x.SkillName == oldName);

            if (existing is null)
            {
                TempData["Error"] = "Skill not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var newName = Name.Trim();

            // Check if new name already exists (excluding current skill)
            var duplicate = await _db.JobSeekerSkills
                .AnyAsync(x => x.JobSeekerId == uid && x.SkillName == newName && x.SkillName != oldName);

            if (duplicate)
            {
                TempData["Error"] = "Skill already exists.";
                return RedirectToAction(nameof(JobSeeker));
            }

            existing.SkillName = newName;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Skill updated successfully.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSkill(string id)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var skill = await _db.JobSeekerSkills
                .FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);

            if (skill is null)
            {
                TempData["Error"] = "Skill not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            try
            {
                _db.JobSeekerSkills.Remove(skill);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Skill removed successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error deleting skill: " + ex.Message;
            }

            return RedirectToAction(nameof(JobSeeker));
        }

        // ----------------- LANGUAGES -----------------
        private static readonly string[] AllowedProficiency =
            new[] { "Basic", "Conversational", "Fluent", "Native" };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLanguage([FromForm] string Language, [FromForm] string? Proficiency)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var name = (Language ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Please select a language.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var prof = (Proficiency ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prof) || !AllowedProficiency.Contains(prof))
            {
                TempData["Error"] = "Please select a valid proficiency.";
                return RedirectToAction(nameof(JobSeeker));
            }

            _db.Languages.Add(new Language
            {
                Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                JobSeekerId = uid,
                Name = name,
                Proficiency = prof
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = "Language added.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditLanguage(string id, [FromForm] string Language, [FromForm] string? Proficiency)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var lang = await _db.Languages.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (lang is null)
            {
                TempData["Error"] = "Language not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var name = (Language ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Please select a language.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var prof = (Proficiency ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prof) || !AllowedProficiency.Contains(prof))
            {
                TempData["Error"] = "Please select a valid proficiency.";
                return RedirectToAction(nameof(JobSeeker));
            }

            lang.Name = name;
            lang.Proficiency = prof;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Language updated.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLanguage(string id)
        {
            try
            {
                var language = await _db.Languages.FindAsync(id);
                if (language == null)
                {
                    return NotFound();
                }

                _db.Languages.Remove(language);
                await _db.SaveChangesAsync();

                TempData["Success"] = "Language deleted successfully";
                return RedirectToAction(nameof(JobSeeker));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error deleting language: " + ex.Message;
                return RedirectToAction(nameof(JobSeeker));
            }
        }

        // ----------------- LICENSES -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLicense([FromForm] string Title, [FromForm] string? Issuer, [FromForm] DateTime? IssuedDate, [FromForm] DateTime? ExpiresDate, [FromForm] string? CredentialUrl)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var title = (Title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Title is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var c = new License
            {
                Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                JobSeekerId = uid,
                Title = title,
                Issuer = string.IsNullOrWhiteSpace(Issuer) ? null : Issuer.Trim(),
                IssuedDate = IssuedDate,
                ExpiresDate = ExpiresDate,
                CredentialUrl = string.IsNullOrWhiteSpace(CredentialUrl) ? null : CredentialUrl.Trim()
            };

            _db.Licenses.Add(c);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Certification added.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditLicense(
            string id,
            [FromForm] string Title,
            [FromForm] string? Issuer,
            [FromForm] DateTime? IssuedDate,
            [FromForm] DateTime? ExpiresDate,
            [FromForm] string? CredentialUrl)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var lic = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (lic is null)
            {
                TempData["Error"] = "License not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            var title = (Title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Title is required.";
                return RedirectToAction(nameof(JobSeeker));
            }

            lic.Title = title;
            lic.Issuer = string.IsNullOrWhiteSpace(Issuer) ? null : Issuer.Trim();
            lic.IssuedDate = IssuedDate;
            lic.ExpiresDate = ExpiresDate;
            lic.CredentialUrl = string.IsNullOrWhiteSpace(CredentialUrl) ? null : CredentialUrl.Trim();

            await _db.SaveChangesAsync();
            TempData["Success"] = "Certification updated.";
            return RedirectToAction(nameof(JobSeeker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLicense(string id)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid)) return Forbid();

            var lic = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && x.JobSeekerId == uid);
            if (lic is null)
            {
                TempData["Error"] = "License not found.";
                return RedirectToAction(nameof(JobSeeker));
            }

            _db.Licenses.Remove(lic);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Certification removed.";
            return RedirectToAction(nameof(JobSeeker));
        }

        private int CalculateProfileCompleteness(JobSeeker jobSeeker)
        {
            int totalScore = 0;
            int maxScore = 100;

            // Basic info (20 points)
            if (!string.IsNullOrEmpty(jobSeeker.FullName)) totalScore += 5;
            if (!string.IsNullOrEmpty(jobSeeker.Email)) totalScore += 5;
            if (!string.IsNullOrEmpty(jobSeeker.Phone)) totalScore += 5;
            if (!string.IsNullOrEmpty(jobSeeker.Address)) totalScore += 5;

            // Profile photo (5 points)
            if (!string.IsNullOrEmpty(jobSeeker.ProfilePhotoFileName)) totalScore += 5;

            // Resume (10 points)
            if (!string.IsNullOrEmpty(jobSeeker.ResumeFileName)) totalScore += 10;

            // Summary (10 points)
            if (!string.IsNullOrEmpty(jobSeeker.Summary)) totalScore += 10;

            // Experience (15 points)
            if (jobSeeker.Experiences?.Any() == true) totalScore += 15;

            // Education (15 points)
            if (jobSeeker.Educations?.Any() == true) totalScore += 15;

            // Skills (10 points)
            if (jobSeeker.Skills?.Any() == true) totalScore += 10;

            // Languages (10 points)
            if (jobSeeker.Languages?.Any() == true) totalScore += 10;

            // Licenses (5 points)
            if (jobSeeker.Licenses?.Any() == true) totalScore += 5;

            return Math.Min(totalScore, maxScore);
        }
    }
}