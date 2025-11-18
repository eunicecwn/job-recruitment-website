using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobRecruitment.Models
{
    public class DB : DbContext
    {
        public DB(DbContextOptions<DB> options) : base(options) { }

        // DbSets for all entities
        public DbSet<UserBase> Users { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Employer> Employers { get; set; }
        public DbSet<JobSeeker> JobSeekers { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<Application> Applications { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<QuestionSet> QuestionSets { get; set; }
        public DbSet<QuestionResponse> QuestionResponses { get; set; }
        public DbSet<JobCategory> JobCategories { get; set; }
        public DbSet<JobSeekerSkill> JobSeekerSkills { get; set; }
        public DbSet<AdminLog> AdminLogs { get; set; }
        public DbSet<EmployerReport> EmployerReports { get; set; }
        public DbSet<UserReport> UserReports { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<UserIdCounter> UserIdCounters { get; set; }
        public DbSet<JobReport> JobReports { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<LanguageOption> LanguageOptions { get; set; }
        public DbSet<License> Licenses { get; set; }
        public DbSet<WorkExperience> WorkExperiences { get; set; }
        public DbSet<Education> Educations { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<SavedJob> SavedJobs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure string IDs
            modelBuilder.Entity<UserBase>(e =>
            {
                e.Property(u => u.Id).HasMaxLength(10);
                e.HasDiscriminator<string>("UserType")
                    .HasValue<Admin>("Admin")
                    .HasValue<Employer>("Employer")
                    .HasValue<JobSeeker>("JobSeeker");
            });

            // Configure other entities' IDs
            modelBuilder.Entity<Job>(e => e.Property(j => j.Id).HasMaxLength(10));
            modelBuilder.Entity<Application>(e => e.Property(a => a.Id).HasMaxLength(10));
            modelBuilder.Entity<Review>(e => e.Property(r => r.Id).HasMaxLength(10));
            modelBuilder.Entity<Report>(e => e.Property(r => r.Id).HasMaxLength(10));
            modelBuilder.Entity<Question>(e => e.Property(q => q.Id).HasMaxLength(10));
            modelBuilder.Entity<QuestionSet>(e => e.Property(qs => qs.Id).HasMaxLength(10));
            modelBuilder.Entity<QuestionResponse>(e => e.Property(qr => qr.Id).HasMaxLength(10));
            modelBuilder.Entity<JobCategory>(e => e.Property(jc => jc.Id).HasMaxLength(10));
            modelBuilder.Entity<AdminLog>(e => e.Property(al => al.Id).HasMaxLength(10));
            modelBuilder.Entity<UserProfile>(e => e.Property(up => up.Id).HasMaxLength(10));

            // Configure Reports relationships
            modelBuilder.Entity<Report>()
                .HasOne(r => r.JobSeeker)
                .WithMany(js => js.Reports)
                .HasForeignKey(r => r.JobSeekerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Report>()
                .HasOne(r => r.Employer)
                .WithMany()
                .HasForeignKey(r => r.EmployerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Reviews relationships
            modelBuilder.Entity<Review>()
                .HasOne(r => r.JobSeeker)
                .WithMany(js => js.Reviews)
                .HasForeignKey(r => r.JobSeekerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.Employer)
                .WithMany(e => e.Reviews)
                .HasForeignKey(r => r.EmployerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Employer approval relationship
            modelBuilder.Entity<Employer>()
                .HasOne(e => e.ApprovedByAdmin)
                .WithMany()
                .HasForeignKey(e => e.ApprovedByAdminId)
                .HasPrincipalKey(a => a.Id)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Job relationships
            modelBuilder.Entity<Job>()
                .HasOne(j => j.Employer)
                .WithMany(e => e.Jobs)
                .HasForeignKey(j => j.EmployerId)
                .HasPrincipalKey(e => e.Id)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Application relationships
            modelBuilder.Entity<Application>()
                .HasOne(a => a.Job)
                .WithMany(j => j.Applications)
                .HasForeignKey(a => a.JobId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Application>()
                .HasOne(a => a.JobSeeker)
                .WithMany(js => js.Applications)
                .HasForeignKey(a => a.JobSeekerId)
                .HasPrincipalKey(js => js.Id)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Question relationships
            modelBuilder.Entity<Question>()
                .HasOne(q => q.Job)
                .WithMany(j => j.Questions)
                .HasForeignKey(q => q.JobId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Question>()
                .HasOne(q => q.Employer)
                .WithMany(e => e.Questions)
                .HasForeignKey(q => q.EmployerId)
                .HasPrincipalKey(e => e.Id)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Job>()
                .HasOne(j => j.Category)
                .WithMany(c => c.Jobs)
                .HasForeignKey(j => j.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // JobSeekerSkill 
            modelBuilder.Entity<JobSeekerSkill>(e =>
            {
                // Configure the primary key
                e.HasKey(x => x.Id); // ← This line is missing!

                e.Property(x => x.Id).HasMaxLength(10);
                e.Property(x => x.JobSeekerId).HasMaxLength(10);
                e.Property(x => x.SkillName).HasMaxLength(100).IsRequired();

                e.HasOne(x => x.JobSeeker)
                    .WithMany(js => js.Skills)
                    .HasForeignKey(x => x.JobSeekerId)
                    .HasPrincipalKey(js => js.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // WorkExperience
            modelBuilder.Entity<WorkExperience>(e =>
            {
                e.Property(x => x.Id).HasMaxLength(10);
                e.HasOne(x => x.JobSeeker).WithMany(js => js.Experiences)
                    .HasForeignKey(x => x.JobSeekerId)
                    .HasPrincipalKey(js => js.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            //Education
            modelBuilder.Entity<Education>(e =>
            {
                e.Property(x => x.Id).HasMaxLength(10);
                e.HasOne(x => x.JobSeeker).WithMany(js => js.Educations)
                    .HasForeignKey(x => x.JobSeekerId)
                    .HasPrincipalKey(js => js.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Language>(e =>
            {
                e.ToTable("Languages");
                e.Property(x => x.Id).HasMaxLength(50);

                // Change this from 50 to 10 to match Users.Id
                e.Property(x => x.JobSeekerId).HasMaxLength(10).IsRequired();

                e.Property(x => x.Name)
                    .HasColumnName("Language")
                    .HasMaxLength(80)
                    .IsRequired();

                e.Property(x => x.Proficiency).HasMaxLength(40);

                // default defined in SQL
                e.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("sysdatetime()");

                e.HasOne(x => x.JobSeeker)
                    .WithMany(js => js.Languages)
                    .HasForeignKey(x => x.JobSeekerId)
                    .HasPrincipalKey(js => js.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LanguageOption>(e =>
            {
                e.ToTable("LanguageOptions");
                e.HasKey(x => x.Code);
                e.Property(x => x.Code).HasMaxLength(8);
                e.Property(x => x.Name).HasMaxLength(100);
            });

            // LICENSES
            modelBuilder.Entity<License>(e =>
            {
                e.Property(x => x.Id).HasMaxLength(10);
                e.Property(x => x.JobSeekerId).HasMaxLength(10).IsRequired();
                e.Property(x => x.Title).HasMaxLength(150).IsRequired();
                e.Property(x => x.Issuer).HasMaxLength(150);
                e.Property(x => x.CredentialUrl).HasMaxLength(300);

                e.HasOne(x => x.JobSeeker)
                    .WithMany(js => js.Licenses)
                    .HasForeignKey(x => x.JobSeekerId)
                    .HasPrincipalKey(js => js.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Admin hierarchy
            modelBuilder.Entity<Admin>()
                .HasOne(a => a.Supervisor)
                .WithMany(a => a.Subordinates)
                .HasForeignKey(a => a.SupervisorId)
                .HasPrincipalKey(a => a.Id)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Job>()
                .Property(j => j.Latitude)
                .HasColumnType("decimal(9,6)");

            modelBuilder.Entity<Job>()
                .Property(j => j.Longitude)
                .HasColumnType("decimal(9,6)");

            // Configure ReportBase TPH inheritance
            modelBuilder.Entity<ReportBase>()
                .HasDiscriminator<string>("ReportType")
                .HasValue<EmployerReport>("EmployerReport")
                .HasValue<UserReport>("UserReport");

            // EmployerReport relationships
            modelBuilder.Entity<EmployerReport>()
                .HasOne(er => er.Employer)
                .WithMany()
                .HasForeignKey(er => er.EmployerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EmployerReport>()
                .HasOne(er => er.Reporter)
                .WithMany()
                .HasForeignKey(er => er.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);

            // UserReport relationships
            modelBuilder.Entity<UserReport>()
                .HasOne(ur => ur.ReportedUser)
                .WithMany()
                .HasForeignKey(ur => ur.ReportedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserReport>()
                .HasOne(ur => ur.Employer)
                .WithMany()
                .HasForeignKey(ur => ur.EmployerId)
                .OnDelete(DeleteBehavior.Restrict);

            // AdminLog relationship
            modelBuilder.Entity<AdminLog>()
                .HasOne(al => al.Admin)
                .WithMany()
                .HasForeignKey(al => al.AdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // UserProfile relationship
            modelBuilder.Entity<UserProfile>()
                .HasOne(up => up.User)
                .WithOne(u => u.UserProfile)
                .HasForeignKey<UserProfile>(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure QuestionSet relationships
            modelBuilder.Entity<QuestionSet>()
                .HasOne(qs => qs.Employer)
                .WithMany(e => e.QuestionSets)
                .HasForeignKey(qs => qs.EmployerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Update Question relationships for QuestionSet
            modelBuilder.Entity<Question>()
                .HasOne(q => q.QuestionSet)
                .WithMany(qs => qs.Questions)
                .HasForeignKey(q => q.QuestionSetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Job-QuestionSet relationship
            modelBuilder.Entity<Job>()
                .HasOne(j => j.QuestionSet)
                .WithMany(qs => qs.Jobs)
                .HasForeignKey(j => j.QuestionSetId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure QuestionResponse relationships
            modelBuilder.Entity<QuestionResponse>()
                .HasOne(qr => qr.Question)
                .WithMany(q => q.Responses)
                .HasForeignKey(qr => qr.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionResponse>()
                .HasOne(qr => qr.Application)
                .WithMany(a => a.QuestionResponses)
                .HasForeignKey(qr => qr.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionResponse>()
                .HasOne(qr => qr.JobSeeker)
                .WithMany()
                .HasForeignKey(qr => qr.JobSeekerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<JobReport>(e =>
            {
                e.Property(jr => jr.Id).HasMaxLength(10);

                e.HasOne(jr => jr.Job)
                 .WithMany(j => j.JobReports)
                 .HasForeignKey(jr => jr.JobId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Subscription>(e =>
            {
                e.Property(s => s.Id).HasMaxLength(10);
                e.HasOne(s => s.User)
                 .WithMany()
                 .HasForeignKey(s => s.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure Payment relationships
            modelBuilder.Entity<Payment>(e =>
            {
                e.Property(p => p.Id).HasMaxLength(10);
                e.HasOne(p => p.User)
                 .WithMany()
                 .HasForeignKey(p => p.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(p => p.Subscription)
                 .WithMany()
                 .HasForeignKey(p => p.SubscriptionId)
                 .OnDelete(DeleteBehavior.SetNull);
            });


            // Configure Review properties
            modelBuilder.Entity<Review>(e =>
            {
                e.Property(r => r.Rating).HasDefaultValue(5);
                e.Property(r => r.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
            });

            modelBuilder.Entity<SavedJob>(e =>
            {
                e.Property(x => x.Id).HasMaxLength(10);
                e.Property(x => x.JobSeekerId).HasMaxLength(10);
                e.Property(x => x.JobId).HasMaxLength(10);

                e.HasOne(x => x.JobSeeker)
                 .WithMany() // (or add ICollection<SavedJob> Saved if you want)
                 .HasForeignKey(x => x.JobSeekerId)
                 .HasPrincipalKey(js => js.Id)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Job)
                 .WithMany()
                 .HasForeignKey(x => x.JobId)
                 .OnDelete(DeleteBehavior.Cascade);

                // A user can save a given job only once
                e.HasIndex(x => new { x.JobSeekerId, x.JobId }).IsUnique();
            });


        }
    }

    public enum PremiumLevel
    {
        [Display(Name = "Normal")]
        Normal = 0,
        [Display(Name = "Basic")]
        Basic = 1,
        [Display(Name = "Premium")]
        Premium = 2
    }

    // 6. Premium Plan Configuration
    public static class PremiumPlans
    {
        public static readonly Dictionary<string, PremiumPlanInfo> Plans = new()
        {
            ["Normal"] = new PremiumPlanInfo
            {
                Name = "Normal",
                JobPostLimit = 3,
                Price = 0,
                Duration = TimeSpan.FromDays(30),
                Features = new[] { "3 job posts per month", "Basic support" }
            },
            ["Basic"] = new PremiumPlanInfo
            {
                Name = "Basic",
                JobPostLimit = 10,
                Price = 500,
                Duration = TimeSpan.FromDays(30),
                Features = new[] { "10 job posts per month", "Priority support", "Enhanced job visibility" }
            },
            ["Premium"] = new PremiumPlanInfo
            {
                Name = "Premium",
                JobPostLimit = int.MaxValue,
                Price = 1500,
                Duration = TimeSpan.FromDays(30),
                Features = new[] { "Unlimited job posts", "24/7 premium support", "Advanced analytics", "Featured job listings" }
            }
        };
    }

    public class PremiumPlanInfo
    {
        public string Name { get; set; }
        public int JobPostLimit { get; set; }
        public decimal Price { get; set; }
        public TimeSpan Duration { get; set; }
        public string[] Features { get; set; }
    }

    // Base user class with common properties
    public class UserBase
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(50)]
        public string Username { get; set; }

        [Required]
        public string Password { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; }

        [MaxLength(10)]
        public string Gender { get; set; }

        [Required, MaxLength(100)]
        public string Email { get; set; }

        [Required, MaxLength(20)]
        public string Role { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? ProfilePhotoFileName { get; set; }

        public DateTime CreatedDate { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsEmailVerified { get; set; } = false;

        public int FailedLoginAttempts { get; set; } = 0;

        [MaxLength(100)]
        public string? PasswordResetToken { get; set; }

        public DateTime? PasswordResetTokenExpire { get; set; }

        // NEW PREMIUM FIELDS
        [MaxLength(20)]
        public string PremiumLevel { get; set; } = "Normal"; // Normal, Basic, Premium

        public DateTime? PremiumStartDate { get; set; }

        public DateTime? PremiumEndDate { get; set; }

        public int JobPostsUsed { get; set; } = 0; // Track current month usage

        public DateTime? LastJobPostReset { get; set; } // Track when to reset monthly limit

        public string? StripeCustomerId { get; set; } // For Stripe integration

        public string? StripeSubscriptionId { get; set; } // For subscription management

        public UserProfile? UserProfile { get; set; }
    }

    // 2. Add new Subscription entity
    public class Subscription
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [MaxLength(10)]
        public string UserId { get; set; }
        public UserBase User { get; set; }

        [Required, MaxLength(20)]
        public string PlanType { get; set; } // Basic, Premium

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(100)]
        public string? StripeSessionId { get; set; }

        [MaxLength(100)]
        public string? StripePaymentIntentId { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }

    // 3. Add new Payment entity
    public class Payment
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [MaxLength(10)]
        public string UserId { get; set; }
        public UserBase User { get; set; }

        [MaxLength(10)]
        public string? SubscriptionId { get; set; }
        public Subscription? Subscription { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        [MaxLength(20)]
        public string PaymentMethod { get; set; } = "Stripe";

        [MaxLength(50)]
        public string Status { get; set; } // Pending, Completed, Failed, Refunded

        [MaxLength(100)]
        public string? StripePaymentIntentId { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
    }

    public class Admin : UserBase
    {
        [MaxLength(50)]
        public string? Department { get; set; }

        [MaxLength(100)]
        public string? Position { get; set; }

        public string Permissions { get; set; } = "Full";
        public DateTime LastLoginDate { get; set; }
        public int LoginCount { get; set; } = 0;

        [MaxLength(10)]
        public string? SupervisorId { get; set; }
        public Admin? Supervisor { get; set; }
        public List<Admin> Subordinates { get; set; } = new();
    }

    public class Employer : UserBase
    {
        [Required, MaxLength(100)]
        public string CompanyName { get; set; }

        [MaxLength(200)]
        public string? CompanyAddress { get; set; }

        public string? CompanyDescription { get; set; }

        [MaxLength(100)]
        public string? Website { get; set; }

        [MaxLength(50)]
        public string? Industry { get; set; }

        [MaxLength(20)]
        public string ApprovalStatus { get; set; } = "Approved";

        [MaxLength(10)]
        public string? ApprovedByAdminId { get; set; }
        public Admin? ApprovedByAdmin { get; set; }

        public List<Job> Jobs { get; set; } = new();
        public List<Review> Reviews { get; set; } = new();
        public List<Question> Questions { get; set; } = new();
        public List<QuestionSet> QuestionSets { get; set; } = new();

    }

    public class JobSeeker : UserBase
    {
        [MaxLength(200)] public string? Address { get; set; }
        public string? ResumeFileName { get; set; }
        [MaxLength(50)] public string? ExperienceLevel { get; set; }
        public string? Summary { get; set; }
        public List<WorkExperience> Experiences { get; set; } = new();
        public List<Education> Educations { get; set; } = new();
        public List<Application> Applications { get; set; } = new();
        public List<Review> Reviews { get; set; } = new();
        public List<Report> Reports { get; set; } = new();
        public List<JobSeekerSkill> Skills { get; set; } = new();
        public List<Language> Languages { get; set; } = new();
        public List<License> Licenses { get; set; } = new();
    }

    public class CompanyPhoto
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        [Required, MaxLength(255)]
        public string FileName { get; set; }

        [MaxLength(10)]
        public string FileType { get; set; }

        public long FileSize { get; set; }

        [MaxLength(50)]
        public string PhotoType { get; set; } // "Logo", "Cover", "Gallery"

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime UploadDate { get; set; } = DateTime.UtcNow;

        [MaxLength(255)]
        public string Caption { get; set; }
    }

    public class Job
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(120)]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public string Location { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal MinSalary { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal MaxSalary { get; set; }

        [Required]
        public JobType JobType { get; set; }

        [Required]
        public JobStatus Status { get; set; }

        public DateTime PostedDate { get; set; } = DateTime.UtcNow;
        public DateTime? ClosingDate { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public string? CategoryId { get; set; }
        public JobCategory? Category { get; set; }

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        [MaxLength(10)]
        public string? QuestionSetId { get; set; }
        public QuestionSet? QuestionSet { get; set; }

        public bool IsActive { get; set; } = true;

        public List<Application> Applications { get; set; } = new();
        public List<Question> Questions { get; set; } = new();

        // Navigation to JobReports
        public List<JobReport> JobReports { get; set; } = new();
    }

    public class Application
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        public DateTime AppliedDate { get; set; }

        public ApplicationStatusEnum Status { get; set; }

        public string? CoverLetter { get; set; }

        public string? ResumeFileName { get; set; }

        [MaxLength(10)]
        public string JobId { get; set; }
        public Job Job { get; set; }

        [MaxLength(10)]
        public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }

        public DateTime? InterviewDate { get; set; }
        public DateTime? InterviewEndDate { get; set; }
        public string? InterviewLocation { get; set; }
        public string? InterviewNotes { get; set; }
        public string? InterviewerInfo { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancellationDate { get; set; }

        public List<QuestionResponse> QuestionResponses { get; set; } = new();
    }

    public class Review
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required]
        public string Content { get; set; }

        // Add these new properties for reply functionality
        public string? EmployerReply { get; set; }

        public DateTime? ReplyDate { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Range(1, 5)]
        public int Rating { get; set; } = 5;

        [MaxLength(10)]
        public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }
    }

    public class Report
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required]
        public string Reason { get; set; }

        public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }

        public string EmployerId { get; set; }
        public Employer Employer { get; set; }
    }

    public class Question
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required]
        public string Text { get; set; }

        [Required]
        public QuestionType Type { get; set; } = QuestionType.Text;

        public bool IsRequired { get; set; } = true;

        public string? Options { get; set; }

        public int? MaxLength { get; set; }

        public int Order { get; set; }

        [MaxLength(10)]
        public string QuestionSetId { get; set; }
        public QuestionSet QuestionSet { get; set; }

        [MaxLength(10)]
        public string? JobId { get; set; }
        public Job? Job { get; set; }

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        public List<QuestionResponse> Responses { get; set; } = new();
    }

    public class QuestionResponse
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required]
        public string Answer { get; set; }

        public DateTime ResponseDate { get; set; } = DateTime.UtcNow;

        [MaxLength(10)]
        public string QuestionId { get; set; }
        public Question Question { get; set; }

        [MaxLength(10)]
        public string ApplicationId { get; set; }
        public Application Application { get; set; }

        [MaxLength(10)]
        public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }
    }

    public class QuestionSet
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; }

        public string Description { get; set; }

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        public List<Question> Questions { get; set; } = new();
        public List<Job> Jobs { get; set; } = new();
    }

    public enum QuestionType
    {
        [Display(Name = "Text")]
        Text = 1,
        [Display(Name = "Multiple Choice")]
        MultipleChoice = 2,
        [Display(Name = "Checkbox")]
        Checkbox = 3,
        [Display(Name = "Dropdown")]
        Dropdown = 4,
        [Display(Name = "Text Area")]
        TextArea = 5,
        [Display(Name = "File Upload")]
        FileUpload = 6,
        [Display(Name = "Date")]
        Date = 7
    }

    public class JobCategory
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; }
        public List<Job> Jobs { get; set; } = new();
    }

    public class JobSeekerSkill
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [MaxLength(10)]
        public string JobSeekerId { get; set; }

        [MaxLength(100)]
        public string SkillName { get; set; }

        public JobSeeker JobSeeker { get; set; }
    }

    public enum JobType
    {
        [Display(Name = "Full Time")]
        FullTime,
        [Display(Name = "Part Time")]
        PartTime,
        [Display(Name = "Contract")]
        Contract,
        [Display(Name = "Internship")]
        Internship,
        [Display(Name = "Freelance")]
        Freelance
    }

    public enum ApplicationStatusEnum
    {
        Pending = 1,
        Shortlisted,
        InterviewScheduled,
        OfferSent,
        Hired,
        Rejected,
    }

    public enum JobStatus
    {
        [Display(Name = "Open")]
        Open,
        [Display(Name = "Closed")]
        Closed,
        [Display(Name = "Draft")]
        Draft,
    }

    public abstract class ReportBase
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required]
        public string Reason { get; set; }

        public DateTime DateReported { get; set; }
    }

    public class EmployerReport : ReportBase
    {
        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        [MaxLength(10)]
        public string ReporterId { get; set; }
        public UserBase Reporter { get; set; }
    }

    public class UserReport : ReportBase
    {
        [MaxLength(10)]
        public string ReportedUserId { get; set; }
        public UserBase ReportedUser { get; set; }

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }
    }

    public class AdminLog
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [MaxLength(10)]
        public string AdminId { get; set; }
        public Admin Admin { get; set; }

        [Required, MaxLength(100)]
        public string Action { get; set; }

        public string Details { get; set; }

        public DateTime Timestamp { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }
    }

    public class UserProfile
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [MaxLength(10)]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public UserBase User { get; set; }

        [MaxLength(50)]
        public string GeneratedUserId { get; set; }

        public string ProfilePicturePath { get; set; }
        public DateTime RegistrationDate { get; set; }
        public bool EmailVerified { get; set; }

        [MaxLength(100)]
        public string EmailVerificationToken { get; set; }

        [MaxLength(20)]
        public string AccountStatus { get; set; }

        public DateTime? LastLogin { get; set; }
        public int LoginAttempts { get; set; }
        public DateTime? LockedUntil { get; set; }
    }

    public class UserIdCounter
    {
        [Key, MaxLength(20)]
        public string UserType { get; set; }

        public int LastNumber { get; set; }
    }

    public class JobReport
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(10)]
        public string JobId { get; set; }
        public Job Job { get; set; }

        [Required]
        public string Reason { get; set; }

        public DateTime DateReported { get; set; } = DateTime.UtcNow;
    }

    public class CompanyFeature
    {
        [Key, MaxLength(10)]
        public string Id { get; set; }

        [Required, MaxLength(100)]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        [MaxLength(50)]
        public string Icon { get; set; } // e.g., "bi-building", "bi-people", etc.

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(10)]
        public string EmployerId { get; set; }
        public Employer Employer { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }


    public class WorkExperience
    {
        [Key, MaxLength(10)] public string Id { get; set; }
        [MaxLength(10)] public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }
        [Required, MaxLength(120)] public string Role { get; set; }
        [MaxLength(120)] public string? Company { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Description { get; set; }
    }

    public class Education
    {
        [Key, MaxLength(10)] public string Id { get; set; }
        [MaxLength(10)] public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }
        [Required, MaxLength(120)] public string School { get; set; }
        [MaxLength(120)] public string? Degree { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Description { get; set; }
    }

    [Table("Languages")]
    public class Language
    {
        [Key, MaxLength(50)]
        public string Id { get; set; }

        [MaxLength(10)] // Changed from 50 to 10
        public string JobSeekerId { get; set; }

        public JobSeeker JobSeeker { get; set; }

        // maps to column "Language"
        [Required, MaxLength(80)]
        [Column("Language")]
        public string Name { get; set; }

        [MaxLength(40)]
        public string? Proficiency { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class LanguageOption
    {
        [Key, MaxLength(8)] public string Code { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // License 
    public class License
    {
        [Key, MaxLength(10)] public string Id { get; set; }
        [MaxLength(10)] public string JobSeekerId { get; set; }
        public JobSeeker JobSeeker { get; set; }

        [Required, MaxLength(150)] public string Title { get; set; }
        [MaxLength(150)] public string? Issuer { get; set; }
        public DateTime? IssuedDate { get; set; }
        public DateTime? ExpiresDate { get; set; }
        [MaxLength(300)] public string? CredentialUrl { get; set; }
    }

    public class SavedJob
    {
        [Key, MaxLength(10)] public string Id { get; set; } = default!;

        [MaxLength(10)] public string JobSeekerId { get; set; } = default!;
        public JobSeeker JobSeeker { get; set; } = default!;

        [MaxLength(10)] public string JobId { get; set; } = default!;
        public Job Job { get; set; } = default!;

        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    }
}