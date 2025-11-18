using Demo.Models.AccountViewModels;
using Demo.Services;
using JobRecruitment.Services;
using JobRecruitment.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

namespace JobRecruitment.Controllers
{
    public class AccountController : BaseController
    {
        private readonly DB db;
        private readonly ILogger<AccountController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _emailSender;
        private readonly IUserIdService _userIdService;
        private readonly IMemoryCache _memoryCache;

        // Enhanced OTP storage with better concurrency handling
        private static readonly ConcurrentDictionary<string, OtpData> _otpStorage = new();
        private static readonly ConcurrentDictionary<string, DateTime> _otpRateLimit = new();
        private static readonly Timer _cleanupTimer;

        // Enhanced OTP data structure with more security features
        private class OtpData
        {
            public string Code { get; set; } = string.Empty;
            public DateTime Expiry { get; set; }
            public bool IsVerified { get; set; }
            public int Attempts { get; set; }
            public DateTime CreatedAt { get; set; }
            public string? IpAddress { get; set; }
            public string? UserAgent { get; set; }
        }

        static AccountController()
        {
            // Cleanup expired OTP data every 5 minutes
            _cleanupTimer = new Timer(CleanupExpiredOtpData, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public AccountController(DB context, ILogger<AccountController> logger, IConfiguration configuration, IEmailSender emailSender, IUserIdService userIdService, IMemoryCache memoryCache) : base(context)
        {
            db = context;
            _logger = logger;
            _configuration = configuration;
            _emailSender = emailSender;
            _userIdService = userIdService;
            _memoryCache = memoryCache;
        }

        // GET: Register
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToDashboard();
            }

            ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
            return View(new RegisterViewModel());
        }

        // POST: Register - Enhanced with better error handling and logging
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var clientIp = GetClientIpAddress();

            try
            {
                ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
                _logger.LogInformation("Registration attempt started for email: {Email} from IP: {IP}", model?.Email, clientIp);

                // ADD THIS DEBUGGING SECTION
                _logger.LogInformation("Model values - AccountType: '{AccountType}', CompanyName: '{CompanyName}', HasEmailVerificationToken: {HasToken}",
                    model?.AccountType, model?.CompanyName, !string.IsNullOrEmpty(model?.EmailVerificationToken));

                // Log all form data received
                _logger.LogInformation("All form fields: {FormData}",
                    string.Join(", ", Request.Form.Select(f => $"{f.Key}={f.Value}")));

                // FIXED: Clear CompanyName validation for JobSeekers BEFORE other validations
                if (model.AccountType == "JobSeeker")
                {
                    model.CompanyName = string.Empty;

                    // Remove CompanyName from ModelState if it exists and has errors
                    if (ModelState.ContainsKey("CompanyName"))
                    {
                        ModelState.Remove("CompanyName");
                        _logger.LogInformation("CompanyName validation cleared for JobSeeker account type");
                    }
                }

                // Check ModelState for CompanyName specifically
                if (ModelState.ContainsKey("CompanyName"))
                {
                    var companyNameState = ModelState["CompanyName"];
                    _logger.LogInformation("CompanyName ModelState - Errors: {Errors}, Value: '{Value}'",
                        string.Join("; ", companyNameState.Errors.Select(e => e.ErrorMessage)),
                        companyNameState.AttemptedValue);
                }

                // Enhanced email verification check - UPDATED LOGIC
                bool emailVerificationBypassed = false;
                if (!string.IsNullOrEmpty(model.Email))
                {
                    // Check if user completed email verification (has verification token)
                    bool hasVerificationToken = !string.IsNullOrEmpty(model.EmailVerificationToken) &&
                                              model.EmailVerificationToken.StartsWith("verified_");

                    // Check if email is verified via OTP storage
                    bool isEmailVerified = IsEmailVerified(model.Email);

                    if (hasVerificationToken && isEmailVerified)
                    {
                        // Email was verified - bypass OTP flow
                        emailVerificationBypassed = true;
                        _logger.LogInformation("Email verification bypassed for {Email} - verification token present", model.Email);
                    }
                    else if (!hasVerificationToken && !isEmailVerified)
                    {
                        // Need email verification
                        ModelState.AddModelError("Email", "Please verify your email address before submitting the form.");
                        model.IsOtpSectionVisible = true;
                        model.OtpMessage = "Please verify your email address first.";
                        model.IsOtpSuccess = false;

                        // Try to send OTP automatically with better error handling
                        try
                        {
                            var otpResult = await SendOtpToEmailEnhanced(model.Email);
                            if (otpResult.success)
                            {
                                model.IsEmailSent = true;
                                model.OtpMessage = "Verification code sent to your email.";
                                model.IsOtpSuccess = true;
                                model.CanResendOtp = false;
                                model.ResendCountdownSeconds = 60;
                                _logger.LogInformation("OTP sent successfully to {Email}", model.Email);
                            }
                            else
                            {
                                model.OtpMessage = otpResult.message;
                                model.IsOtpSuccess = false;
                                _logger.LogWarning("OTP sending failed for {Email}: {Message}", model.Email, otpResult.message);
                            }
                        }
                        catch (Exception otpEx)
                        {
                            _logger.LogError(otpEx, "Exception occurred while sending OTP to {Email}", model.Email);
                            model.OtpMessage = "Failed to send verification code. Please try again.";
                            model.IsOtpSuccess = false;
                        }
                    }
                }

                // UPDATED: Enhanced server-side validation with debugging
                await ValidateRegistrationModelEnhanced(model);

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    _logger.LogInformation("Model validation failed for {Email}. Errors: {Errors}",
                        model?.Email, string.Join(", ", errors));

                    // ADD SPECIFIC DEBUGGING FOR COMPANY NAME VALIDATION
                    if (ModelState.ContainsKey("CompanyName") && ModelState["CompanyName"].Errors.Any())
                    {
                        _logger.LogWarning("CompanyName validation failed for AccountType '{AccountType}'. User selected: {UserAccountType}",
                            model?.AccountType,
                            Request.Form.ContainsKey("AccountType") ? Request.Form["AccountType"].ToString() : "NOT_SET");
                    }

                    // Preserve form data for session-only restoration
                    await PreserveFormData(model);
                    return View(model);
                }

                // UPDATED: Skip reCAPTCHA validation if email verification was completed
                if (!emailVerificationBypassed)
                {
                    // Validate reCAPTCHA with better error handling
                    if (!await ValidateRecaptchaEnhanced(model.RecaptchaResponse))
                    {
                        ModelState.AddModelError("RecaptchaResponse", "Please complete the reCAPTCHA verification.");
                        await PreserveFormData(model);
                        _logger.LogWarning("reCAPTCHA validation failed for {Email} from IP {IP}", model.Email, clientIp);
                        return View(model);
                    }
                }
                else
                {
                    _logger.LogInformation("reCAPTCHA validation skipped for {Email} - email verification completed", model.Email);
                }

                // Create user with improved transaction handling
                var (success, newUser, userProfile, errorMessage) = await CreateUserWithTransaction(model);

                if (!success)
                {
                    ModelState.AddModelError("", errorMessage ?? "Registration failed. Please try again.");
                    await PreserveFormData(model);
                    return View(model);
                }

                // Clean up OTP storage after successful registration
                CleanupOtpData(model.Email);

                stopwatch.Stop();
                _logger.LogInformation("User registered successfully: {Username} ({Role}) in {ElapsedMs}ms",
                    newUser.Username, newUser.Role, stopwatch.ElapsedMilliseconds);

                // Set success message based on account type
                TempData["Success"] = model.AccountType == "Employer"
                    ? $"Employer registration successful! Your ID is: {userProfile.GeneratedUserId}. Your account is pending admin approval."
                    : $"Registration successful! Your ID is: {userProfile.GeneratedUserId}. You can now login with your credentials.";

