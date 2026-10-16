using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.DemoRequests;

namespace WhatsBiz.Infrastructure.Notifications;

public sealed class StorefrontFeedbackNotificationOptions
{
    public const string SectionName = "StorefrontFeedbackNotifications";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
    public int LeaseMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public SmtpOptions Email { get; set; } = new();
    public string[] CustomSmtpAllowedHosts { get; set; } = [];
    public string? RetailerRecipient { get; set; }
    public WhatsAppFeedbackDeliveryOptions WhatsApp { get; set; } = new();
}

public sealed class WhatsAppFeedbackDeliveryOptions
{
    public bool Enabled { get; set; }
    public bool RequireExplicitOptIn { get; set; } = true;
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class StorefrontFeedbackNotificationWorker(
    IConfiguration configuration,
    IOptions<StorefrontFeedbackNotificationOptions> options,
    ILogger<StorefrontFeedbackNotificationWorker> logger) : BackgroundService
{
    private readonly StorefrontFeedbackNotificationOptions settings = options.Value;
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!settings.Enabled || !await ProcessOne(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 2, 60)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                NotificationLogs.WorkerFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOne(CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var claim = new SqlCommand("""
;WITH candidate AS
(
    SELECT TOP(1) * FROM commerce.StorefrontFeedbackNotificationDeliveries WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE (Status=N'PENDING' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc<=SYSUTCDATETIME()))
       OR (Status=N'PROCESSING' AND LeaseUntilUtc<SYSUTCDATETIME())
    ORDER BY CreatedAtUtc
)
UPDATE candidate SET Status=N'PROCESSING',AttemptCount=AttemptCount+1,LastAttemptAtUtc=SYSUTCDATETIME(),LeaseUntilUtc=DATEADD(minute,@lease,SYSUTCDATETIME())
OUTPUT inserted.DeliveryId,inserted.Channel,inserted.RecipientReference,inserted.TemplateKey,inserted.AttemptCount,inserted.NotificationId;
""", connection, transaction);
        claim.Parameters.AddWithValue("@lease", Math.Clamp(settings.LeaseMinutes, 1, 30));
        await using var reader = await claim.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { await reader.CloseAsync(); await transaction.CommitAsync(token); return false; }
        var item = new WorkItem(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetGuid(5));
        await reader.CloseAsync();
        await transaction.CommitAsync(token);

        var result = await Deliver(item, token);
        await using var update = new SqlCommand("""
UPDATE commerce.StorefrontFeedbackNotificationDeliveries
SET Status=@status,ProviderMessageReference=@provider,ErrorCategory=@category,ErrorMessage=@message,LeaseUntilUtc=NULL,
    NextAttemptAtUtc=CASE WHEN @status=N'PENDING' THEN DATEADD(minute,CASE WHEN AttemptCount=1 THEN 1 WHEN AttemptCount=2 THEN 5 WHEN AttemptCount=3 THEN 25 ELSE 60 END,SYSUTCDATETIME()) ELSE NULL END
WHERE DeliveryId=@id AND Status=N'PROCESSING';
""", connection);
        update.Parameters.AddWithValue("@id", item.Id); update.Parameters.AddWithValue("@status", result.Status);
        update.Parameters.AddWithValue("@provider", (object?)result.ProviderReference ?? DBNull.Value);
        update.Parameters.AddWithValue("@category", (object?)result.Category ?? DBNull.Value);
        update.Parameters.AddWithValue("@message", (object?)result.Message ?? DBNull.Value);
        await update.ExecuteNonQueryAsync(token);
        return true;
    }

    private async Task<DeliveryResult> Deliver(WorkItem item, CancellationToken token)
    {
        if (string.Equals(item.Channel, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
            return new("SKIPPED", null, "NOT_CONFIGURED", "WhatsApp complaint delivery requires explicit customer opt-in and an approved template mapping.");
        if (!string.Equals(item.Channel, "EMAIL", StringComparison.OrdinalIgnoreCase))
            return new("SKIPPED", null, "UNSUPPORTED_CHANNEL", "The delivery channel is not enabled.");
        if (!settings.Email.Enabled)
            return new("SKIPPED", null, "DISABLED", "Email complaint notifications are disabled.");
        var recipient = item.RecipientReference;
        if (string.IsNullOrWhiteSpace(recipient)) recipient = settings.RetailerRecipient;
        if (string.IsNullOrWhiteSpace(recipient) || !MailAddress.TryCreate(recipient, out _))
            return new("SKIPPED", null, "NO_VALID_RECIPIENT", "No valid email recipient is configured.");
        if (string.IsNullOrWhiteSpace(settings.Email.Host) || string.IsNullOrWhiteSpace(settings.Email.FromAddress))
            return new("SKIPPED", null, "NOT_CONFIGURED", "SMTP email configuration is incomplete.");
        try
        {
            var content = await ReadTemplate(item, token);
            using var message = new MailMessage { From = new MailAddress(settings.Email.FromAddress, settings.Email.FromName), To = { recipient }, Subject = content.Subject, Body = content.Body, IsBodyHtml = true };
            using var client = new SmtpClient(settings.Email.Host, settings.Email.Port) { EnableSsl = settings.Email.EnableSsl };
            if (!string.IsNullOrWhiteSpace(settings.Email.Username)) client.Credentials = new NetworkCredential(settings.Email.Username, settings.Email.Password);
            await client.SendMailAsync(message, token);
            return new("SENT", $"smtp:{item.Id:N}", null, null);
        }
        catch (SmtpException ex)
        {
            return new(item.AttemptCount >= settings.MaxAttempts ? "FAILED" : "PENDING", null, "SMTP", SafeMessage(ex));
        }
        catch (InvalidOperationException ex)
        {
            return new("SKIPPED", null, "CONFIGURATION", ex.Message);
        }
    }

    private async Task<(string Subject, string Body)> ReadTemplate(WorkItem item, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT n.Title,n.Message,t.TicketReference,t.Subject FROM commerce.StorefrontFeedbackNotificationDeliveries d JOIN commerce.StorefrontFeedbackNotifications n ON n.NotificationId=d.NotificationId JOIN commerce.StorefrontFeedbackTickets t ON t.TicketId=d.TicketId WHERE d.DeliveryId=@id AND d.TenantId=t.TenantId;", connection);
        command.Parameters.AddWithValue("@id", item.Id);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Feedback notification data is unavailable.");
        var title = WebUtility.HtmlEncode(reader.GetString(0)); var text = WebUtility.HtmlEncode(reader.GetString(1)); var reference = WebUtility.HtmlEncode(reader.GetString(2)); var subject = WebUtility.HtmlEncode(reader.GetString(3));
        return ($"{title} - {reference}", $"<p><strong>{reference}</strong></p><p>{subject}</p><p>{text}</p>");
    }

    private static string SafeMessage(SmtpException exception) => exception.StatusCode == SmtpStatusCode.MailboxUnavailable ? "The recipient mailbox was unavailable." : "The email provider rejected or could not deliver the message.";
    private sealed record WorkItem(Guid Id, string Channel, string? RecipientReference, string TemplateKey, int AttemptCount, Guid NotificationId);
    private sealed record DeliveryResult(string Status, string? ProviderReference, string? Category, string? Message);
}
