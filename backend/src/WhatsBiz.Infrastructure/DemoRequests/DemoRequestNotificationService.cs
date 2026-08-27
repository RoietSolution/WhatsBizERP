using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.DemoRequests;

namespace WhatsBiz.Infrastructure.DemoRequests;

public sealed class DemoRequestOptions
{
    public const string SectionName = "DemoRequests";
    public int DuplicateWindowMinutes { get; set; } = 5;
    public string? WhatsAppContactNumber { get; set; }
    public SmtpOptions Email { get; set; } = new();
    public CaptchaOptions Captcha { get; set; } = new();
}

public sealed class SmtpOptions
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; } = "KhataDhari Website";
    public string? SupportAddress { get; set; }
}

public sealed class CaptchaOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Turnstile";
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }
    public string VerificationUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

public sealed class DemoRequestNotificationService(IOptions<DemoRequestOptions> options, ILogger<DemoRequestNotificationService> logger) : IDemoRequestNotificationService
{
    private static readonly Action<ILogger, long, string, Exception?> NotificationFailed =
        LoggerMessage.Define<long, string>(LogLevel.Error, new EventId(3101, nameof(NotificationFailed)), "Demo request notification failed for lead {LeadId} ({ReferenceNo})");
    private readonly DemoRequestOptions settings = options.Value;

    public async Task<string> NotifyAsync(DemoRequestDetail request, CancellationToken token)
    {
        if (!settings.Email.Enabled) return "SKIPPED";
        try
        {
            var smtp = settings.Email;
            if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress) || string.IsNullOrWhiteSpace(smtp.SupportAddress))
                throw new InvalidOperationException("Demo request email configuration is incomplete.");
            using var message = new MailMessage
            {
                From = new MailAddress(smtp.FromAddress, smtp.FromName),
                Subject = $"New KhataDhari Demo Request - {request.ReferenceNo}",
                Body = BuildBody(request),
                IsBodyHtml = false
            };
            message.To.Add(smtp.SupportAddress);
            using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
            if (!string.IsNullOrWhiteSpace(smtp.Username)) client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
            await client.SendMailAsync(message, token);
            return "SENT";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotificationFailed(logger, request.Id, request.ReferenceNo, exception);
            return "FAILED";
        }
    }

    private static string BuildBody(DemoRequestDetail x)
    {
        var india = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var submitted = TimeZoneInfo.ConvertTime(x.CreatedOn, india).ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.GetCultureInfo("en-IN"));
        return $"""
New KhataDhari Demo Request

Reference: {x.ReferenceNo}
Name: {x.Name}
Mobile: {x.Mobile}
Email: {x.Email ?? "-"}
Business: {x.BusinessName ?? "-"}
Business Type: {x.BusinessType ?? "-"}
City: {x.City ?? "-"}
Source: {x.Source}

Message:
{x.Message ?? "-"}

Submitted On:
{submitted}
""";
    }
}
