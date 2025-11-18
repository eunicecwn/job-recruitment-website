using System.Net;
using System.Net.Mail;
using System.Text;

namespace Demo.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Attempting to send email to {Email} with subject: {Subject}", email, subject);

                // Validate configuration
                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var portString = _configuration["EmailSettings:Port"];
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var senderEmail = _configuration["EmailSettings:SenderEmail"];
                var senderName = _configuration["EmailSettings:SenderName"];

                if (string.IsNullOrEmpty(smtpServer) ||
                    string.IsNullOrEmpty(portString) ||
                    string.IsNullOrEmpty(username) ||
                    string.IsNullOrEmpty(password) ||
                    string.IsNullOrEmpty(senderEmail))
                {
                    throw new InvalidOperationException("Email configuration is incomplete. Please check EmailSettings in configuration.");
                }

                if (!int.TryParse(portString, out int port))
                {
                    throw new InvalidOperationException("Invalid port number in EmailSettings configuration.");
                }

                // Validate email addresses
                if (!IsValidEmail(email))
                {
                    throw new ArgumentException($"Invalid recipient email address: {email}");
                }

                if (!IsValidEmail(senderEmail))
                {
                    throw new InvalidOperationException($"Invalid sender email address: {senderEmail}");
                }

                // Create SMTP client with timeout and enhanced security
                using var smtpClient = new SmtpClient(smtpServer)
                {
                    Port = port,
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000, // 30 seconds timeout
                };

                // Create mail message with proper encoding
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName ?? "HireRight Portal"),
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8,
                    Priority = MailPriority.Normal
                };

                mailMessage.To.Add(new MailAddress(email));

                // Add headers for better deliverability
                mailMessage.Headers.Add("Message-ID", $"<{Guid.NewGuid()}@{GetDomainFromEmail(senderEmail)}>");
                mailMessage.Headers.Add("X-Mailer", "HireRight Portal v1.0");
                mailMessage.Headers.Add("List-Unsubscribe", $"<mailto:{senderEmail}?subject=Unsubscribe>");

                // Send email with retry logic
                await SendWithRetry(smtpClient, mailMessage, maxRetries: 3);

                stopwatch.Stop();
                _logger.LogInformation("Email sent successfully to {Email} in {ElapsedMs}ms", email, stopwatch.ElapsedMilliseconds);
            }
            catch (SmtpException smtpEx)
            {
                stopwatch.Stop();
                _logger.LogError(smtpEx, "SMTP error sending email to {Email} after {ElapsedMs}ms. StatusCode: {StatusCode}",
                    email, stopwatch.ElapsedMilliseconds, smtpEx.StatusCode);

                var userFriendlyMessage = GetUserFriendlySmtpError(smtpEx);
                throw new InvalidOperationException($"Failed to send email: {userFriendlyMessage}", smtpEx);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Unexpected error sending email to {Email} after {ElapsedMs}ms", email, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Failed to send email due to an unexpected error: {ex.Message}", ex);
            }
        }

        private async Task SendWithRetry(SmtpClient smtpClient, MailMessage mailMessage, int maxRetries)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await smtpClient.SendMailAsync(mailMessage);
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Email sent successfully on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    }
                    return; // Success
                }
                catch (Exception ex) when (attempt < maxRetries && IsRetriableException(ex))
                {
                    lastException = ex;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    _logger.LogWarning("Email send attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}s. Error: {Error}",
                        attempt, maxRetries, delay.TotalSeconds, ex.Message);

                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    // Non-retriable exception or final attempt
                    _logger.LogError("Email send attempt {Attempt}/{MaxRetries} failed with non-retriable error: {Error}",
                        attempt, maxRetries, ex.Message);
                    throw;
                }
            }

            // If we get here, all retries failed
            throw lastException ?? new InvalidOperationException("All email send attempts failed");
        }

        private static bool IsRetriableException(Exception ex)
        {
            return ex switch
            {
                SmtpException smtpEx => smtpEx.StatusCode switch
                {
                    SmtpStatusCode.MailboxBusy => true,
                    SmtpStatusCode.TransactionFailed => true,
                    SmtpStatusCode.LocalErrorInProcessing => true,
                    SmtpStatusCode.GeneralFailure => true,
                    _ => false
                },
                TimeoutException => true,
                HttpRequestException => true,
                _ => false
            };
        }

        private static string GetUserFriendlySmtpError(SmtpException smtpEx)
        {
            return smtpEx.StatusCode switch
            {
                SmtpStatusCode.MailboxUnavailable => "The email address is not available or does not exist.",
                SmtpStatusCode.MailboxBusy => "The email server is temporarily busy. Please try again later.",
                SmtpStatusCode.TransactionFailed => "Email delivery failed due to server issues.",
                SmtpStatusCode.LocalErrorInProcessing => "Email processing error occurred.",
                SmtpStatusCode.GeneralFailure => "Email service is temporarily unavailable.",
                SmtpStatusCode.CommandUnrecognized => "Email server configuration issue.",
                SmtpStatusCode.SyntaxError => "Email address format issue.",
                _ => $"Email delivery failed (Error: {smtpEx.StatusCode})"
            };
        }

        private static bool IsValidEmail(string email)
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

        private static string GetDomainFromEmail(string email)
        {
            try
            {
                var atIndex = email.LastIndexOf('@');
                return atIndex > 0 && atIndex < email.Length - 1 ? email.Substring(atIndex + 1) : "localhost";
            }
            catch
            {
                return "localhost";
            }
        }
    }
    // Optional: Email template service for more complex scenarios
    public interface IEmailTemplateService
    {
        Task<string> GenerateEmailTemplateAsync(string templateName, object model);
        Task<string> GenerateOtpEmailAsync(string otp, string email, string userName = null);
        Task<string> GenerateWelcomeEmailAsync(string userName, string generatedUserId);
        Task<string> GeneratePasswordResetEmailAsync(string userName, string resetToken);
    }

    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly ILogger<EmailTemplateService> _logger;

        public EmailTemplateService(ILogger<EmailTemplateService> logger)
        {
            _logger = logger;
        }

        public async Task<string> GenerateEmailTemplateAsync(string templateName, object model)
        {
            // Implementation would use a templating engine like RazorEngine or Scriban
            // For now, return a basic template
            await Task.CompletedTask;
            return $"<h1>Email Template: {templateName}</h1><p>Model: {model}</p>";
        }

        public async Task<string> GenerateOtpEmailAsync(string otp, string email, string userName = null)
        {
            await Task.CompletedTask;

            var displayName = !string.IsNullOrEmpty(userName) ? userName : email.Split('@')[0];

            return $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='utf-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <title>Email Verification - HireRight Portal</title>
                <style>
                    body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5; }}
                    .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }}
                    .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px 20px; text-align: center; }}
                    .content {{ padding: 40px 30px; }}
                    .otp-code {{ font-size: 32px; font-weight: bold; color: #667eea; text-align: center; padding: 20px; margin: 20px 0; background: #f8f9fa; border-radius: 8px; letter-spacing: 4px; }}
                    .footer {{ background: #f8f9fa; padding: 20px; text-align: center; color: #666; font-size: 12px; }}
                    .button {{ display: inline-block; padding: 12px 24px; background: #667eea; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>HireRight Portal</h1>
                        <p>Email Verification Required</p>
                    </div>
                    <div class='content'>
                        <h2>Hello {displayName}!</h2>
                        <p>Thank you for registering with HireRight Portal. To complete your registration, please verify your email address using the code below:</p>
                        
                        <div class='otp-code'>{otp}</div>
                        
                        <p style='text-align: center; color: #666;'>This code will expire in 5 minutes</p>
                        
                        <hr style='margin: 30px 0; border: none; border-top: 1px solid #eee;'>
                        
                        <h3>Security Tips:</h3>
                        <ul>
                            <li>This code is only valid for 5 minutes</li>
                            <li>Never share this code with anyone</li>
                            <li>If you didn't request this, please ignore this email</li>
                        </ul>
                    </div>
                    <div class='footer'>
                        <p><strong>HireRight Portal Registration System</strong></p>
                        <p>This is an automated message, please do not reply.</p>
                        <p>&copy; 2024 HireRight Portal. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        public async Task<string> GenerateWelcomeEmailAsync(string userName, string generatedUserId)
        {
            await Task.CompletedTask;

            return $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='utf-8'>
                <title>Welcome to HireRight Portal</title>
                <style>
                    body {{ font-family: Arial, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }}
                    .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
                    .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; }}
                    .content {{ padding: 30px; }}
                    .user-id {{ font-size: 24px; font-weight: bold; color: #667eea; text-align: center; background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0; }}
                    .footer {{ background: #f8f9fa; padding: 20px; text-align: center; color: #666; font-size: 12px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>Welcome to HireRight Portal!</h1>
                    </div>
                    <div class='content'>
                        <h2>Hello {userName}!</h2>
                        <p>Congratulations! Your account has been successfully created.</p>
                        
                        <p>Your unique User ID is:</p>
                        <div class='user-id'>{generatedUserId}</div>
                        
                        <p>You can now:</p>
                        <ul>
                            <li>Complete your profile</li>
                            <li>Browse job opportunities</li>
                            <li>Apply for positions</li>
                            <li>Connect with employers</li>
                        </ul>
                        
                        <p>Get started by logging into your account and completing your profile.</p>
                    </div>
                    <div class='footer'>
                        <p>&copy; 2024 HireRight Portal. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        public async Task<string> GeneratePasswordResetEmailAsync(string userName, string resetToken)
        {
            await Task.CompletedTask;

            return $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='utf-8'>
                <title>Password Reset - HireRight Portal</title>
                <style>
                    body {{ font-family: Arial, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }}
                    .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
                    .header {{ background: #dc3545; color: white; padding: 20px; text-align: center; }}
                    .content {{ padding: 30px; }}
                    .button {{ display: inline-block; padding: 12px 24px; background: #dc3545; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
                    .footer {{ background: #f8f9fa; padding: 20px; text-align: center; color: #666; font-size: 12px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>Password Reset Request</h1>
                    </div>
                    <div class='content'>
                        <h2>Hello {userName},</h2>
                        <p>We received a request to reset your password. If you made this request, click the button below:</p>
                        
                        <div style='text-align: center;'>
                            <a href='#' class='button'>Reset Password</a>
                        </div>
                        
                        <p><strong>This link will expire in 1 hour.</strong></p>
                        
                        <p>If you didn't request a password reset, please ignore this email and your password will remain unchanged.</p>
                        
                        <hr>
                        <p style='font-size: 12px; color: #666;'>Reset Token: {resetToken}</p>
                    </div>
                    <div class='footer'>
                        <p>&copy; 2024 HireRight Portal. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";
        }
    }
}