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
    public string? LogoUrl { get; set; }
    public string? FeatureImageUrl { get; set; }
}

public sealed class CaptchaOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Turnstile";
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }
    public string VerificationUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

public sealed record DemoRequestEmail(string Recipient, string Subject, string Body, bool IsBodyHtml = false);

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
            IsBodyHtml = email.IsBodyHtml
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
                BuildRequesterBody(request, settings.Email.LogoUrl, settings.Email.FeatureImageUrl),
                true), token);
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

    private static string BuildRequesterBody(DemoRequestDetail x, string? logoUrl, string? featureImageUrl)
    {
        var name = WebUtility.HtmlEncode(x.Name);
        var reference = WebUtility.HtmlEncode(x.ReferenceNo);
        var logo = BuildImage(logoUrl, "KhataDhari", "width:180px;max-width:55%;height:auto;display:block;margin:0 auto;");
        var features = BuildImage(featureImageUrl, "KhataDhari All-in-One Retail ERP and WhatsApp Commerce Features", "width:100%;max-width:720px;height:auto;display:block;margin:24px auto 0;");
        return $"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
<body style="margin:0;padding:0;background:#f4f7f5;font-family:Arial,Helvetica,sans-serif;color:#24312b;">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#f4f7f5;">
    <tr><td align="center" style="padding:24px 12px;">
      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:720px;background:#ffffff;border-radius:14px;overflow:hidden;">
        <tr><td align="center" style="padding:28px 24px 18px;background:#ffffff;">{logo}</td></tr>
        <tr><td style="padding:8px 32px 32px;">
          <h1 style="margin:0 0 18px;font-size:26px;line-height:1.3;color:#17633c;text-align:center;">Your demo request has been received</h1>
          <p style="margin:0 0 14px;font-size:16px;line-height:1.65;">Hello {name},</p>
          <p style="margin:0 0 14px;font-size:16px;line-height:1.65;">Thank you for requesting a KhataDhari demo. We have received your request successfully.</p>
          <p style="margin:0 0 22px;font-size:16px;line-height:1.65;"><strong>Reference:</strong> {reference}</p>
          <div style="background:#eef8f1;border-left:4px solid #23915a;border-radius:8px;padding:18px 20px;">
            <h2 style="margin:0 0 10px;font-size:20px;line-height:1.35;color:#17633c;">What happens next?</h2>
            <p style="margin:0 0 8px;font-size:15px;line-height:1.6;">A member of the KhataDhari team will contact you shortly to understand your business requirements.</p>
            <p style="margin:0;font-size:15px;line-height:1.6;">We will arrange a convenient time to show you how KhataDhari can support your retail operations, billing, inventory and WhatsApp commerce.</p>
          </div>
          {features}
          <p style="margin:26px 0 0;font-size:16px;line-height:1.6;">Regards,<br><strong>KhataDhari Team</strong></p>
        </td></tr>
        <tr><td align="center" style="padding:18px 24px;background:#17633c;color:#ffffff;font-size:12px;line-height:1.5;">KhataDhari &ndash; Retail ERP &amp; WhatsApp Commerce</td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
    }

    private static string BuildImage(string? configuredUrl, string altText, string style)
    {
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo)) return string.Empty;
        return $"<img src=\"{WebUtility.HtmlEncode(uri.AbsoluteUri)}\" alt=\"{WebUtility.HtmlEncode(altText)}\" style=\"{style}\">";
    }
}
