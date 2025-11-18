using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Demo.Models.AccountViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        [StringLength(50, MinimumLength = 4, ErrorMessage = "Username must be between 4 and 50 characters")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full name is required")]
        [Display(Name = "Full Name")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters")]
        [RegularExpression(@"^[a-zA-ZÀ-ÿĀ-žА-я\s\-\.\']+$", ErrorMessage = "Full name can only contain letters, spaces, hyphens, dots, and apostrophes")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [Display(Name = "Email Address")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [Display(Name = "Password")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        [StrongPassword(ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, and one number")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password")]
        [Display(Name = "Confirm Password")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select your gender")]
        [Display(Name = "Gender")]
        [AllowedValues("Male", "Female", "Other", ErrorMessage = "Please select a valid gender")]
        public string Gender { get; set; } = string.Empty;

        [Required(ErrorMessage = "Profile Picture is required")]
        [Display(Name = "Profile Picture")]
        [AllowedExtensions(new string[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }, ErrorMessage = "Please upload a valid image file (jpg, jpeg, png, gif, webp)")]
        [MaxFileSize(5 * 1024 * 1024, ErrorMessage = "Maximum file size is 5MB")]
        [MinFileSize(1024, ErrorMessage = "File size too small. Minimum size is 1KB")]
        public IFormFile? ProfilePicture { get; set; }

        [Required(ErrorMessage = "Please select account type")]
        [Display(Name = "Account Type")]
        [AllowedValues("JobSeeker", "Employer", ErrorMessage = "Please select a valid account type")]
        public string AccountType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please complete the reCAPTCHA verification")]
        public string RecaptchaResponse { get; set; } = string.Empty;

        // For Employer only - conditional validation handled server-side
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please accept the terms and conditions")]
        [Display(Name = "I agree to the Terms and Conditions")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms and conditions")]
        public bool AgreeTerms { get; set; } = false;

        // Enhanced OTP Properties
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be exactly 6 digits")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be exactly 6 digits")]
        [Display(Name = "Verification Code")]
        public string OtpCode { get; set; } = string.Empty;

        public bool IsOtpSectionVisible { get; set; } = false;
        public bool IsEmailSent { get; set; } = false;
        public string OtpMessage { get; set; } = string.Empty;
        public bool IsOtpSuccess { get; set; } = false;
        public int ResendCountdownSeconds { get; set; } = 0;
        public bool CanResendOtp { get; set; } = true;
        public bool IsEmailVerified { get; set; } = false;

        // Email Verification Token to bypass reCAPTCHA after verification
        [Display(Name = "Email Verification Token")]
        public string EmailVerificationToken { get; set; } = string.Empty;

        // Additional Security Properties
        public DateTime? EmailVerificationTime { get; set; }
        public int OtpAttemptCount { get; set; } = 0;
        public bool IsSecurityCheckPassed { get; set; } = false;

        // UI State Properties
        public bool IsProcessing { get; set; } = false;
        public string ProcessingMessage { get; set; } = string.Empty;
        public Dictionary<string, bool> FieldValidationStates { get; set; } = new();
    }

    // Enhanced Custom Validation Attributes

    public class AllowedExtensionsAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;

        public AllowedExtensionsAttribute(string[] extensions)
        {
            _extensions = extensions?.Select(ext => ext.ToLowerInvariant()).ToArray() ?? Array.Empty<string>();
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
            {
                return ValidationResult.Success;
            }

            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                return new ValidationResult("File name is required");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_extensions.Contains(extension))
            {
                return new ValidationResult($"Only {string.Join(", ", _extensions.Select(e => e.ToUpperInvariant()))} files are allowed");
            }

            return ValidationResult.Success;
        }
    }

    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxFileSize;
        private readonly string _maxFileSizeFormatted;

        public MaxFileSizeAttribute(int maxFileSize)
        {
            _maxFileSize = maxFileSize;
            _maxFileSizeFormatted = FormatFileSize(maxFileSize);
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
            {
                return ValidationResult.Success;
            }

            if (file.Length > _maxFileSize)
            {
                return new ValidationResult($"File size exceeds maximum allowed size of {_maxFileSizeFormatted}");
            }

            return ValidationResult.Success;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##}{sizes[order]}";
        }
    }

    public class MinFileSizeAttribute : ValidationAttribute
    {
        private readonly int _minFileSize;
        private readonly string _minFileSizeFormatted;

        public MinFileSizeAttribute(int minFileSize)
        {
            _minFileSize = minFileSize;
            _minFileSizeFormatted = FormatFileSize(minFileSize);
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
            {
                return ValidationResult.Success;
            }

            if (file.Length < _minFileSize)
            {
                return new ValidationResult($"File size is too small. Minimum size is {_minFileSizeFormatted}");
            }

            return ValidationResult.Success;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##}{sizes[order]}";
        }
    }

    public class AllowedValuesAttribute : ValidationAttribute
    {
        private readonly string[] _allowedValues;

        public AllowedValuesAttribute(params string[] allowedValues)
        {
            _allowedValues = allowedValues ?? Array.Empty<string>();
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success; // Let Required attribute handle null values
            }

            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return ValidationResult.Success; // Let Required attribute handle empty values
            }

            if (!_allowedValues.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
            {
                return new ValidationResult($"Value must be one of: {string.Join(", ", _allowedValues)}");
            }

            return ValidationResult.Success;
        }
    }

    public class StrongPasswordAttribute : ValidationAttribute
    {
        public int MinLength { get; set; } = 6;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireDigit { get; set; } = true;
        public bool RequireSpecialCharacter { get; set; } = false;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string password || string.IsNullOrWhiteSpace(password))
            {
                return ValidationResult.Success; // Let Required attribute handle null/empty values
            }

            var errors = new List<string>();

            if (password.Length < MinLength)
            {
                errors.Add($"at least {MinLength} characters");
            }

            if (RequireUppercase && !password.Any(char.IsUpper))
            {
                errors.Add("one uppercase letter");
            }

            if (RequireLowercase && !password.Any(char.IsLower))
            {
                errors.Add("one lowercase letter");
            }

            if (RequireDigit && !password.Any(char.IsDigit))
            {
                errors.Add("one number");
            }

            if (RequireSpecialCharacter && !Regex.IsMatch(password, @"[!@#$%^&*()_+=\[{\]};:<>|./?,-]"))
            {
                errors.Add("one special character");
            }

            if (errors.Any())
            {
                return new ValidationResult($"Password must contain {string.Join(", ", errors)}");
            }

            return ValidationResult.Success;
        }
    }

    // Enhanced Email Validation Attribute
    public class EnhancedEmailAttribute : ValidationAttribute
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> DisposableEmailProviders = new(StringComparer.OrdinalIgnoreCase)
        {
            "10minutemail.com", "guerrillamail.com", "mailinator.com", "tempmail.org", "temp-mail.org"
        };

        public bool AllowDisposableEmails { get; set; } = true;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string email || string.IsNullOrWhiteSpace(email))
            {
                return ValidationResult.Success; // Let Required attribute handle null/empty values
            }

            if (!EmailRegex.IsMatch(email))
            {
                return new ValidationResult("Invalid email format");
            }

            if (!AllowDisposableEmails)
            {
                var domain = email.Split('@').LastOrDefault();
                if (!string.IsNullOrWhiteSpace(domain) && DisposableEmailProviders.Contains(domain))
                {
                    return new ValidationResult("Disposable email addresses are not allowed");
                }
            }

            return ValidationResult.Success;
        }
    }

    // Security-focused Username Validation
    public class SecureUsernameAttribute : ValidationAttribute
    {
        private static readonly HashSet<string> ReservedUsernames = new(StringComparer.OrdinalIgnoreCase)
        {
            "admin", "administrator", "root", "user", "test", "guest", "demo", "system", "support",
            "api", "www", "mail", "email", "help", "info", "contact", "about", "privacy", "terms",
            "null", "undefined", "anonymous", "public", "private"
        };

        public override bool IsValid(object? value)
        {
            if (value is not string username || string.IsNullOrWhiteSpace(username))
            {
                return true; // Let Required attribute handle null/empty values
            }

            // Check for reserved usernames
            if (ReservedUsernames.Contains(username))
            {
                ErrorMessage = "This username is reserved and cannot be used";
                return false;
            }

            // Check for consecutive special characters
            if (Regex.IsMatch(username, @"__+"))
            {
                ErrorMessage = "Username cannot contain consecutive underscores";
                return false;
            }

            // Check for username starting or ending with underscore
            if (username.StartsWith('_') || username.EndsWith('_'))
            {
                ErrorMessage = "Username cannot start or end with underscore";
                return false;
            }

            return true;
        }
    }
}