using JobRecruitment.Models;
using Microsoft.EntityFrameworkCore;

namespace JobRecruitment.Services
{
    public interface IPremiumService
    {
        Task<bool> CanPostJobAsync(string userId);
        Task<int> GetRemainingJobPostsAsync(string userId);
        Task<PremiumPlanInfo> GetCurrentPlanAsync(string userId);
        Task IncrementJobPostCountAsync(string userId);
        Task<bool> UpgradePlanAsync(string userId, string planType, string paymentIntentId, string sessionId = null);
        Task ResetMonthlyLimitsAsync();
        Task<UserPremiumStatus> GetUserPremiumStatusAsync(string userId);
    }

    public class PremiumService : IPremiumService
    {
        private readonly DB _db;
        private readonly ILogger<PremiumService> _logger;

        public PremiumService(DB db, ILogger<PremiumService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<bool> CanPostJobAsync(string userId)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return false;

                // Check if monthly limit needs reset
                await CheckAndResetMonthlyLimitAsync(user);

                var plan = PremiumPlans.Plans[user.PremiumLevel];
                return user.JobPostsUsed < plan.JobPostLimit;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking job post limit for user {UserId}", userId);
                return false;
            }
        }

        public async Task<int> GetRemainingJobPostsAsync(string userId)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return 0;

                await CheckAndResetMonthlyLimitAsync(user);

                var plan = PremiumPlans.Plans[user.PremiumLevel];
                if (plan.JobPostLimit == int.MaxValue) return int.MaxValue;

