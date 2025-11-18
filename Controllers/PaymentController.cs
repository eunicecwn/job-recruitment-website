using JobRecruitment.Models;
using JobRecruitment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace JobRecruitment.Controllers
{
    [Authorize]
    public class PremiumController : Controller
    {
        private readonly DB _db;
        private readonly IPremiumService _premiumService;
        private readonly IConfiguration _config;
        private readonly ILogger<PremiumController> _logger;

        public PremiumController(
            DB db,
            IPremiumService premiumService,
            IConfiguration config,
            ILogger<PremiumController> logger)
        {
            _db = db;
            _premiumService = premiumService;
            _config = config;
            _logger = logger;

            // Configure Stripe API Key
            var secretKey = _config["Stripe:SecretKey"];
            if (string.IsNullOrEmpty(secretKey))
            {
                _logger.LogError("Stripe SecretKey is not configured");
            }
            else
            {
                StripeConfiguration.ApiKey = secretKey;
            }
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

        // Display upgrade options
        public async Task<IActionResult> Upgrade()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var premiumStatus = await _premiumService.GetUserPremiumStatusAsync(userId);

            ViewBag.CurrentStatus = premiumStatus;
            ViewBag.PublishableKey = _config["Stripe:PublishableKey"];

            return View();
        }

        // Display current subscription status
        public async Task<IActionResult> Status()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var premiumStatus = await _premiumService.GetUserPremiumStatusAsync(userId);

            // Get payment history
            var payments = await _db.Payments
                .Where(p => p.UserId == userId)
                .Include(p => p.Subscription)
                .OrderByDescending(p => p.PaymentDate)
                .Take(10)
                .ToListAsync();

            ViewBag.PaymentHistory = payments;

            return View(premiumStatus);
        }

        // Updated CreateCheckoutSession method that handles both JSON and Form data
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCheckoutSession(string planType)
        {
            try
            {
                // Log the incoming request
                _logger.LogInformation("CreateCheckoutSession called with planType: {PlanType}", planType);

                // Validate Stripe configuration
                var secretKey = _config["Stripe:SecretKey"];
                if (string.IsNullOrEmpty(secretKey))
                {
                    _logger.LogError("Stripe SecretKey is not configured");
                    return Json(new { success = false, error = "Payment system not configured" });
                }

                // Ensure Stripe API key is set
                StripeConfiguration.ApiKey = secretKey;

                if (string.IsNullOrEmpty(planType))
                {
                    _logger.LogWarning("PlanType is null or empty");
                    return Json(new { success = false, error = "Plan type is required" });
                }

                if (!PremiumPlans.Plans.ContainsKey(planType))
                {
                    _logger.LogWarning("Invalid plan type: {PlanType}", planType);
                    return Json(new { success = false, error = "Invalid plan type" });
                }

                var plan = PremiumPlans.Plans[planType];
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User not authenticated");
                    return Json(new { success = false, error = "User not authenticated" });
                }

                var user = await _db.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    return Json(new { success = false, error = "User not found" });
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                _logger.LogInformation("Base URL: {BaseUrl}", baseUrl);

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(plan.Price * 100), // Convert to cents
                        Currency = "myr",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"{plan.Name} Plan",
                            Description = $"HireRightPro {plan.Name} Plan - {string.Join(", ", plan.Features.Take(2))}",
                            Images = new List<string>
                            {
                                $"{baseUrl}/images/logo.png"
                            }
                        }
                    },
                    Quantity = 1
                }
            },
                    Mode = "payment",
                    SuccessUrl = $"{baseUrl}/Premium/PaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{baseUrl}/Premium/Upgrade?cancelled=true",
                    CustomerEmail = user.Email,
                    Metadata = new Dictionary<string, string>
                    {
                        ["userId"] = userId,
                        ["planType"] = planType,
                        ["userName"] = user.FullName ?? "Unknown"
                    },
                    BillingAddressCollection = "required",
                    PaymentIntentData = new SessionPaymentIntentDataOptions
                    {
                        Metadata = new Dictionary<string, string>
                        {
                            ["userId"] = userId,
                            ["planType"] = planType
                        }
                    }
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                _logger.LogInformation("Created Stripe Checkout session {SessionId} for user {UserId} plan {PlanType}",
                    session.Id, userId, planType);

                return Json(new
                {
                    success = true,
                    sessionId = session.Id,
                    checkoutUrl = session.Url
                });
            }
            catch (StripeException stripeEx)
            {
                _logger.LogError(stripeEx, "Stripe API error creating checkout session for user {UserId}: {Error}",
                    User.FindFirstValue(ClaimTypes.NameIdentifier), stripeEx.Message);
                return Json(new { success = false, error = $"Payment system error: {stripeEx.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Stripe Checkout session for user {UserId}",
                    User.FindFirstValue(ClaimTypes.NameIdentifier));
                return Json(new { success = false, error = "Payment initialization failed" });
            }
        }

        // Handle successful payment return from Stripe
        public async Task<IActionResult> PaymentSuccess(string session_id)
        {
            if (string.IsNullOrEmpty(session_id))
            {
                TempData["ErrorMessage"] = "Invalid payment session.";
                return RedirectToAction("Upgrade");
            }

            try
            {
                var sessionService = new SessionService();
                var session = await sessionService.GetAsync(session_id);

                if (session.PaymentStatus != "paid")
                {
                    TempData["ErrorMessage"] = "Payment was not completed successfully.";
                    return RedirectToAction("Upgrade");
                }

                // Extract metadata
                var userId = session.Metadata["userId"];
                var planType = session.Metadata["planType"];
                var userName = session.Metadata["userName"];

                // Get the payment intent
                var paymentIntent = session.PaymentIntentId;

                // Upgrade the user's plan
                var success = await _premiumService.UpgradePlanAsync(userId, planType, paymentIntent, session_id);

                if (success)
                {
                    _logger.LogInformation("Successfully upgraded user {UserId} to {PlanType} plan via Checkout",
                        userId, planType);

                    // Create receipt data
                    var receiptData = new PaymentReceiptViewModel
                    {
                        SessionId = session_id,
                        PaymentIntentId = paymentIntent,
                        PlanName = planType,
                        Amount = (decimal)session.AmountTotal / 100m, // Convert back from cents
                        Currency = session.Currency.ToUpper(),
                        CustomerName = userName,
                        CustomerEmail = session.CustomerDetails?.Email,
                        PaymentDate = DateTime.UtcNow,
                        PaymentMethod = "Card",
                        Status = "Completed"
                    };

                    return View("PaymentReceipt", receiptData);
                }
                else
                {
                    _logger.LogError("Failed to upgrade user {UserId} to {PlanType} plan after successful payment",
                        userId, planType);
                    TempData["ErrorMessage"] = "Payment was successful, but there was an error upgrading your account. Please contact support.";
                    return RedirectToAction("Status");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing successful payment for session {SessionId}", session_id);
                TempData["ErrorMessage"] = "There was an error processing your payment. Please contact support.";
                return RedirectToAction("Upgrade");
            }
        }

        // Cancel subscription (downgrade to normal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelSubscription()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _db.Users.FindAsync(userId);

                if (user == null)
                {
                    return Json(new { success = false, error = "User not found" });
                }

                // Reset to normal plan
                user.PremiumLevel = "Normal";
                user.PremiumStartDate = null;
                user.PremiumEndDate = null;
                user.JobPostsUsed = 0;
                user.LastJobPostReset = DateTime.UtcNow;

                // Deactivate current subscription
                var activeSubscription = await _db.Subscriptions
                    .Where(s => s.UserId == userId && s.IsActive)
                    .FirstOrDefaultAsync();

                if (activeSubscription != null)
                {
                    activeSubscription.IsActive = false;
                    _db.Subscriptions.Update(activeSubscription);
                }

                _db.Users.Update(user);
                await _db.SaveChangesAsync();

                _logger.LogInformation("User {UserId} cancelled their subscription", userId);

                TempData["InfoMessage"] = "Your subscription has been cancelled. Your account has been downgraded to Normal plan.";
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling subscription for user {UserId}",
                    User.FindFirstValue(ClaimTypes.NameIdentifier));
                return Json(new { success = false, error = "Failed to cancel subscription" });
            }
        }

        public IActionResult Receipt(string paymentIntentId)
        {
            var model = new PaymentReceiptViewModel
            {
                PaymentIntentId = paymentIntentId ?? Guid.NewGuid().ToString("N").Substring(0, 10),
                PaymentDate = DateTime.Now,
                PaymentMethod = "Credit Card (Visa)",
                Status = "Completed",
                CustomerName = "John Doe",
                CustomerEmail = "johndoe@example.com",
                PlanName = "Premium",
                Currency = "USD",
                Amount = 99.99m
            };

            return View("PaymentReceipt", model);
        }

        // Get current premium status (AJAX)
        [HttpGet]
        public async Task<IActionResult> GetStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var status = await _premiumService.GetUserPremiumStatusAsync(userId);

            return Json(new
            {
                currentPlan = status.CurrentPlan,
                jobPostsUsed = status.JobPostsUsed,
                jobPostsRemaining = status.JobPostsRemaining == int.MaxValue ? "Unlimited" : status.JobPostsRemaining.ToString(),
                isActive = status.IsActive,
                expiryDate = status.ExpiryDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                planInfo = new
                {
                    name = status.PlanInfo.Name,
                    price = status.PlanInfo.Price,
                    jobPostLimit = status.PlanInfo.JobPostLimit == int.MaxValue ? "Unlimited" : status.PlanInfo.JobPostLimit.ToString(),
                    features = status.PlanInfo.Features
                }
            });
        }
    }

    // Request models
    public class CreatePaymentRequest
    {
        public string PlanType { get; set; }
    }

    // Receipt view model
    public class PaymentReceiptViewModel
    {
        public string SessionId { get; set; }
        public string PaymentIntentId { get; set; }
        public string PlanName { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; }
        public string Status { get; set; }
    }
}