                // Signal to clear session storage
                ViewBag.ClearSessionStorage = true;
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Critical error during registration for {Email} from IP {IP} after {ElapsedMs}ms",
                    model?.Email, clientIp, stopwatch.ElapsedMilliseconds);

                ModelState.AddModelError("", "An unexpected error occurred during registration. Please try again.");

                if (model != null)
                {
                    await PreserveFormData(model);
                }

                return View(model ?? new RegisterViewModel());
            }
        }

        // GET: Login with enhanced security logging
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string returnUrl = null)
        {
            if (User.Identity.IsAuthenticated)
            {
                var currentRole = User.FindFirstValue(ClaimTypes.Role);
                _logger.LogInformation("Already authenticated user {Username} with role {Role} redirected from login",
                    User.Identity.Name, currentRole);
                return RedirectToDashboard();
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];

            if (TempData["Success"] != null)
            {
                ViewBag.SuccessMessage = TempData["Success"];
            }

            return View(new LoginViewModel());
        }

        // POST: Login with enhanced security features
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var clientIp = GetClientIpAddress();
            var userAgent = Request.Headers["User-Agent"].ToString();

            try
            {
                ViewData["ReturnUrl"] = returnUrl;
                ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];

                _logger.LogInformation("Login attempt for {Username} from IP {IP}", model?.Username, clientIp);

                if (!ModelState.IsValid)
                {
                    _logger.LogInformation("Login model validation failed for {Username}", model?.Username);
                    return View(model);
                }

                // Enhanced reCAPTCHA validation
                if (!await ValidateRecaptchaEnhanced(model.RecaptchaResponse))
                {
                    ModelState.AddModelError("RecaptchaResponse", "Please complete the reCAPTCHA verification.");
                    _logger.LogWarning("reCAPTCHA validation failed for login attempt {Username} from {IP}", model.Username, clientIp);
                    return View(model);
                }

                // Enhanced user lookup and validation
                var (loginSuccess, user, errorMessage) = await ValidateUserCredentials(model, clientIp, userAgent);

                if (!loginSuccess)
                {
                    ModelState.AddModelError("", errorMessage);
                    return View(model);
                }

                // Enhanced session creation
                await SignInUserEnhanced(user, model.RememberMe, clientIp, userAgent);

                stopwatch.Stop();
                _logger.LogInformation("User {Username} logged in successfully with role {Role} from IP {IP} in {ElapsedMs}ms",
                    user.Username, user.Role, clientIp, stopwatch.ElapsedMilliseconds);

                // Enhanced role-based redirection
                if (user.Role == "Admin")
                {
                    return RedirectToAction("AdminDashboard", "Admin");
                }
                else if (user.Role == "Employer")
                {
                    return RedirectToAction("EmployerDashboard", "Employer");
                }
                else if (user.Role == "JobSeeker")
                {
                    return RedirectToAction("Index", "JobSeeker");
                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToDashboard();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Critical error during login for {Username} from IP {IP} after {ElapsedMs}ms",
                    model?.Username, clientIp, stopwatch.ElapsedMilliseconds);

                ModelState.AddModelError("", "Login failed due to a system error. Please try again.");
                return View(model);
            }
        }

        // Enhanced logout with better session cleanup
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name;
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.Session.Clear();

                // Clear any cached data
                if (!string.IsNullOrEmpty(username))
                {
                    var cacheKey = $"user_session_{username}";
                    _memoryCache.Remove(cacheKey);
                }

                _logger.LogInformation("User {Username} with role {Role} logged out successfully", username, userRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout for user {Username}", username);
            }

            return RedirectToAction("Index", "Home");
        }

        // ENHANCED OTP Methods with better error handling and security
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmailOTP(string email)
        {
            var clientIp = GetClientIpAddress();

            try
            {
                _logger.LogInformation("OTP request for email {Email} from IP {IP}", email, clientIp);

                if (string.IsNullOrWhiteSpace(email) || !IsValidEmailEnhanced(email))
                {
                    _logger.LogWarning("Invalid email format in OTP request: {Email} from IP {IP}", email, clientIp);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid email address format",
                        resendCountdown = 0
                    });
                }

                var result = await SendOtpToEmailEnhanced(email);

                _logger.LogInformation("OTP send result for {Email}: Success={Success}, Message={Message}",
                    email, result.success, result.message);

                return Json(new
                {
                    success = result.success,
                    message = result.message,
                    resendCountdown = result.success ? 60 : 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in SendEmailOTP for {Email} from IP {IP}", email, clientIp);
                return Json(new
                {
                    success = false,
                    message = "An error occurred. Please try again.",
                    resendCountdown = 0
                });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult VerifyEmailOTP(string email, string otp)
        {
            var clientIp = GetClientIpAddress();

            try
            {
                _logger.LogInformation("OTP verification attempt for email {Email} from IP {IP}", email, clientIp);

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
                {
                    _logger.LogWarning("Empty email or OTP in verification request from IP {IP}", clientIp);
                    return Json(new
                    {
                        success = false,
                        message = "Email and OTP are required",
                        verified = false,
                        attemptsRemaining = 0
                    });
                }

                if (!IsValidEmailEnhanced(email))
                {
                    _logger.LogWarning("Invalid email format in OTP verification: {Email} from IP {IP}", email, clientIp);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid email format",
                        verified = false,
                        attemptsRemaining = 0
                    });
                }

                if (!IsValidOtpFormat(otp))
                {
                    _logger.LogWarning("Invalid OTP format from IP {IP}", clientIp);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid OTP format",
                        verified = false,
                        attemptsRemaining = 0
                    });
                }

                var result = VerifyOtpCodeEnhanced(email, otp, clientIp);

                _logger.LogInformation("OTP verification result for {Email}: Success={Success}, Verified={Verified}, Message={Message}",
                    email, result.Success, result.IsVerified, result.Message);

                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    verified = result.IsVerified,
                    attemptsRemaining = result.AttemptsRemaining
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in VerifyEmailOTP for {Email} from IP {IP}", email, clientIp);
                return Json(new
                {
                    success = false,
                    message = "Error verifying code. Please try again.",
                    verified = false,
                    attemptsRemaining = 0
                });
            }
        }

        // Enhanced availability check methods with caching
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> IsUsernameAvailable(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) ||
                    username.Length < 4 || username.Length > 50 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
                {
                    return Json(new { available = false, reason = "Invalid format" });
                }

                // Check cache first
                var cacheKey = $"username_check_{username.ToLowerInvariant()}";
                if (_memoryCache.TryGetValue(cacheKey, out bool cachedResult))
                {
                    return Json(new { available = !cachedResult });
                }

                var exists = await db.Users.AnyAsync(u => u.Username == username);

                // Cache result for 5 minutes
                _memoryCache.Set(cacheKey, exists, TimeSpan.FromMinutes(5));

                return Json(new { available = !exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username availability: {Username}", username);
                return Json(new { available = false, reason = "Check failed" });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> IsEmailAvailable(string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email) || !IsValidEmailEnhanced(email))
                {
                    return Json(new { available = false, reason = "Invalid format" });
                }

                // Check cache first
                var cacheKey = $"email_check_{email.ToLowerInvariant()}";
                if (_memoryCache.TryGetValue(cacheKey, out bool cachedResult))
                {
                    return Json(new { available = !cachedResult });
                }

                var exists = await db.Users.AnyAsync(u => u.Email == email);

                // Cache result for 5 minutes
                _memoryCache.Set(cacheKey, exists, TimeSpan.FromMinutes(5));

                return Json(new { available = !exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email availability: {Email}", email);
                return Json(new { available = false, reason = "Check failed" });
            }
        }

        // ENHANCED HELPER METHODS

        private async Task<(bool success, string message)> SendOtpToEmailEnhanced(string email)
        {
            try
            {
                if (string.IsNullOrEmpty(email) || !IsValidEmailEnhanced(email))
                {
                    return (false, "Invalid email address");
                }

                var emailExists = await db.Users.AnyAsync(u => u.Email == email);
                if (emailExists)
                {
                    return (false, "Email is already registered");
                }

                // Enhanced rate limiting with better thread safety
                if (_otpRateLimit.ContainsKey(email))
                {
                    var lastSent = _otpRateLimit[email];
                    var timeSinceLastSent = DateTime.UtcNow - lastSent;
                    if (timeSinceLastSent.TotalSeconds < 60)
                    {
                        var remainingSeconds = 60 - (int)timeSinceLastSent.TotalSeconds;
                        return (false, $"Please wait {remainingSeconds} seconds before requesting another code");
                    }
                }

                // Generate secure OTP
                var otp = GenerateSecureOtp();
                var expiryTime = DateTime.UtcNow.AddMinutes(5);
                var clientIp = GetClientIpAddress();
                var userAgent = Request.Headers["User-Agent"].ToString();

                var otpData = new OtpData
                {
                    Code = otp,
                    Expiry = expiryTime,
                    IsVerified = false,
                    Attempts = 0,
                    CreatedAt = DateTime.UtcNow,
                    IpAddress = clientIp,
                    UserAgent = userAgent
                };

                _otpStorage.AddOrUpdate(email, otpData, (key, oldValue) => otpData);
                _otpRateLimit.AddOrUpdate(email, DateTime.UtcNow, (key, oldValue) => DateTime.UtcNow);

                var subject = "Email Verification - HireRight Portal Registration";
                var body = CreateEnhancedOtpEmailBody(otp, email);

                await _emailSender.SendEmailAsync(email, subject, body);
                _logger.LogInformation("OTP sent to {Email} from IP {IP}", email, clientIp);

                return (true, "Verification code sent to your email address");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending OTP to {Email}", email);
                return (false, "Failed to send verification code. Please try again.");
            }
        }

        private (bool Success, string Message, bool IsVerified, int AttemptsRemaining) VerifyOtpCodeEnhanced(string email, string otp, string clientIp)
        {
            try
            {
                if (!_otpStorage.TryGetValue(email, out var otpData))
                {
                    _logger.LogWarning("OTP verification attempted for non-existent email {Email} from IP {IP}", email, clientIp);
                    return (false, "No verification code found for this email", false, 0);
                }

                if (DateTime.UtcNow > otpData.Expiry)
                {
                    _otpStorage.TryRemove(email, out _);
                    _logger.LogInformation("Expired OTP verification attempt for {Email} from IP {IP}", email, clientIp);
                    return (false, "Verification code has expired. Please request a new one.", false, 0);
                }

                if (otpData.IsVerified)
                {
                    return (true, "Email already verified", true, 0);
                }

                // Check max attempts
                if (otpData.Attempts >= 5)
                {
                    _otpStorage.TryRemove(email, out _);
                    _logger.LogWarning("Maximum OTP attempts exceeded for {Email} from IP {IP}", email, clientIp);
                    return (false, "Maximum attempts exceeded. Please request a new code.", false, 0);
                }

                if (otpData.Code != otp)
                {
                    // Increment attempts with thread-safe update
                    var updatedOtpData = new OtpData
                    {
                        Code = otpData.Code,
                        Expiry = otpData.Expiry,
                        IsVerified = false,
                        Attempts = otpData.Attempts + 1,
                        CreatedAt = otpData.CreatedAt,
                        IpAddress = otpData.IpAddress,
                        UserAgent = otpData.UserAgent
                    };

                    _otpStorage.AddOrUpdate(email, updatedOtpData, (key, oldValue) => updatedOtpData);

                    var remainingAttempts = 5 - updatedOtpData.Attempts;
                    var message = remainingAttempts > 0
                        ? $"Invalid verification code. {remainingAttempts} attempts remaining."
                        : "Invalid verification code. Maximum attempts reached.";

                    _logger.LogWarning("Invalid OTP attempt for {Email} from IP {IP}. Attempts: {Attempts}",
                        email, clientIp, updatedOtpData.Attempts);

                    return (false, message, false, remainingAttempts);
                }

                // Success - mark as verified with thread-safe update
                var verifiedOtpData = new OtpData
                {
                    Code = otpData.Code,
                    Expiry = otpData.Expiry,
                    IsVerified = true,
                    Attempts = otpData.Attempts,
                    CreatedAt = otpData.CreatedAt,
                    IpAddress = otpData.IpAddress,
                    UserAgent = otpData.UserAgent
                };

                _otpStorage.AddOrUpdate(email, verifiedOtpData, (key, oldValue) => verifiedOtpData);

                _logger.LogInformation("Email verified successfully: {Email} from IP {IP}", email, clientIp);
                return (true, "Email verified successfully! You can now complete your registration.", true, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in OTP verification for {Email} from IP {IP}", email, clientIp);
                return (false, "Error verifying code. Please try again.", false, 0);
            }
        }

        private bool IsEmailVerified(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;

            if (_otpStorage.TryGetValue(email, out var otpData))
            {
                return otpData.IsVerified && DateTime.UtcNow <= otpData.Expiry;
            }

            return false;
        }

        private static string GenerateSecureOtp()
        {
            // Use secure random number generation
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var number = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 1000000;
            return number.ToString("D6");
        }

        private string CreateEnhancedOtpEmailBody(string otp, string email)
        {
            return $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset=""utf-8"">
            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
            <title>Email Verification - HireRight Portal</title>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 0; background-color: #f8f9fa; line-height: 1.6; }}
                .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background-color: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); }}
                .header {{ text-align: center; padding: 30px 0; border-bottom: 2px solid #e9ecef; }}
                .logo-container {{ display: flex; align-items: center; justify-content: center; margin-bottom: 20px; }}
                .logo-img {{ width: 48px; height: 48px; margin-right: 12px; border-radius: 8px; }}
                .logo-text {{ font-size: 28px; font-weight: bold; color: #343a40; }}
                .logo-pro {{ color: #667eea; }}
                .main-content {{ padding: 40px 20px; }}
                .otp-section {{ text-align: center; margin: 30px 0; }}
                .otp-code {{ 
                    font-size: 36px; 
                    font-weight: bold; 
                    color: #667eea; 
                    background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%);
                    padding: 20px; 
                    border-radius: 12px;
                    margin: 20px 0; 
                    letter-spacing: 6px;
                    font-family: 'Courier New', monospace;
                    border: 3px dashed #667eea;
                }}
                .security-notice {{ 
                    background: #fff3cd; 
                    border-left: 4px solid #ffc107;
                    padding: 20px; 
                    margin: 30px 0; 
                    border-radius: 0 8px 8px 0;
                }}
                .security-notice h4 {{ margin: 0 0 10px 0; color: #856404; }}
                .security-notice p {{ margin: 0; color: #856404; font-size: 14px; }}
                .tips-section {{ 
                    background: #e3f2fd; 
                    border-radius: 8px; 
                    padding: 20px; 
                    margin: 30px 0; 
                }}
                .tips-section h4 {{ margin: 0 0 15px 0; color: #1976d2; }}
                .tips-list {{ margin: 0; padding-left: 20px; color: #1976d2; }}
                .tips-list li {{ margin-bottom: 8px; }}
                .footer {{ 
                    text-align: center;
                    padding: 30px 20px 20px; 
                    border-top: 1px solid #e9ecef; 
                    font-size: 12px; 
                    color: #6c757d; 
                }}
                .footer a {{ color: #667eea; text-decoration: none; }}
                .footer a:hover {{ text-decoration: underline; }}
            </style>
            </head>
            <body>
            <div class='container'>
                <div class='header'>
                    <div class='logo-container'>
                        <img src='https://cdn-b.saashub.com/images/app/service_logos/16/55ae58f74bf5/large.png?1539675612' alt='HireRight Logo' class='logo-img' />
                        <span class='logo-text'>HireRight<span class='logo-pro'>Pro</span></span>
                    </div>
                    <h2 style='color: #343a40; margin: 0;'>Email Verification Required</h2>
                    <p style='color: #6c757d; margin: 10px 0 0 0;'>Complete your registration securely</p>
                </div>
                
                <div class='main-content'>
                    <p>Welcome to HireRightPro! We're excited to have you join our platform.</p>
                    <p>To complete your registration and secure your account, please verify your email address using the code below:</p>
                    
                    <div class='otp-section'>
                        <div class='otp-code'>{otp}</div>
                        <p style='font-size: 14px; color: #6c757d; margin: 0;'>Enter this 6-digit code in the verification form</p>
                    </div>
                    
                    <div class='security-notice'>
                        <h4><i class='fas fa-shield-alt'></i> Security Notice</h4>
                        <p><strong>This code will expire in 5 minutes</strong> and can only be used once for security purposes.</p>
                        <p>If you didn't request this verification, please ignore this email.</p>
                    </div>
                    
                    <div class='tips-section'>
                        <h4><i class='fas fa-lightbulb'></i> Helpful Tips:</h4>
                        <ul class='tips-list'>
                            <li>You can copy and paste the code directly into the verification form</li>
                            <li>The code is case-sensitive, so enter it exactly as shown</li>
                            <li>If you don't receive this email, check your spam/junk folder</li>
                            <li>For security, this code expires in 5 minutes</li>
                        </ul>
                    </div>
                </div>
                
                <div class='footer'>
                    <p><strong>HireRightPro Registration System</strong></p>
                    <p>This is an automated security message. Please do not reply to this email.</p>
                    <p>Need help? Contact our support team at <a href='mailto:hiredrightpro@gmail.com'>hiredrightpro@gmail.com</a></p>
                    <p style='margin-top: 20px;'>&copy; {DateTime.Now.Year} HireRightPro. All rights reserved.</p>
                </div>
            </div>
            </body>
            </html>";
        }

        private static void CleanupExpiredOtpData(object? state)
        {
            try
            {
                var expiredKeys = new List<string>();
                var cutoffTime = DateTime.UtcNow.AddMinutes(-10); // Clean up data older than 10 minutes

                foreach (var kvp in _otpStorage)
                {
                    if (kvp.Value.Expiry < cutoffTime)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    _otpStorage.TryRemove(key, out _);
                    _otpRateLimit.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    Console.WriteLine($"Cleaned up {expiredKeys.Count} expired OTP entries");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during OTP cleanup: {ex.Message}");
            }
        }

        private static void CleanupOtpData(string email)
        {
            _otpStorage.TryRemove(email, out _);
            _otpRateLimit.TryRemove(email, out _);
        }

        // Enhanced validation methods
        private bool IsValidEmailEnhanced(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email &&
                       email.Contains("@") &&
                       email.Contains(".") &&
                       email.Length <= 254 &&
                       !email.StartsWith("@") &&
                       !email.EndsWith("@") &&
                       !email.Contains("..");
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidOtpFormat(string otp)
        {
            return !string.IsNullOrWhiteSpace(otp) &&
                   otp.Length == 6 &&
                   otp.All(char.IsDigit);
        }

        private string GetClientIpAddress()
        {
            try
            {
                var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xForwardedFor))
                {
                    return xForwardedFor.Split(',')[0].Trim();
                }

                var xRealIp = Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xRealIp))
                {
                    return xRealIp;
                }

                return Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        // FIXED: Enhanced user creation with transaction support - REMOVED NESTED TRANSACTION
        private async Task<(bool success, UserBase? user, UserProfile? profile, string? errorMessage)> CreateUserWithTransaction(RegisterViewModel model)
        {
            using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                // Check if username already exists
                if (await db.Users.AnyAsync(u => u.Username == model.Username))
                {
                    return (false, null, null, "Username already exists. Please choose another.");
                }

                // Check if email already exists
                if (await db.Users.AnyAsync(u => u.Email == model.Email))
                {
                    return (false, null, null, "Email already exists. Please use another email address.");
                }

                // Handle profile picture upload
                string? profilePhotoFileName = null;
                if (model.ProfilePicture != null && model.ProfilePicture.Length > 0)
                {
                    var uploadResult = await SaveProfilePictureEnhanced(model.ProfilePicture, model.Username);
                    if (!uploadResult.Success)
                    {
                        return (false, null, null, uploadResult.ErrorMessage);
                    }
                    profilePhotoFileName = uploadResult.FileName;
                }

                // Create user based on account type
                UserBase newUser = model.AccountType switch
                {
                    "JobSeeker" => new JobSeeker
                    {
                        Id = Guid.NewGuid().ToString("N")[..10],
                        Username = model.Username,
                        Password = BCrypt.Net.BCrypt.HashPassword(model.Password),
                        FullName = model.FullName,
                        Email = model.Email,
                        Gender = model.Gender,
                        Role = "JobSeeker",
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsEmailVerified = true,
                        FailedLoginAttempts = 0,
                        ProfilePhotoFileName = profilePhotoFileName,
                        ExperienceLevel = "Entry"
                    },
                    "Employer" => new Employer
                    {
                        Id = Guid.NewGuid().ToString("N")[..10],
                        Username = model.Username,
                        Password = BCrypt.Net.BCrypt.HashPassword(model.Password),
                        FullName = model.FullName,
                        Email = model.Email,
                        Gender = model.Gender,
                        Role = "Employer",
                        CompanyName = model.CompanyName ?? "",
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsEmailVerified = true,
                        ApprovalStatus = "Approved",
                        FailedLoginAttempts = 0,
                        ProfilePhotoFileName = profilePhotoFileName
                    },
                    _ => throw new InvalidOperationException("Invalid account type selected")
                };

                // FIXED: UserIdService will now participate in existing transaction instead of creating its own
                string generatedUserId = await _userIdService.GenerateNextUserId(model.AccountType);

                // FIXED: Include EmailVerificationToken and ProfilePicturePath to satisfy database constraints
                string emailVerificationToken = !string.IsNullOrEmpty(model.EmailVerificationToken)
                    ? model.EmailVerificationToken
                    : $"verified_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

                var userProfile = new UserProfile
                {
                    Id = Guid.NewGuid().ToString("N")[..10],
                    UserId = newUser.Id,
                    GeneratedUserId = generatedUserId,
                    RegistrationDate = DateTime.UtcNow,
                    EmailVerified = true,
                    AccountStatus = "Active",
                    LoginAttempts = 0,
                    EmailVerificationToken = emailVerificationToken, // FIXED: Added required field
                    ProfilePicturePath = string.Empty // FIXED: Added required field (set to empty string)
                };

                db.Users.Add(newUser);
                db.UserProfiles.Add(userProfile);
                await db.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("User created successfully: {Username} ({Role}) with ID {GeneratedUserId}",
                    newUser.Username, newUser.Role, generatedUserId);

                return (true, newUser, userProfile, null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating user during transaction for username: {Username}", model.Username);
                return (false, null, null, "Registration failed due to a system error. Please try again.");
            }
        }

        // FIXED: Enhanced validation methods with CompanyName conditional validation
        private async Task ValidateRegistrationModelEnhanced(RegisterViewModel model)
        {
            if (!string.IsNullOrEmpty(model.Username))
            {
                if (await db.Users.AnyAsync(u => u.Username == model.Username))
                {
                    ModelState.AddModelError("Username", "Username already exists. Please choose another.");
                }
                else if (model.Username.Length < 4 || model.Username.Length > 50)
                {
                    ModelState.AddModelError("Username", "Username must be between 4 and 50 characters.");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(model.Username, @"^[a-zA-Z0-9_]+$"))
                {
                    ModelState.AddModelError("Username", "Username can only contain letters, numbers, and underscores.");
                }
            }

            if (!string.IsNullOrEmpty(model.Email))
            {
                if (await db.Users.AnyAsync(u => u.Email == model.Email))
                {
                    ModelState.AddModelError("Email", "Email already exists. Please use another email address.");
                }
                else if (!IsValidEmailEnhanced(model.Email))
                {
                    ModelState.AddModelError("Email", "Please enter a valid email address.");
                }
            }

            // FIXED: Only validate CompanyName for Employer accounts
            if (model.AccountType == "Employer")
            {
                if (string.IsNullOrEmpty(model.CompanyName))
                {
                    ModelState.AddModelError("CompanyName", "Company name is required for employer accounts.");
                }
                else if (model.CompanyName.Length < 2 || model.CompanyName.Length > 100)
                {
                    ModelState.AddModelError("CompanyName", "Company name must be between 2 and 100 characters.");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(model.CompanyName, @"^[a-zA-ZÀ-ÿĀ-žА-я0-9\s\-\.\,\&\']+$"))
                {
                    ModelState.AddModelError("CompanyName", "Company name contains invalid characters.");
                }
            }
            else if (model.AccountType == "JobSeeker")
            {
                // FIXED: Clear CompanyName for JobSeekers to avoid validation issues
                model.CompanyName = string.Empty;

                // Remove any existing CompanyName errors from ModelState
                if (ModelState.ContainsKey("CompanyName"))
                {
                    ModelState.Remove("CompanyName");
                    _logger.LogInformation("CompanyName validation cleared for JobSeeker during model validation");
                }
            }

            if (model.ProfilePicture != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(model.ProfilePicture.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError("ProfilePicture", "Only JPG, PNG, GIF, and WEBP files are allowed.");
                }
                else if (model.ProfilePicture.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("ProfilePicture", "Profile picture must be less than 5MB.");
                }
                else if (model.ProfilePicture.Length < 1024)
                {
                    ModelState.AddModelError("ProfilePicture", "File size too small. Minimum size is 1KB.");
                }
            }
        }

        private async Task PreserveFormData(RegisterViewModel model)
        {
            ViewBag.PreserveFormData = true;
            ViewBag.ModelJson = JsonConvert.SerializeObject(new
            {
                model.Username,
                model.FullName,
                model.Email,
                model.Gender,
                model.AccountType,
                model.CompanyName,
                model.AgreeTerms,
                HasPhoto = model.ProfilePicture != null
            });

            // Handle photo preservation on validation errors
            if (model.ProfilePicture != null && model.ProfilePicture.Length > 0)
            {
                try
                {
                    // Convert photo to base64 for temporary storage
                    using var memoryStream = new MemoryStream();
                    await model.ProfilePicture.CopyToAsync(memoryStream);
                    var photoBytes = memoryStream.ToArray();
                    var base64Photo = Convert.ToBase64String(photoBytes);

                    ViewBag.PreservedPhoto = base64Photo;
                    ViewBag.PreservedPhotoType = model.ProfilePicture.ContentType;
                    ViewBag.PreservedPhotoName = model.ProfilePicture.FileName;

                    _logger.LogInformation("Photo preserved for validation error recovery for user {Username}", model.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to preserve photo for validation error for user {Username}", model.Username);
                }
            }
        }

        // Enhanced user credential validation
        private async Task<(bool success, UserBase? user, string errorMessage)> ValidateUserCredentials(LoginViewModel model, string clientIp, string userAgent)
        {
            try
            {
                var user = await db.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username || u.Email == model.Username);

                if (user == null)
                {
                    _logger.LogWarning("Login attempt for non-existent user {Username} from IP {IP}", model.Username, clientIp);
                    return (false, null, "Invalid username or password.");
                }

                if (user.FailedLoginAttempts >= 5)
                {
                    _logger.LogWarning("Login attempt for locked account {Username} from IP {IP}", user.Username, clientIp);
                    return (false, null, "Account is locked due to multiple failed login attempts. Please contact support.");
                }

                if (!BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
                {
                    user.FailedLoginAttempts++;
                    await db.SaveChangesAsync();

                    _logger.LogWarning("Invalid password for user {Username} from IP {IP}. Failed attempts: {FailedAttempts}",
                        user.Username, clientIp, user.FailedLoginAttempts);

                    if (user.FailedLoginAttempts >= 5)
                    {
                        return (false, null, "Account locked due to multiple failed attempts. Please contact support.");
                    }
                    else
                    {
                        return (false, null, $"Invalid credentials. {5 - user.FailedLoginAttempts} attempts remaining.");
                    }
                }

                if (!user.IsActive)
                {
                    _logger.LogWarning("Login attempt for deactivated account {Username} from IP {IP}", user.Username, clientIp);
                    return (false, null, "Account deactivated. Please contact support.");
                }

                if (user is Employer employer && employer.ApprovalStatus != "Approved")
                {
                    _logger.LogInformation("Login attempt for non-approved employer {Username} with status {Status}",
                        user.Username, employer.ApprovalStatus);

                    var message = employer.ApprovalStatus == "Pending"
                        ? "Your employer account is pending approval. Please wait for admin approval."
                        : "Your employer account application was rejected. Please contact support.";

                    return (false, null, message);
                }

                // Reset failed attempts on successful validation
                user.FailedLoginAttempts = 0;
                await db.SaveChangesAsync();

                return (true, user, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during credential validation for {Username} from IP {IP}", model.Username, clientIp);
                return (false, null, "Login failed due to a system error. Please try again.");
            }
        }

        // Enhanced user sign-in with detailed logging
        private async Task SignInUserEnhanced(UserBase user, bool rememberMe, string clientIp, string userAgent)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("FullName", user.FullName ?? ""),
                new Claim("ProfilePhotoFileName", user.ProfilePhotoFileName ?? ""),
                new Claim("Gender", user.Gender ?? ""),
                new Claim("LoginTime", DateTime.UtcNow.ToString("O")),
                new Claim("ClientIP", clientIp),
                new Claim("SessionId", Guid.NewGuid().ToString())
            };

            if (user is Employer employer)
            {
                claims.Add(new Claim("CompanyName", employer.CompanyName ?? ""));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : DateTimeOffset.UtcNow.AddDays(1),
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties
            );

            // Cache user session data
            var sessionKey = $"user_session_{user.Username}";
            var sessionData = new
            {
                UserId = user.Id,
                Username = user.Username,
                Role = user.Role,
                LoginTime = DateTime.UtcNow,
                ClientIp = clientIp,
                UserAgent = userAgent
            };

            _memoryCache.Set(sessionKey, sessionData, TimeSpan.FromHours(24));

            _logger.LogInformation("Session created for {Username} ({Role}) from IP {IP} - RememberMe: {RememberMe}, Expires: {Expires}",
                user.Username, user.Role, clientIp, rememberMe, authProperties.ExpiresUtc);
        }

        // Enhanced reCAPTCHA validation with better error handling
        private async Task<bool> ValidateRecaptchaEnhanced(string? recaptchaResponse)
        {
            if (string.IsNullOrEmpty(recaptchaResponse))
            {
                _logger.LogWarning("Empty reCAPTCHA response received");
                return false;
            }

            try
            {
                var secretKey = _configuration["Recaptcha:SecretKey"];
                if (string.IsNullOrEmpty(secretKey))
                {
                    _logger.LogError("reCAPTCHA secret key not configured");
                    return false;
                }

                var apiUrl = "https://www.google.com/recaptcha/api/siteverify";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.PostAsync(apiUrl, new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("secret", secretKey),
                    new KeyValuePair<string, string>("response", recaptchaResponse),
                    new KeyValuePair<string, string>("remoteip", GetClientIpAddress())
                }));

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("reCAPTCHA validation failed with HTTP status: {StatusCode}", response.StatusCode);
                    return false;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<RecaptchaResponse>(jsonResponse);

                if (result == null)
                {
                    _logger.LogError("Failed to deserialize reCAPTCHA response");
                    return false;
                }

                if (!result.success)
                {
                    _logger.LogWarning("reCAPTCHA validation failed. Errors: {Errors}",
                        result.errors != null ? string.Join(", ", result.errors) : "None");
                }

                return result.success;
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("reCAPTCHA validation timeout");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during reCAPTCHA validation");
                return false;
            }
        }

        // Enhanced profile picture saving with better security
        private async Task<(bool Success, string? FileName, string? ErrorMessage)> SaveProfilePictureEnhanced(IFormFile file, string username)
        {
            try
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var mimeType = file.ContentType.ToLowerInvariant();

                // Validate extension and MIME type
                if (!allowedExtensions.Contains(extension))
                {
                    return (false, null, "Invalid file type. Only JPG, PNG, GIF, and WEBP files are allowed.");
                }

                if (!allowedMimeTypes.Contains(mimeType))
                {
                    return (false, null, "Invalid MIME type. File content doesn't match the extension.");
                }

                // Validate file size
                if (file.Length > 5 * 1024 * 1024)
                {
                    return (false, null, "File size too large. Maximum size is 5MB.");
                }

                if (file.Length < 1024)
                {
                    return (false, null, "File size too small. Minimum size is 1KB.");
                }

                // Basic file content validation
                var buffer = new byte[512];
                using (var stream = file.OpenReadStream())
                {
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                }

                // Check for common image file signatures
                if (!IsValidImageFileSignature(buffer, extension))
                {
                    return (false, null, "Invalid image file. File content doesn't match the expected format.");
                }

                // Create upload directory
                string uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profilepics");

                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                    _logger.LogInformation("Created upload directory: {UploadPath}", uploadPath);
                }

                // Generate secure filename
                string cleanUsername = System.Text.RegularExpressions.Regex.Replace(username, @"[^a-zA-Z0-9_-]", "");
                string fileName = $"{cleanUsername}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}{extension}";
                string filePath = Path.Combine(uploadPath, fileName);

                // Ensure no file conflicts
                int counter = 1;
                string baseFileName = fileName;
                while (System.IO.File.Exists(filePath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(baseFileName);
                    fileName = $"{nameWithoutExt}_{counter}{extension}";
                    filePath = Path.Combine(uploadPath, fileName);
                    counter++;
                }

                // Save file with proper permissions
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(stream);
                    await stream.FlushAsync();
                }

                // Verify file was saved correctly
                if (!System.IO.File.Exists(filePath))
                {
                    return (false, null, "Failed to save file to server.");
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    System.IO.File.Delete(filePath);
                    return (false, null, "Saved file is empty or corrupted.");
                }

                _logger.LogInformation("Profile picture saved successfully: {FileName} for user {Username}", fileName, username);
                return (true, fileName, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while saving profile picture for {Username}", username);
                return (false, null, "An unexpected error occurred while saving the file. Please try again.");
            }
        }

        // Enhanced file signature validation
        private static bool IsValidImageFileSignature(byte[] buffer, string extension)
        {
            if (buffer.Length < 8) return false;

            return extension switch
            {
                ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
                ".png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
                ".gif" => (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38 &&
                          (buffer[4] == 0x37 || buffer[4] == 0x39) && buffer[5] == 0x61),
                ".webp" => buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                          buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50,
                _ => false
            };
        }

        // Enhanced dashboard redirection
        private IActionResult RedirectToDashboard()
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var username = User.Identity?.Name;

            _logger.LogInformation("Redirecting user {Username} with role {Role} to appropriate dashboard", username, userRole);

            return userRole switch
            {
                "Admin" => RedirectToAction("AdminDashboard", "Admin"),
                "Employer" => RedirectToAction("Index", "Employer"),
                "JobSeeker" => RedirectToAction("Index", "JobSeeker"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        // GET: Access Denied with logging
        [HttpGet]
        public IActionResult AccessDenied()
        {
            var username = User.Identity?.Name;
            var requestedPath = Request.Headers["Referer"].ToString();

            _logger.LogWarning("Access denied for user {Username} attempting to access {RequestedPath}", username, requestedPath);

            return View();
        }

        // GET: ForgotPassword
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
            return View(new ForgotPasswordViewModel()); 
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model) 
        {
            var clientIp = GetClientIpAddress();

            try
            {
                _logger.LogInformation("Password reset request for email {Email} from IP {IP}", model?.Email, clientIp);

                if (!ModelState.IsValid)
                {
                    ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
                    return View(model);
                }

                // Validate reCAPTCHA
                if (!await ValidateRecaptchaEnhanced(model.RecaptchaResponse))
                {
                    ModelState.AddModelError("RecaptchaResponse", "Please complete the reCAPTCHA verification.");
                    ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
                    return View(model);
                }

                if (string.IsNullOrEmpty(model.Email) || !IsValidEmailEnhanced(model.Email))
                {
                    ModelState.AddModelError("Email", "Please enter a valid email address.");
                    ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
                    return View(model);
                }

                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

                // Always show success message for security (don't reveal if email exists)
                if (user == null)
                {
                    _logger.LogInformation("Password reset requested for non-existent email {Email} from IP {IP}", model.Email, clientIp);
                    return RedirectToAction("ForgotPasswordConfirmation");
                }

                // Check if user account is active
                if (!user.IsActive)
                {
                    _logger.LogWarning("Password reset requested for inactive account {Email} from IP {IP}", model.Email, clientIp);
                    return RedirectToAction("ForgotPasswordConfirmation");
                }

                // Generate secure reset token
                var resetToken = GeneratePasswordResetToken();
                var resetTokenExpiry = DateTime.UtcNow.AddMinutes(15); // Token Valid for 15 minutes

                // Update user with reset token
                user.PasswordResetToken = resetToken;
                user.PasswordResetTokenExpire = resetTokenExpiry;
                await db.SaveChangesAsync();

                // Create reset link
                var resetUrl = Url.Action("ResetPassword", "Account",
                    new { token = resetToken, email = model.Email }, Request.Scheme);

                // Send reset email
                var emailSubject = "Password Reset Request - HireRight Portal";
                var emailBody = CreatePasswordResetEmailBody(user.FullName, resetUrl, resetToken);

                await _emailSender.SendEmailAsync(model.Email, emailSubject, emailBody);

                _logger.LogInformation("Password reset email sent to {Email} from IP {IP}", model.Email, clientIp);

                // Store email in TempData for confirmation page
                TempData["ResetEmail"] = model.Email;

                return RedirectToAction("ForgotPasswordConfirmation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during password reset request for email {Email} from IP {IP}", model?.Email, clientIp);
                ModelState.AddModelError("", "An error occurred while processing your request. Please try again.");
                ViewBag.SiteKey = _configuration["Recaptcha:SiteKey"];
                return View(model);
            }
        }

        // Dispose method to clean up resources
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cleanup would go here if needed
            }
            base.Dispose(disposing);
        }

        // Change Password
        [HttpGet]
        [Authorize] // Ensure user is logged in
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize] // Ensure user is logged in
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            // Validation
            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
            {
                ViewBag.Error = "All fields are required.";
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "New passwords don't match.";
                return View();
            }

            if (newPassword.Length < 6)
            {
                ViewBag.Error = "Password must be at least 6 characters long.";
                return View();
            }

            // Get current user (using the same pattern as your Program.cs)
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await db.Users.FindAsync(userId);

            if (user == null)
            {
                ViewBag.Error = "User not found.";
                return View();
            }

            // Verify current password using BCrypt (already imported in Program.cs)
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.Password))
            {
                ViewBag.Error = "Current password is incorrect.";
                return View();
            }

            // Check if new password is same as current
            if (BCrypt.Net.BCrypt.Verify(newPassword, user.Password))
            {
                ViewBag.Error = "New password must be different from current password.";
                return View();
            }

            // Update password
            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await db.SaveChangesAsync();

            // Log the change (using the logger pattern from your Program.cs)
            _logger.LogInformation("Password changed for user {Username} at {Time}", user.Username, DateTime.UtcNow);

            ViewBag.Success = "Password changed successfully!";
            return View();
        }

        // GET: ForgotPasswordConfirmation
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            ViewBag.Email = TempData["ResetEmail"] as string;
            return View();
        }

        // GET: ResetPassword
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(string token, string email)
        {
            var clientIp = GetClientIpAddress();

            try
            {
                _logger.LogInformation("Password reset page accessed for email {Email} from IP {IP}", email, clientIp);

                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("Invalid reset link accessed from IP {IP} - missing token or email", clientIp);
                    TempData["Error"] = "Invalid reset link. Please request a new password reset.";
                    return RedirectToAction("ForgotPassword");
                }

                if (!IsValidEmailEnhanced(email))
                {
                    _logger.LogWarning("Invalid email format in reset link: {Email} from IP {IP}", email, clientIp);
                    TempData["Error"] = "Invalid reset link. Please request a new password reset.";
                    return RedirectToAction("ForgotPassword");
                }

                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                {
                    _logger.LogWarning("Password reset attempted for non-existent user {Email} from IP {IP}", email, clientIp);
                    TempData["Error"] = "Invalid reset link. Please request a new password reset.";
                    return RedirectToAction("ForgotPassword");
                }

                // Validate token
                if (string.IsNullOrEmpty(user.PasswordResetToken) ||
                    user.PasswordResetToken != token ||
                    user.PasswordResetTokenExpire == null ||
                    DateTime.UtcNow > user.PasswordResetTokenExpire)
                {
                    _logger.LogWarning("Invalid or expired reset token for user {Email} from IP {IP}", email, clientIp);
                    TempData["Error"] = "This reset link has expired or is invalid. Please request a new password reset.";
                    return RedirectToAction("ForgotPassword");
                }

                var model = new Demo.Models.AccountViewModels.ResetPasswordViewModel // FIXED: Correct namespace
                {
                    Email = email,
                    Token = token
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception accessing reset password page for email {Email} from IP {IP}", email, clientIp);
                TempData["Error"] = "An error occurred. Please try again.";
                return RedirectToAction("ForgotPassword");
            }
        }

        // POST: ResetPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(Demo.Models.AccountViewModels.ResetPasswordViewModel model) // FIXED: Correct namespace
        {
            var clientIp = GetClientIpAddress();

            try
            {
                _logger.LogInformation("Password reset attempt for email {Email} from IP {IP}", model?.Email, clientIp);

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
                if (user == null)
                {
                    _logger.LogWarning("Password reset attempted for non-existent user {Email} from IP {IP}", model.Email, clientIp);
                    ModelState.AddModelError("", "Invalid reset request.");
                    return View(model);
                }

                // Validate token again
                if (string.IsNullOrEmpty(user.PasswordResetToken) ||
                    user.PasswordResetToken != model.Token ||
                    user.PasswordResetTokenExpire == null ||
                    DateTime.UtcNow > user.PasswordResetTokenExpire)
                {
                    _logger.LogWarning("Invalid or expired reset token during password reset for user {Email} from IP {IP}", model.Email, clientIp);
                    ModelState.AddModelError("", "This reset link has expired or is invalid. Please request a new password reset.");
                    return View(model);
                }

                // Check if new password is same as current password
                if (BCrypt.Net.BCrypt.Verify(model.NewPassword, user.Password))
                {
                    ModelState.AddModelError("NewPassword", "New password must be different from your current password.");
                    return View(model);
                }

                // Update password and clear reset token
                user.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
                user.PasswordResetToken = null;
                user.PasswordResetTokenExpire = null;
                user.FailedLoginAttempts = 0; // Reset failed login attempts

                await db.SaveChangesAsync();

                _logger.LogInformation("Password reset successfully for user {Email} from IP {IP}", model.Email, clientIp);

                // Send confirmation email
                try
                {
                    var confirmationSubject = "Password Reset Successful - HireRight Portal";
                    var confirmationBody = CreatePasswordResetConfirmationEmailBody(user.FullName);
                    await _emailSender.SendEmailAsync(user.Email, confirmationSubject, confirmationBody);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send password reset confirmation email to {Email}", user.Email);
                    // Don't fail the entire operation if email fails
                }

                TempData["Success"] = "Your password has been reset successfully. You can now login with your new password.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during password reset for email {Email} from IP {IP}", model?.Email, clientIp);
                ModelState.AddModelError("", "An error occurred while resetting your password. Please try again.");
                return View(model);
            }
        }

        // Helper method to generate secure password reset token
        private static string GeneratePasswordResetToken()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        // Helper method to create password reset email body
        private string CreatePasswordResetEmailBody(string fullName, string resetUrl, string token)
        {
            return $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset=""utf-8"">
            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
            <title>Password Reset Request - HireRight Portal</title>
            <style>
            body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 0; background-color: #f8f9fa; line-height: 1.6; }}
            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background-color: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); }}
            .header {{ text-align: center; padding: 30px 0; border-bottom: 2px solid #e9ecef; }}
            .logo-container {{ display: flex; align-items: center; justify-content: center; margin-bottom: 20px; }}
            .logo-img {{ width: 48px; height: 48px; margin-right: 12px; border-radius: 8px; }}
            .logo-text {{ font-size: 28px; font-weight: bold; color: #343a40; }}
            .logo-pro {{ color: #667eea; }}
            .main-content {{ padding: 40px 20px; }}
            .reset-button {{ 
            display: inline-block;
            background: #667eea;
            color: white !important;
            padding: 15px 30px;
            text-decoration: none;
            border-radius: 8px;
            font-weight: bold;
            font-size: 16px;
            text-align: center;
            margin: 20px 0;
            box-shadow: 0 2px 4px rgba(102, 126, 234, 0.3);
            border: 1px solid #667eea;
            }}
            .reset-button:hover {{ 
            background: #5a6fd8; 
            border-color: #5a6fd8;
            color: white !important;
            }}
            .security-notice {{ 
                background: #fff3cd; 
                border-left: 4px solid #ffc107;
                padding: 20px; 
                margin: 30px 0; 
                border-radius: 0 8px 8px 0;
            }}
            .security-notice h4 {{ margin: 0 0 10px 0; color: #856404; }}
            .security-notice p {{ margin: 0; color: #856404; font-size: 14px; }}
            .footer {{ 
                text-align: center;
                padding: 30px 20px 20px; 
                border-top: 1px solid #e9ecef; 
                font-size: 12px; 
                color: #6c757d; 
            }}
            .footer a {{ color: #667eea; text-decoration: none; }}
            .footer a:hover {{ text-decoration: underline; }}
            .token-info {{ 
                background: #f8f9fa; 
                padding: 15px; 
                border-radius: 8px; 
                margin: 20px 0;
                font-family: 'Courier New', monospace;
                font-size: 12px;
                color: #6c757d;
                word-break: break-all;
            }}
            </style>
            </head>
            <body>
            <div class='container'>
            <div class='header'>
            <div class='logo-container'>
                <img src='https://cdn-b.saashub.com/images/app/service_logos/16/55ae58f74bf5/large.png?1539675612' alt='HireRight Logo' class='logo-img' />
                <span class='logo-text'>HireRight<span class='logo-pro'>Pro</span></span>
            </div>
            <h2 style='color: #343a40; margin: 0;'>Password Reset Request</h2>
            <p style='color: #6c757d; margin: 10px 0 0 0;'>Secure password reset for your account</p>
            </div>

            <div class='main-content'>
            <p>Hello {fullName},</p>
            <p>We received a request to reset the password for your HireRightPro account. If you made this request, click the button below to reset your password:</p>
    
            <div style='text-align: center; margin: 30px 0;'>
            <a href=""{resetUrl}"" class=""reset-button"">Reset My Password</a>
            </div>
    
            <div class='security-notice'>
                <h4><i class='fas fa-shield-alt'></i> Security Information</h4>
                <p><strong>This link will expire in 15 minutes</strong> for your security.</p>
                <p>If you didn't request this password reset, please ignore this email and your password will remain unchanged.</p>
                <p>For security reasons, this reset link can only be used once.</p>
            </div>
    
            <p><strong>If the button doesn't work</strong>, copy and paste this link into your browser:</p>
            <p style='word-break: break-all; color: #667eea; font-family: monospace; background: #f8f9fa; padding: 10px; border-radius: 4px;'>{resetUrl}</p>
    
            <hr style='margin: 30px 0; border: none; border-top: 1px solid #e9ecef;'>
    
            <h4>Didn't request this reset?</h4>
            <p>If you didn't request a password reset, your account may be compromised. Please:</p>
            <ul>
                <li>Login to your account immediately and change your password</li>
                <li>Review your account activity</li>
                <li>Contact our support team if you notice any suspicious activity</li>
            </ul>
            </div>

            <div class='footer'>
            <p><strong>HireRightPro Security Team</strong></p>
            <p>This is an automated security message. Please do not reply to this email.</p>
            <p>Need help? Contact our support team at <a href='mailto:hiredrightpro@gmail.com'>hiredrightpro@gmail.com</a></p>
            <p style='margin-top: 20px;'>&copy; {DateTime.Now.Year} HireRightPro. All rights reserved.</p>
            </div>
            </div>
            </body>
            </html>";
        }

        // Helper method to create password reset confirmation email
        private string CreatePasswordResetConfirmationEmailBody(string fullName)
        {
            return $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset=""utf-8"">
            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
            <title>Password Reset Successful - HireRight Portal</title>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 0; background-color: #f8f9fa; line-height: 1.6; }}
                .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background-color: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); }}
                .header {{ text-align: center; padding: 30px 0; border-bottom: 2px solid #e9ecef; }}
                .logo-container {{ display: flex; align-items: center; justify-content: center; margin-bottom: 20px; }}
                .logo-img {{ width: 48px; height: 48px; margin-right: 12px; border-radius: 8px; }}
                .logo-text {{ font-size: 28px; font-weight: bold; color: #343a40; }}
                .logo-pro {{ color: #667eea; }}
                .main-content {{ padding: 40px 20px; }}
                .success-section {{ text-align: center; margin: 30px 0; }}
                .success-icon {{ 
                    font-size: 64px; 
                    color: #28a745; 
                    margin-bottom: 20px;
                    display: block;
                }}
                .success-message {{
                    background: linear-gradient(135deg, #d4edda 0%, #c3e6cb 100%);
                    border: 2px solid #28a745;
                    color: #155724;
                    padding: 20px;
                    border-radius: 12px;
                    margin: 20px 0;
                    font-weight: bold;
                }}
                .details-section {{ 
                    background: #e3f2fd; 
                    border-radius: 8px; 
                    padding: 20px; 
                    margin: 30px 0; 
                }}
                .details-section h4 {{ margin: 0 0 15px 0; color: #1976d2; }}
                .details-list {{ margin: 0; padding-left: 20px; color: #1976d2; }}
                .details-list li {{ margin-bottom: 8px; }}
                .security-tips {{ 
                    background: #fff3cd; 
                    border-left: 4px solid #ffc107;
                    padding: 20px; 
                    margin: 30px 0; 
                    border-radius: 0 8px 8px 0;
                }}
                .security-tips h4 {{ margin: 0 0 10px 0; color: #856404; }}
                .security-tips ul {{ margin: 10px 0; padding-left: 20px; color: #856404; }}
                .security-tips li {{ margin-bottom: 8px; }}
                .footer {{ 
                    text-align: center;
                    padding: 30px 20px 20px; 
                    border-top: 1px solid #e9ecef; 
                    font-size: 12px; 
                    color: #6c757d; 
                }}
                .footer a {{ color: #667eea; text-decoration: none; }}
                .footer a:hover {{ text-decoration: underline; }}
            </style>
            </head>
            <body>
            <div class='container'>
                <div class='header'>
                    <div class='logo-container'>
                        <img src='https://cdn-b.saashub.com/images/app/service_logos/16/55ae58f74bf5/large.png?1539675612' alt='HireRight Logo' class='logo-img' />
                        <span class='logo-text'>HireRight<span class='logo-pro'>Pro</span></span>
                    </div>
                    <h2 style='color: #343a40; margin: 0;'>Password Reset Successful</h2>
                    <p style='color: #6c757d; margin: 10px 0 0 0;'>Your account security has been updated</p>
                </div>
        
                <div class='main-content'>
                    <div class='success-section'>
                        <span class='success-icon'>✓</span>
                        <div class='success-message'>
                            Password Successfully Reset!
                        </div>
                    </div>
            
                    <p>Hello {fullName},</p>
                    <p>Great news! Your password has been successfully reset for your HireRightPro account. You can now login with your new password.</p>
            
                    <div class='details-section'>
                        <h4><i class='fas fa-info-circle'></i> Reset Details:</h4>
                        <ul class='details-list'>
                            <li><strong>Date & Time:</strong> {DateTime.UtcNow:F} UTC</li>
                        </ul>
                    </div>
            
                    <div class='security-tips'>
                        <h4><i class='fas fa-shield-alt'></i> Security Recommendations</h4>
                        <p>To keep your account secure, we recommend:</p>
                        <ul>
                            <li>Use a strong, unique password that you haven't used elsewhere</li>
                            <li>Keep your login credentials secure and never share them</li>
                            <li>Log out of shared or public computers after use</li>
                        </ul>
                    </div>
            
                    <hr style='margin: 30px 0; border: none; border-top: 1px solid #e9ecef;'>
            
                    <p><strong>Didn't make this change?</strong></p>
                    <p>If you didn't reset your password, your account may be compromised. Please contact our support team immediately at <a href='mailto:hiredrightpro@gmail.com' style='color: #667eea;'>hiredrightpro@gmail.com</a></p>
                </div>
        
                <div class='footer'>
                    <p><strong>HireRightPro Security Team</strong></p>
                    <p>This is an automated security confirmation. Please do not reply to this email.</p>
                    <p>Need help? Contact our support team at <a href='mailto:hiredrightpro@gmail.com'>hiredrightpro@gmail.com</a></p>
                    <p style='margin-top: 20px;'>&copy; {DateTime.Now.Year} HireRightPro. All rights reserved.</p>
                </div>
            </div>
            </body>
            </html>";
        }
        public class RecaptchaResponse
        {
            public bool success { get; set; }
            public string[]? errors { get; set; }
            public DateTime challenge_ts { get; set; }
            public string? hostname { get; set; }
            public double score { get; set; }
            public string? action { get; set; }
        }
    }
}