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

public sealed record DemoRequestEmail(string Recipient, string Subject, string Body);

public interface IDemoRequestEmailSender
{
    Task SendAsync(DemoRequestEmail email, CancellationToken token);
}

public sealed class SmtpDemoRequestEmailSender(IOptions<DemoRequestOptions> options) : IDemoRequestEmailSender
{
    private readonly SmtpOptions settings = options.Value.Email;

    public async Task SendAsync(DemoRequestEmail email, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Demo request SMTP configuration is incomplete.");

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = email.Subject,
            Body = email.Body,
            IsBodyHtml = false
        };
        message.To.Add(email.Recipient);

        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message, token);
    }
}

public sealed class DemoRequestNotificationService(
    IOptions<DemoRequestOptions> options,
    IDemoRequestEmailSender emailSender,
    ILogger<DemoRequestNotificationService> logger) : IDemoRequestNotificationService
{
    private static readonly Action<ILogger, long, string, string, Exception?> InternalNotificationFailed =
        LoggerMessage.Define<long, string, string>(LogLevel.Error, new EventId(3101, nameof(InternalNotificationFailed)), "Demo request internal notification failed for lead {LeadId} ({ReferenceNo}); failure type: {FailureType}");
    private static readonly Action<ILogger, long, string, string, Exception?> RequesterAcknowledgementFailed =
        LoggerMessage.Define<long, string, string>(LogLevel.Error, new EventId(3102, nameof(RequesterAcknowledgementFailed)), "Demo request requester acknowledgement failed for lead {LeadId} ({ReferenceNo}); failure type: {FailureType}");
    private readonly DemoRequestOptions settings = options.Value;

    public async Task<string> NotifyAsync(DemoRequestDetail request, CancellationToken token)
    {
        if (!settings.Email.Enabled) return "SKIPPED";

        var internalStatus = await SendInternalNotificationAsync(request, token);
        if (!string.IsNullOrWhiteSpace(request.Email))
            await SendRequesterAcknowledgementAsync(request, token);
        return internalStatus;
    }

    private async Task<string> SendInternalNotificationAsync(DemoRequestDetail request, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.Email.SupportAddress))
                throw new InvalidOperationException("Demo request support recipient is not configured.");

            await emailSender.SendAsync(new(
                settings.Email.SupportAddress,
                $"New KhataDhari Demo Request - {request.ReferenceNo}",
                BuildInternalBody(request)), token);
            return "SENT";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InternalNotificationFailed(logger, request.Id, request.ReferenceNo, exception.GetType().Name, null);
            return "FAILED";
        }
    }

    private async Task SendRequesterAcknowledgementAsync(DemoRequestDetail request, CancellationToken token)
    {
        try
        {
            await emailSender.SendAsync(new(
                request.Email!,
                "Your KhataDhari Demo Request Has Been Received",
                BuildRequesterBody(request)), token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RequesterAcknowledgementFailed(logger, request.Id, request.ReferenceNo, exception.GetType().Name, null);
        }
    }

    private static string BuildInternalBody(DemoRequestDetail x)
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

    private static string BuildRequesterBody(DemoRequestDetail x) => $"""
Hello {x.Name},

Thank you for requesting a KhataDhari demo. We have received your request successfully.

Reference: {x.ReferenceNo}

The KhataDhari team will contact you shortly to understand your requirements and arrange the demo.

Regards,
KhataDhari Team
""";
}