                return Math.Max(0, plan.JobPostLimit - user.JobPostsUsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting remaining job posts for user {UserId}", userId);
                return 0;
            }
        }

        public async Task<PremiumPlanInfo> GetCurrentPlanAsync(string userId)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return PremiumPlans.Plans["Normal"];

                return PremiumPlans.Plans[user.PremiumLevel];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current plan for user {UserId}", userId);
                return PremiumPlans.Plans["Normal"];
            }
        }

        public async Task IncrementJobPostCountAsync(string userId)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return;

                await CheckAndResetMonthlyLimitAsync(user);

                user.JobPostsUsed++;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Incremented job post count for user {UserId}. Current count: {Count}",
                    userId, user.JobPostsUsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error incrementing job post count for user {UserId}", userId);
            }
        }

        public async Task<bool> UpgradePlanAsync(string userId, string planType, string paymentIntentId, string sessionId = null)
        {
            try
            {
                if (!PremiumPlans.Plans.ContainsKey(planType))
                {
                    _logger.LogWarning("Invalid plan type: {PlanType}", planType);
                    return false;
                }

                var user = await _db.Users.FindAsync(userId);
                if (user == null) return false;

                var plan = PremiumPlans.Plans[planType];
                var startDate = DateTime.UtcNow;
                var endDate = startDate.Add(plan.Duration);

                // Update user premium status
                user.PremiumLevel = planType;
                user.PremiumStartDate = startDate;
                user.PremiumEndDate = endDate;
                user.JobPostsUsed = 0; // Reset usage when upgrading
                user.LastJobPostReset = startDate;

                // Create subscription record
                var subscription = new Subscription
                {
                    Id = GenerateSubscriptionId(),
                    UserId = userId,
                    PlanType = planType,
                    Amount = plan.Price,
                    StartDate = startDate,
                    EndDate = endDate,
                    IsActive = true,
                    StripeSessionId = sessionId,
                    StripePaymentIntentId = paymentIntentId
                };

                // Create payment record
                var payment = new Payment
                {
                    Id = GeneratePaymentId(),
                    UserId = userId,
                    SubscriptionId = subscription.Id,
                    Amount = plan.Price,
                    Status = "Completed",
                    StripePaymentIntentId = paymentIntentId,
                    PaymentDate = DateTime.UtcNow,
                    PaymentMethod = "Stripe Checkout"
                };

                _db.Users.Update(user);
                _db.Subscriptions.Add(subscription);
                _db.Payments.Add(payment);

                await _db.SaveChangesAsync();

                _logger.LogInformation("Successfully upgraded user {UserId} to {PlanType}", userId, planType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upgrading plan for user {UserId}", userId);
                return false;
            }
        }

        public async Task ResetMonthlyLimitsAsync()
        {
            try
            {
                var usersToReset = await _db.Users
                    .Where(u => u.LastJobPostReset.HasValue &&
                               u.LastJobPostReset.Value.AddDays(30) <= DateTime.UtcNow)
                    .ToListAsync();

                foreach (var user in usersToReset)
                {
                    user.JobPostsUsed = 0;
                    user.LastJobPostReset = DateTime.UtcNow;
                }

                if (usersToReset.Any())
                {
                    _db.Users.UpdateRange(usersToReset);
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Reset monthly limits for {Count} users", usersToReset.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting monthly limits");
            }
        }

        public async Task<UserPremiumStatus> GetUserPremiumStatusAsync(string userId)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null)
                {
                    return new UserPremiumStatus
                    {
                        CurrentPlan = "Normal",
                        JobPostsUsed = 0,
                        JobPostsRemaining = 3,
                        IsActive = true,
                        ExpiryDate = null,
                        PlanInfo = PremiumPlans.Plans["Normal"]
                    };
                }

                await CheckAndResetMonthlyLimitAsync(user);

                var plan = PremiumPlans.Plans[user.PremiumLevel];
                var remaining = plan.JobPostLimit == int.MaxValue ?
                    int.MaxValue : Math.Max(0, plan.JobPostLimit - user.JobPostsUsed);

                return new UserPremiumStatus
                {
                    CurrentPlan = user.PremiumLevel,
                    JobPostsUsed = user.JobPostsUsed,
                    JobPostsRemaining = remaining,
                    IsActive = !user.PremiumEndDate.HasValue || user.PremiumEndDate > DateTime.UtcNow,
                    ExpiryDate = user.PremiumEndDate,
                    PlanInfo = plan
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting premium status for user {UserId}", userId);
                return new UserPremiumStatus
                {
                    CurrentPlan = "Normal",
                    JobPostsUsed = 0,
                    JobPostsRemaining = 3,
                    IsActive = true,
                    ExpiryDate = null,
                    PlanInfo = PremiumPlans.Plans["Normal"]
                };
            }
        }

        private async Task CheckAndResetMonthlyLimitAsync(UserBase user)
        {
            if (!user.LastJobPostReset.HasValue)
            {
                user.LastJobPostReset = DateTime.UtcNow;
                user.JobPostsUsed = 0;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();
                return;
            }

            // Reset if 30 days have passed
            if (user.LastJobPostReset.Value.AddDays(30) <= DateTime.UtcNow)
            {
                user.JobPostsUsed = 0;
                user.LastJobPostReset = DateTime.UtcNow;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Reset monthly limit for user {UserId}", user.Id);
            }
        }

        private string GenerateSubscriptionId()
        {
            var lastId = _db.Subscriptions
                .Where(s => s.Id.StartsWith("SUB") && s.Id.Length == 10)
                .OrderByDescending(s => s.Id)
                .Select(s => s.Id)
                .FirstOrDefault();

            if (lastId == null) return "SUB0000001";

            var numericPart = int.Parse(lastId.Substring(3));
            return $"SUB{(numericPart + 1):D7}";
        }

        private string GeneratePaymentId()
        {
            var lastId = _db.Payments
                .Where(p => p.Id.StartsWith("PAY") && p.Id.Length == 10)
                .OrderByDescending(p => p.Id)
                .Select(p => p.Id)
                .FirstOrDefault();

            if (lastId == null) return "PAY0000001";

            var numericPart = int.Parse(lastId.Substring(3));
            return $"PAY{(numericPart + 1):D7}";
        }
    }

    public class UserPremiumStatus
    {
        public string CurrentPlan { get; set; }
        public int JobPostsUsed { get; set; }
        public int JobPostsRemaining { get; set; }
        public bool IsActive { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public PremiumPlanInfo PlanInfo { get; set; }
    }
}