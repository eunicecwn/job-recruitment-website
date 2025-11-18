using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using X.PagedList;
using X.PagedList.Extensions;

namespace JobRecruitment.Controllers
{
    [Authorize(Roles = "Employer")]
    public class QuestionController : Controller
    {
        private readonly DB db;

        public QuestionController(DB db)
        {
            this.db = db;
        }
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
                ViewBag.EmployerName = User.FindFirstValue(ClaimTypes.Name);
                ViewBag.EmployerEmail = User.FindFirstValue(ClaimTypes.Email);
                ViewBag.ProfilePhotoFileName = User.FindFirstValue("ProfilePhotoFileName");
                ViewBag.EmplpyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }


        // === Sequential ID generation ===
        // Generate Question Id
        private string NextQuestionId(int number)
        {
            return $"QUE{number:D7}"; // e.g., QUE0000007
        }

        // Generate Question Set Id
        private string NextQuestionSetId()
        {
            var numericIds = db.QuestionSets
                .Where(qs => qs.Id.StartsWith("QST") && qs.Id.Length == 10)
                .Select(qs => qs.Id.Substring(3))
                .AsEnumerable()
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .DefaultIfEmpty(0)
                .Max();

            return $"QST{(numericIds + 1):D7}";
        }

        //Get current employer id 
        private string GetCurrentEmployerId()
        {
            var employerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(employerId))
                throw new InvalidOperationException("User is not authenticated or employer ID not found.");

