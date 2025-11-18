using Microsoft.AspNetCore.Mvc;

namespace Demo.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string message);
}