            return employerId;
        }


        // GET:Question/CreateQuestion 
        public IActionResult CreateQuestion()
        {
            var viewModel = new QuestionSetViewModel
            {
                Questions = new List<QuestionViewModel>() // Start with NO questions
            };
            return View(viewModel);
        }

        // POST: Question/CreateQuestion 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuestion(QuestionSetViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Ensure Questions list is not null
                model.Questions ??= new List<QuestionViewModel>();
                return View(model);
            }

            try
            {
                // Generate IDs
                model.Id = NextQuestionSetId();
                model.EmployerId = GetCurrentEmployerId();

                var questionSet = new QuestionSet
                {
                    Id = model.Id,
                    EmployerId = model.EmployerId,
                    Name = model.Name,
                    Description = model.Description ?? "",
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    Questions = new List<Question>()
                };

                // Get the last question number to start from
                var lastQuestion = await db.Questions
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefaultAsync();

                int lastNumber = 0;
                if (lastQuestion != null && int.TryParse(lastQuestion.Id.Substring(3), out var num))
                {
                    lastNumber = num;
                }

                // Process questions - ensure we filter out empty questions
                var validQuestions = model.Questions?
                    .Where(q => !string.IsNullOrWhiteSpace(q.Text))
                    .ToList() ?? new List<QuestionViewModel>();

                int order = 1;
                foreach (var q in validQuestions)
                {
                    lastNumber++;
                    var questionId = NextQuestionId(lastNumber);

                    var question = new Question
                    {
                        Id = questionId,
                        EmployerId = model.EmployerId,
                        QuestionSetId = questionSet.Id,
                        Text = q.Text.Trim(),
                        Type = q.Type,
                        IsRequired = q.IsRequired,
                        Options = string.IsNullOrWhiteSpace(q.Options) ? null : q.Options.Trim(),
                        MaxLength = q.MaxLength,
                        Order = order++
                    };

                    questionSet.Questions.Add(question);
                }

                // Check if we have at least one valid question
                if (!questionSet.Questions.Any())
                {
                    TempData["ErrorMessage"] = "At least one valid question is required.";
                    model.Questions ??= new List<QuestionViewModel>();
                    return View(model);
                }

                // Save to database
                db.QuestionSets.Add(questionSet);
                await db.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Question set created successfully with {questionSet.Questions.Count} questions!";
                return RedirectToAction("QuestionList", "Question");
            }
            catch (Exception ex)
            {
                Console.WriteLine("CreateQuestion Save Error: " + ex);
                ModelState.AddModelError("", "Database error: " + ex.Message);

                // Ensure Questions list is not null for the view
                model.Questions ??= new List<QuestionViewModel>();
                return View(model);
            }
        }

        //GET: Question/Details
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var questionSet = await db.QuestionSets
                    .Include(qs => qs.Questions.OrderBy(q => q.Order))
                    .Include(qs => qs.Jobs)
                    .Include(qs => qs.Employer)
                    .FirstOrDefaultAsync(qs => qs.Id == id);

                if (questionSet == null) return NotFound();

                return View(questionSet);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while loading question set details.";
                Console.WriteLine($"Error in Details: {ex}");
                return RedirectToAction("Index");
            }
        }

        //GET: Question/AssignToJobs  --> assign job to question
        [HttpGet]
        public async Task<IActionResult> AssignToJobs(string id)
        {
            Console.WriteLine($"🔍 GET AssignToJobs called with id: {id}");

            if (string.IsNullOrEmpty(id))
            {
                Console.WriteLine("❌ Question set ID is required");
                TempData["ErrorMessage"] = "Question set ID is required.";
                return RedirectToAction("Index");
            }

            try
            {
                var questionSet = await db.QuestionSets
                    .FirstOrDefaultAsync(qs => qs.Id == id);

                if (questionSet == null)
                {
                    Console.WriteLine($"❌ Question set not found: {id}");
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("Index");
                }

                var employerId = GetCurrentEmployerId();
                Console.WriteLine($"🔍 Loading jobs for employer: {employerId}");

                var availableJobs = await db.Jobs
                    .Where(j => j.EmployerId == employerId && j.Status == JobStatus.Open)
                    .Select(j => new JobInfoViewModel
                    {
                        Id = j.Id,
                        Title = j.Title,
                        HasQuestionSet = j.QuestionSetId != null,
                        CurrentQuestionSetId = j.QuestionSetId,
                        Selected = false
                    })
                    .ToListAsync();

                Console.WriteLine($"🔍 Found {availableJobs.Count} available jobs");
                Console.WriteLine($"🔍 Jobs with existing question sets: {availableJobs.Count(j => j.HasQuestionSet)}");

                var model = new AssignQuestionSetViewModel
                {
                    QuestionSetId = id,
                    QuestionSetName = questionSet.Name,
                    AvailableJobs = availableJobs
                };

                return View(model);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"❌ Authentication error: {ex.Message}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in AssignToJobs GET: {ex}");
                TempData["ErrorMessage"] = "An error occurred while loading jobs for assignment.";
                return RedirectToAction("Details", new { id });
            }
        }

        // POST: Question/AssignToJobs 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignToJobs(AssignQuestionSetViewModel model)
        {
            Console.WriteLine($"🔍 POST AssignToJobs called - ModelState.IsValid: {ModelState.IsValid}");
            Console.WriteLine($"🔍 JobsToUnassign received: '{model.JobsToUnassign}'");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("❌ ModelState is invalid. Errors:");
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    if (errors.Count > 0)
                    {
                        Console.WriteLine($"  {key}: {string.Join(", ", errors.Select(e => e.ErrorMessage))}");
                    }
                }

                try
                {
                    var employerId = GetCurrentEmployerId();
                    Console.WriteLine($"🔍 Repopulating jobs for employer: {employerId}");

                    var selectedIds = model.AvailableJobs?
                        .Where(mj => mj.Selected)
                        .Select(mj => mj.Id)
                        .ToHashSet() ?? new HashSet<string>();

                    Console.WriteLine($"🔍 Previously selected job IDs: {string.Join(", ", selectedIds)}");

                    model.AvailableJobs = await db.Jobs
                        .Where(j => j.EmployerId == employerId && j.Status == JobStatus.Open)
                        .Select(j => new JobInfoViewModel
                        {
                            Id = j.Id,
                            Title = j.Title,
                            HasQuestionSet = j.QuestionSetId != null,
                            CurrentQuestionSetId = j.QuestionSetId,
                            Selected = selectedIds.Contains(j.Id)
                        })
                        .ToListAsync();

                    Console.WriteLine($"🔍 Repopulated {model.AvailableJobs.Count} jobs");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error repopulating jobs: {ex}");
                }

                return View(model);
            }

            Console.WriteLine($"✅ ModelState is valid. Processing assignment for QuestionSet: {model.QuestionSetId}");

            try
            {
                var selectedJobIds = model.AvailableJobs?
                    .Where(j => j.Selected)
                    .Select(j => j.Id)
                    .ToList() ?? new List<string>();

                // Get jobs to unassign (parse from the hidden field)
                var jobsToUnassignIds = new List<string>();
                if (!string.IsNullOrEmpty(model.JobsToUnassign))
                {
                    jobsToUnassignIds = model.JobsToUnassign.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    Console.WriteLine($"🔍 Parsed {jobsToUnassignIds.Count} jobs to unassign: {string.Join(", ", jobsToUnassignIds)}");
                }
                else
                {
                    Console.WriteLine("🔍 No jobs to unassign (JobsToUnassign is null or empty)");
                }

                Console.WriteLine($"🔍 Selected job IDs: {string.Join(", ", selectedJobIds)}");
                Console.WriteLine($"🔍 Jobs to unassign IDs: {string.Join(", ", jobsToUnassignIds)}");

                using var transaction = await db.Database.BeginTransactionAsync();
                Console.WriteLine("🔍 Transaction started");

                // Get all jobs that need to be updated (both assignment and unassignment)
                var allAffectedJobIds = selectedJobIds.Union(jobsToUnassignIds).ToList();
                var jobsToUpdate = await db.Jobs
                    .Where(j => allAffectedJobIds.Contains(j.Id))
                    .ToListAsync();

                Console.WriteLine($"🔍 Found {jobsToUpdate.Count} jobs to update");

                // Process assignments
                var jobsToAssign = jobsToUpdate.Where(j => selectedJobIds.Contains(j.Id)).ToList();
                Console.WriteLine($"🔍 {jobsToAssign.Count} jobs to assign question set");

                var jobsAlreadyAssigned = jobsToAssign.Where(j => j.QuestionSetId == model.QuestionSetId).ToList();
                Console.WriteLine($"🔍 {jobsAlreadyAssigned.Count} jobs already have this question set");

                var jobsWithOtherSets = jobsToAssign.Where(j => j.QuestionSetId != null && j.QuestionSetId != model.QuestionSetId).ToList();
                Console.WriteLine($"🔍 {jobsWithOtherSets.Count} jobs have different question sets");

                foreach (var job in jobsToAssign)
                {
                    Console.WriteLine($"🔍 Assigning job {job.Id} ({job.Title}) from QSet {job.QuestionSetId} to {model.QuestionSetId}");
                    job.QuestionSetId = model.QuestionSetId;
                    db.Jobs.Update(job);
                }

                // Process unassignments
                var jobsToUnassign = jobsToUpdate.Where(j => jobsToUnassignIds.Contains(j.Id)).ToList();
                Console.WriteLine($"🔍 {jobsToUnassign.Count} jobs to unassign question set");

                foreach (var job in jobsToUnassign)
                {
                    Console.WriteLine($"🔍 Unassigning job {job.Id} ({job.Title}) from QSet {job.QuestionSetId} to NULL");
                    job.QuestionSetId = null;
                    db.Jobs.Update(job);
                }

                // Check if any changes need to be saved
                if (jobsToAssign.Count > 0 || jobsToUnassign.Count > 0)
                {
                    var saveResult = await db.SaveChangesAsync();
                    Console.WriteLine($"🔍 SaveChangesAsync result: {saveResult} entities affected");

                    await transaction.CommitAsync();
                    Console.WriteLine("✅ Transaction committed");
                }
                else
                {
                    Console.WriteLine("ℹ️ No changes to save, rolling back transaction");
                    await transaction.RollbackAsync();
                }

                // Prepare success message with details
                var message = $"Question set assignments updated successfully!";

                if (jobsToAssign.Any())
                {
                    message += $" Assigned to {jobsToAssign.Count} job(s).";
                }

                if (jobsToUnassign.Any())
                {
                    message += $" Removed from {jobsToUnassign.Count} job(s).";
                }

                if (jobsAlreadyAssigned.Any())
                {
                    message += $" {jobsAlreadyAssigned.Count} job(s) were already assigned this set.";
                }

                if (jobsWithOtherSets.Any())
                {
                    message += $" {jobsWithOtherSets.Count} job(s) had their previous question set replaced.";
                }

                Console.WriteLine($"✅ Success: {message}");
                TempData["SuccessMessage"] = message;
                return RedirectToAction("Details", new { id = model.QuestionSetId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AssignToJobs Exception: {ex}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");

                try
                {
                    var employerId = GetCurrentEmployerId();
                    Console.WriteLine($"🔍 Attempting to repopulate jobs after error for employer: {employerId}");

                    var selectedIds = model.AvailableJobs?
                        .Where(mj => mj.Selected)
                        .Select(mj => mj.Id)
                        .ToHashSet() ?? new HashSet<string>();

                    model.AvailableJobs = await db.Jobs
                        .Where(j => j.EmployerId == employerId && j.Status == JobStatus.Open)
                        .Select(j => new JobInfoViewModel
                        {
                            Id = j.Id,
                            Title = j.Title,
                            HasQuestionSet = j.QuestionSetId != null,
                            CurrentQuestionSetId = j.QuestionSetId,
                            Selected = selectedIds.Contains(j.Id)
                        })
                        .ToListAsync();

                    Console.WriteLine($"🔍 Repopulated {model.AvailableJobs.Count} jobs after error");
                }
                catch (Exception repopulateEx)
                {
                    Console.WriteLine($"❌ Error repopulating jobs: {repopulateEx}");
                }

                ModelState.AddModelError("", "An error occurred while assigning the question set: " + ex.Message);
                return View(model);
            }
        }

        //GET: Question/QuestionList
        public async Task<IActionResult> QuestionList(int? page, string search = null)
        {
            try
            {
                var employerId = GetCurrentEmployerId();
                int pageSize = 9;
                int pageNumber = page ?? 1;

                // Query for QUESTION SETS, not individual questions
                IQueryable<QuestionSet> questionSetsQuery = db.QuestionSets
                    .Include(qs => qs.Questions)
                    .Include(qs => qs.Jobs)
                    .Where(qs => qs.EmployerId == employerId);

                if (!string.IsNullOrEmpty(search))
                {
                    questionSetsQuery = questionSetsQuery.Where(qs =>
                        qs.Name.Contains(search) ||
                        qs.Description.Contains(search) ||
                        qs.Questions.Any(q => q.Text.Contains(search)));
                }

                // Use synchronous ToPagedList for Entity Framework 6
                var questionSets = questionSetsQuery
                    .OrderByDescending(qs => qs.CreatedDate)
                    .ToPagedList(pageNumber, pageSize);

                ViewBag.SearchTerm = search;

                return View(questionSets);
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index", "Question");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while loading question sets.";
                Console.WriteLine($"Error in QuestionList: {ex}");
                return View(new List<QuestionSet>().ToPagedList(1, 9));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Search(
            string? searchTerm = null,
            string? status = null,
            int? minQuestions = null,
            int? minJobsAssigned = null,
            string sortBy = "CreatedDate",
            string sortOrder = "desc",
            string viewMode = "card",
            int page = 1,
            int pageSize = 6)
        {
            try
            {
                // Build query
                var query = db.QuestionSets
                    .Include(qs => qs.Questions)
                    .Include(qs => qs.Jobs)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(qs =>
                        qs.Name.Contains(searchTerm) ||
                        (qs.Description != null && qs.Description.Contains(searchTerm)));
                }

                if (!string.IsNullOrEmpty(status))
                {
                    bool isActive = bool.Parse(status);
                    query = query.Where(qs => qs.IsActive == isActive);
                }

                if (minQuestions.HasValue)
                {
                    query = query.Where(qs => qs.Questions.Count >= minQuestions.Value);
                }

                if (minJobsAssigned.HasValue)
                {
                    query = query.Where(qs => qs.Jobs.Count >= minJobsAssigned.Value);
                }

                // Apply sorting
                switch (sortBy.ToLower())
                {
                    case "name":
                        query = sortOrder.ToLower() == "asc"
                            ? query.OrderBy(qs => qs.Name)
                            : query.OrderByDescending(qs => qs.Name);
                        break;
                    case "questioncount":
                        query = sortOrder.ToLower() == "asc"
                            ? query.OrderBy(qs => qs.Questions.Count)
                            : query.OrderByDescending(qs => qs.Questions.Count);
                        break;
                    case "jobcount":
                        query = sortOrder.ToLower() == "asc"
                            ? query.OrderBy(qs => qs.Jobs.Count)
                            : query.OrderByDescending(qs => qs.Jobs.Count);
                        break;
                    default: // CreatedDate
                        query = sortOrder.ToLower() == "asc"
                            ? query.OrderBy(qs => qs.CreatedDate)
                            : query.OrderByDescending(qs => qs.CreatedDate);
                        break;
                }

                // Get total count
                var totalCount = await query.CountAsync();

                // Paginate
                var questionSets = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(qs => new
                    {
                        qs.Id,
                        qs.Name,
                        qs.Description,
                        qs.IsActive,
                        qs.CreatedDate,
                        QuestionCount = qs.Questions.Count,
                        JobCount = qs.Jobs.Count
                    })
                    .ToListAsync();

                // Calculate total pages
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                // Return JSON response for AJAX requests
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new
                    {
                        success = true,
                        questionSets,
                        totalCount,
                        currentPage = page,
                        pageSize,
                        totalPages,
                        viewMode
                    });
                }

                // For non-AJAX requests, return the view with paginated list
                var pagedList = new StaticPagedList<dynamic>(
                    questionSets, page, pageSize, totalCount);

                ViewBag.SearchTerm = searchTerm;
                ViewBag.Status = status;
                ViewBag.MinQuestions = minQuestions;
                ViewBag.MinJobsAssigned = minJobsAssigned;
                ViewBag.SortBy = sortBy;
                ViewBag.SortOrder = sortOrder;
                ViewBag.ViewMode = viewMode;
                ViewBag.PageSize = pageSize;

                return View("QuestionList", pagedList);
            }
            catch (Exception ex)
            {
                return View("QuestionList", new StaticPagedList<dynamic>(new List<dynamic>(), 1, pageSize, 0));
            }
        }


        // GET: Question/Deactivate/5
        [HttpGet]
        public async Task<IActionResult> Deactivate(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["ErrorMessage"] = "Question set ID is required.";
                return RedirectToAction("Index");
            }

            try
            {
                var employerId = GetCurrentEmployerId();
                var questionSet = await db.QuestionSets
                    .FirstOrDefaultAsync(qs => qs.Id == id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("Index");
                }

                questionSet.IsActive = false;
                db.QuestionSets.Update(questionSet);
                await db.SaveChangesAsync();

                TempData["SuccessMessage"] = "Question set deactivated successfully.";
                return RedirectToAction("Index");
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while deactivating the question set.";
                Console.WriteLine($"Error in Deactivate: {ex}");
                return RedirectToAction("Index");
            }
        }

        // GET: Question/Activate/5
        [HttpGet]
        public async Task<IActionResult> Activate(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["ErrorMessage"] = "Question set ID is required.";
                return RedirectToAction("Index");
            }

            try
            {
                var employerId = GetCurrentEmployerId();
                var questionSet = await db.QuestionSets
                    .FirstOrDefaultAsync(qs => qs.Id == id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("Index");
                }

                questionSet.IsActive = true;
                db.QuestionSets.Update(questionSet);
                await db.SaveChangesAsync();

                TempData["SuccessMessage"] = "Question set activated successfully.";
                return RedirectToAction("Index");
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while activating the question set.";
                Console.WriteLine($"Error in Activate: {ex}");
                return RedirectToAction("Index");
            }
        }

        // GET: Question/UpdateQuestion/5
        [HttpGet]
        public async Task<IActionResult> UpdateQuestion(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var employerId = GetCurrentEmployerId();

                // Load the question set with its questions
                var questionSet = await db.QuestionSets
                    .Include(qs => qs.Questions.OrderBy(q => q.Order))
                    .FirstOrDefaultAsync(qs => qs.Id == id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("Index");
                }

                // Map to view model
                var model = new QuestionSetViewModel
                {
                    Id = questionSet.Id,
                    EmployerId = questionSet.EmployerId,
                    Name = questionSet.Name,
                    Description = questionSet.Description,
                    IsActive = questionSet.IsActive,
                    Questions = questionSet.Questions
                        .OrderBy(q => q.Order)
                        .Select(q => new QuestionViewModel
                        {
                            Id = q.Id,
                            Text = q.Text,
                            Type = q.Type,
                            IsRequired = q.IsRequired,
                            Options = q.Options,
                            MaxLength = q.MaxLength,
                            Order = q.Order
                        })
                        .ToList()
                };

                return View(model);
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while loading the question set.";
                Console.WriteLine($"Error in UpdateQuestion GET: {ex}");
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuestion(QuestionSetViewModel model)
        {
            Console.WriteLine($"🔄 UpdateQuestion POST called for QuestionSet: {model?.Id}");

            // Extract deleted question IDs from form data - FIXED ARRAY FORMAT
            var deletedQuestionIds = new List<string>();
            var form = await HttpContext.Request.ReadFormAsync();

            // Handle array format: DeletedQuestionIds[0], DeletedQuestionIds[1], etc.
            foreach (var key in form.Keys)
            {
                if (key.StartsWith("DeletedQuestionIds[") && key.EndsWith("]") && !string.IsNullOrEmpty(form[key]))
                {
                    deletedQuestionIds.Add(form[key]);
                }
            }

            Console.WriteLine($"📋 DeletedQuestionIds extracted: {string.Join(", ", deletedQuestionIds)}");
            Console.WriteLine($"📋 Questions in model: {(model.Questions != null ? model.Questions.Count : 0)}");

            // Clean up validation errors for deleted questions
            if (deletedQuestionIds.Any())
            {
                Console.WriteLine("🧹 Cleaning up validation errors for deleted questions...");

                // Remove validation errors for properties related to deleted questions
                var keysToRemove = ModelState.Keys
                    .Where(key => deletedQuestionIds.Any(deletedId =>
                        key.Contains(deletedId) ||
                        key.Contains($"Questions[{deletedId}]")))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    Console.WriteLine($"❌ Removing validation error: {key}");
                    ModelState.Remove(key);
                }
            }

            if (!ModelState.IsValid)
            {
                Console.WriteLine("❌ ModelState is invalid");
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    if (errors.Count > 0)
                    {
                        Console.WriteLine($"  {key}: {string.Join(", ", errors.Select(e => e.ErrorMessage))}");
                    }
                }

                // In the UpdateQuestion POST method, add this validation before the main try-catch:
                var validQuestions = model.Questions?
                    .Where(q => !string.IsNullOrWhiteSpace(q.Text) && !deletedQuestionIds.Contains(q.Id))
                    .ToList() ?? new List<QuestionViewModel>();

                if (!validQuestions.Any())
                {
                    TempData["ErrorMessage"] = "At least one valid question is required.";

                    // Repopulate the model
                    try
                    {
                        var employerId = GetCurrentEmployerId();
                        var existingQuestionSet = await db.QuestionSets
                            .Include(qs => qs.Questions.OrderBy(q => q.Order))
                            .FirstOrDefaultAsync(qs => qs.Id == model.Id && qs.EmployerId == employerId);

                        if (existingQuestionSet != null)
                        {
                            model.Questions = existingQuestionSet.Questions
                                .Where(q => !deletedQuestionIds.Contains(q.Id))
                                .OrderBy(q => q.Order)
                                .Select(q => new QuestionViewModel
                                {
                                    Id = q.Id,
                                    Text = q.Text,
                                    Type = q.Type,
                                    IsRequired = q.IsRequired,
                                    Options = q.Options,
                                    MaxLength = q.MaxLength,
                                    Order = q.Order
                                })
                                .ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error repopulating model: {ex}");
                    }

                    return View("UpdateQuestion", model);
                }

                // Repopulate the model for the view - FILTER OUT DELETED QUESTIONS
                try
                {
                    var employerId = GetCurrentEmployerId();
                    var existingQuestionSet = await db.QuestionSets
                        .Include(qs => qs.Questions.OrderBy(q => q.Order))
                        .FirstOrDefaultAsync(qs => qs.Id == model.Id && qs.EmployerId == employerId);

                    if (existingQuestionSet != null)
                    {
                        // Filter out deleted questions from the repopulated model
                        model.Questions = existingQuestionSet.Questions
                            .Where(q => !deletedQuestionIds.Contains(q.Id))
                            .OrderBy(q => q.Order)
                            .Select(q => new QuestionViewModel
                            {
                                Id = q.Id,
                                Text = q.Text,
                                Type = q.Type,
                                IsRequired = q.IsRequired,
                                Options = q.Options,
                                MaxLength = q.MaxLength,
                                Order = q.Order
                            })
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error repopulating model: {ex}");
                }

                return View("UpdateQuestion", model);
            }

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var employerId = GetCurrentEmployerId();

                // Get the existing question set with ALL questions
                var questionSet = await db.QuestionSets
                    .Include(qs => qs.Questions)
                    .FirstOrDefaultAsync(qs => qs.Id == model.Id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("Index");
                }

                // === CRITICAL FIX: Delete questions that exist in DB but not in model ===
                var submittedQuestionIds = model.Questions?
                    .Where(q => !string.IsNullOrEmpty(q.Id) && !deletedQuestionIds.Contains(q.Id))
                    .Select(q => q.Id)
                    .ToList() ?? new List<string>();

                // Find questions that exist in database but weren't submitted (should be deleted)
                var questionsToDelete = questionSet.Questions
                    .Where(q => !submittedQuestionIds.Contains(q.Id) && !deletedQuestionIds.Contains(q.Id))
                    .ToList();

                if (questionsToDelete.Any())
                {
                    Console.WriteLine($"🗑️ Found {questionsToDelete.Count} questions to delete that weren't in submitted model");
                    foreach (var question in questionsToDelete)
                    {
                        Console.WriteLine($"✅ Deleting question: {question.Id} - {question.Text}");
                        db.Questions.Remove(question);
                        // Also add to deletedQuestionIds to prevent double processing
                        if (!deletedQuestionIds.Contains(question.Id))
                        {
                            deletedQuestionIds.Add(question.Id);
                        }
                    }
                }

                Console.WriteLine($"📊 Original questions in set: {questionSet.Questions.Count}");

                // Update question set properties
                questionSet.Name = model.Name;
                questionSet.Description = model.Description;
                questionSet.IsActive = model.IsActive;

                db.QuestionSets.Update(questionSet);

                // Handle deleted questions FIRST - THIS IS THE CRITICAL FIX
                if (deletedQuestionIds.Any())
                {
                    Console.WriteLine($"🗑️ Deleting {deletedQuestionIds.Count} questions: {string.Join(", ", deletedQuestionIds)}");

                    foreach (var questionId in deletedQuestionIds)
                    {
                        if (!string.IsNullOrEmpty(questionId))
                        {
                            Console.WriteLine($"🔍 Looking for question to delete: {questionId}");

                            // Find the question in the database
                            var question = await db.Questions.FindAsync(questionId);

                            // Additional security check: ensure the question belongs to this employer and question set
                            if (question != null && question.EmployerId == employerId && question.QuestionSetId == questionSet.Id)
                            {
                                Console.WriteLine($"✅ Deleting question from database: {questionId} - {question.Text}");
                                db.Questions.Remove(question);
                            }
                            else
                            {
                                Console.WriteLine($"❌ Question not found or not authorized to delete: {questionId}");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("ℹ️ No questions marked for deletion");
                }

                // Update or add questions
                if (model.Questions != null)
                {
                    int order = 1;
                    foreach (var questionVm in model.Questions)
                    {
                        // Skip questions that were marked for deletion
                        if (deletedQuestionIds.Contains(questionVm.Id))
                        {
                            Console.WriteLine($"⏭️ Skipping deleted question: {questionVm.Id}");
                            continue;
                        }

                        // Skip empty new questions (text is null or empty)
                        if (string.IsNullOrEmpty(questionVm.Id) && string.IsNullOrWhiteSpace(questionVm.Text))
                        {
                            Console.WriteLine($"⏭️ Skipping empty new question");
                            continue;
                        }

                        if (string.IsNullOrEmpty(questionVm.Id))
                        {
                            // New question - generate ID
                            var lastQuestion = await db.Questions
                                .OrderByDescending(q => q.Id)
                                .FirstOrDefaultAsync();

                            int lastNumber = 0;
                            if (lastQuestion != null && int.TryParse(lastQuestion.Id.Substring(3), out var num))
                            {
                                lastNumber = num;
                            }

                            var questionId = NextQuestionId(lastNumber + 1);

                            var newQuestion = new Question
                            {
                                Id = questionId,
                                Text = questionVm.Text,
                                Type = questionVm.Type,
                                IsRequired = questionVm.IsRequired,
                                Options = questionVm.Options,
                                MaxLength = questionVm.MaxLength,
                                Order = order++,
                                QuestionSetId = questionSet.Id,
                                EmployerId = employerId
                            };

                            Console.WriteLine($"➕ Adding new question: {questionId} - {questionVm.Text}");
                            await db.Questions.AddAsync(newQuestion);
                        }
                        else
                        {
                            // Existing question - update
                            var existingQuestion = questionSet.Questions
                                .FirstOrDefault(q => q.Id == questionVm.Id);

                            if (existingQuestion != null)
                            {
                                Console.WriteLine($"✏️ Updating existing question: {questionVm.Id} - {questionVm.Text}");
                                existingQuestion.Text = questionVm.Text;
                                existingQuestion.Type = questionVm.Type;
                                existingQuestion.IsRequired = questionVm.IsRequired;
                                existingQuestion.Options = questionVm.Options;
                                existingQuestion.MaxLength = questionVm.MaxLength;
                                existingQuestion.Order = order++;

                                db.Questions.Update(existingQuestion);
                            }
                            else
                            {
                                Console.WriteLine($"❌ Existing question not found: {questionVm.Id}");
                            }
                        }
                    }
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                Console.WriteLine($"✅ Successfully updated question set {model.Id}");
                TempData["SuccessMessage"] = "Question set updated successfully!";
                return RedirectToAction("Details", new { id = model.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Error updating question set: {ex}");
                Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                TempData["ErrorMessage"] = "An error occurred while updating the question set.";

                // Repopulate the model for the view - FILTER OUT DELETED QUESTIONS
                try
                {
                    var employerId = GetCurrentEmployerId();
                    var existingQuestionSet = await db.QuestionSets
                        .Include(qs => qs.Questions.OrderBy(q => q.Order))
                        .FirstOrDefaultAsync(qs => qs.Id == model.Id && qs.EmployerId == employerId);

                    if (existingQuestionSet != null)
                    {
                        // Filter out deleted questions from the repopulated model
                        model.Questions = existingQuestionSet.Questions
                            .Where(q => !deletedQuestionIds.Contains(q.Id))
                            .OrderBy(q => q.Order)
                            .Select(q => new QuestionViewModel
                            {
                                Id = q.Id,
                                Text = q.Text,
                                Type = q.Type,
                                IsRequired = q.IsRequired,
                                Options = q.Options,
                                MaxLength = q.MaxLength,
                                Order = q.Order
                            })
                            .ToList();
                    }
                }
                catch (Exception repopEx)
                {
                    Console.WriteLine($"Error repopulating model: {repopEx}");
                }

                return View("UpdateQuestion", model);
            }
        }

        // POST: Question/ToggleStatus
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusRequest request)
        {
            try
            {
                Console.WriteLine($"🔍 ToggleStatus called with ID: {request?.Id}, IsActive: {request?.IsActive}");

                if (request == null || string.IsNullOrEmpty(request.Id))
                {
                    Console.WriteLine("❌ Invalid request data");
                    return Json(new { success = false, message = "Invalid request data." });
                }

                var employerId = GetCurrentEmployerId();
                var questionSet = await db.QuestionSets
                    .FirstOrDefaultAsync(qs => qs.Id == request.Id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    Console.WriteLine($"❌ Question set not found: {request.Id}");
                    return Json(new { success = false, message = "Question set not found." });
                }

                Console.WriteLine($"🔄 Changing status from {questionSet.IsActive} to {request.IsActive}");

                questionSet.IsActive = request.IsActive;
                db.QuestionSets.Update(questionSet);
                await db.SaveChangesAsync();

                Console.WriteLine($"✅ Successfully updated question set {request.Id} to {(request.IsActive ? "Active" : "Inactive")}");

                return Json(new
                {
                    success = true,
                    message = $"Question set {(request.IsActive ? "activated" : "deactivated")} successfully.",
                    newStatus = request.IsActive
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in ToggleStatus: {ex}");
                Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return Json(new { success = false, message = "An error occurred while updating the question set status." });
            }
        }

        // GET: Question/ToggleStatus (for backward compatibility)
        [HttpGet]
        public async Task<IActionResult> ToggleStatus(string id)
        {
            try
            {
                var employerId = GetCurrentEmployerId();
                var questionSet = await db.QuestionSets
                    .FirstOrDefaultAsync(qs => qs.Id == id && qs.EmployerId == employerId);

                if (questionSet == null)
                {
                    TempData["ErrorMessage"] = "Question set not found.";
                    return RedirectToAction("QuestionList");
                }

                questionSet.IsActive = !questionSet.IsActive;
                db.QuestionSets.Update(questionSet);
                await db.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Question set {(questionSet.IsActive ? "activated" : "deactivated")} successfully.";
                return RedirectToAction("QuestionList");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ToggleStatus GET: {ex}");
                TempData["ErrorMessage"] = "An error occurred while updating the question set status.";
                return RedirectToAction("QuestionList");
            }
        }

    }
}