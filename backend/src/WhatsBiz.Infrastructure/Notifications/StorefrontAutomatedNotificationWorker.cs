using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.Notifications;

internal sealed class StorefrontAutomatedNotificationWorker(
    IConfiguration configuration,
    IOptions<StorefrontFeedbackNotificationOptions> feedbackOptions,
    IWhatsAppCommerceProviderResolver providers,
    IDataProtectionProvider protection,
    ILogger<StorefrontAutomatedNotificationWorker> logger) : BackgroundService
{
    private readonly StorefrontFeedbackNotificationOptions settings = feedbackOptions.Value;
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var didWork = await ReconcileOne(stoppingToken) || await ProcessOne(stoppingToken);
                if (!didWork) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 5, 120)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                NotificationLogs.WorkerFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task<bool> ReconcileOne(CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var select = new SqlCommand("""
            SELECT TOP(1) r.AvailabilityRequestId,r.TenantId,r.ProductId,r.CustomerId,r.ContactMobile,p.ProductName,t.Name,t.TenantKey,
                   c.Mobile,r.ConsentGiven,sc.RestockWhatsAppNotificationsEnabled,sc.RestockWhatsAppTemplateName
            FROM commerce.ProductAvailabilityRequests r WITH (UPDLOCK,READPAST,ROWLOCK)
            JOIN master.Products p ON p.ProductId=r.ProductId AND p.TenantId=r.TenantId
            JOIN core.Tenants t ON t.TenantId=r.TenantId AND t.IsActive=1
            JOIN commerce.StorefrontConfigurations sc ON sc.TenantId=r.TenantId
            LEFT JOIN sales.Customers c ON c.CustomerId=r.CustomerId AND c.TenantId=r.TenantId
            WHERE r.RequestStatus=N'ACTIVE' AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1
              AND EXISTS (SELECT 1 FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=b.TenantId
                          WHERE b.TenantId=r.TenantId AND b.ProductId=r.ProductId AND w.IsActive=1 AND w.IsDeleted=0
                          GROUP BY b.TenantId,b.ProductId HAVING SUM(b.QuantityOnHand-b.QuantityReserved)>0)
              AND sc.RestockWhatsAppNotificationsEnabled=1
              AND (r.ConsentGiven=1 OR r.CustomerId IS NULL)
            ORDER BY r.RequestedAtUtc;
            """, connection, transaction);
        await using var reader = await select.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { await reader.CloseAsync(); await transaction.CommitAsync(token); return false; }
        var request = new RestockRequest(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8), !reader.IsDBNull(9) && reader.GetBoolean(9), reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
        await reader.CloseAsync();
        await using var update = new SqlCommand("UPDATE commerce.ProductAvailabilityRequests SET RequestStatus=N'NOTIFICATION_PENDING',LastAvailabilityCheckedAtUtc=SYSUTCDATETIME() WHERE AvailabilityRequestId=@request AND TenantId=@tenant AND RequestStatus=N'ACTIVE';", connection, transaction);
        update.Parameters.AddWithValue("@request", request.RequestId); update.Parameters.AddWithValue("@tenant", request.TenantId); await update.ExecuteNonQueryAsync(token);
        var recipient = request.CustomerId is not null && request.CustomerMobile is not null ? request.CustomerMobile : request.ContactMobile;
        await using var insert = new SqlCommand("""
            INSERT commerce.StorefrontAutomatedNotificationDeliveries(DeliveryId,TenantId,EventType,Channel,EntityId,RecipientReference,TemplateKey,Status,NextAttemptAtUtc)
            SELECT @id,@tenant,N'PRODUCT_RESTOCK',N'WHATSAPP',@entity,@recipient,@template,N'PENDING',SYSUTCDATETIME()
            WHERE NOT EXISTS(SELECT 1 FROM commerce.StorefrontAutomatedNotificationDeliveries WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND EventType=N'PRODUCT_RESTOCK' AND Channel=N'WHATSAPP' AND EntityId=@entity);
            """, connection, transaction);
        insert.Parameters.AddWithValue("@id", Guid.NewGuid()); insert.Parameters.AddWithValue("@tenant", request.TenantId); insert.Parameters.AddWithValue("@entity", request.RequestId);
        insert.Parameters.AddWithValue("@recipient", (object?)recipient ?? DBNull.Value); insert.Parameters.AddWithValue("@template", (object?)request.Template ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token); return true;
    }

    private async Task<bool> ProcessOne(CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var claim = new SqlCommand("""
            ;WITH candidate AS (SELECT TOP(1) * FROM commerce.StorefrontAutomatedNotificationDeliveries WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE (Status=N'PENDING' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc<=SYSUTCDATETIME())) OR (Status=N'PROCESSING' AND LeaseUntilUtc<SYSUTCDATETIME()) ORDER BY CreatedAtUtc)
            UPDATE candidate SET Status=N'PROCESSING',AttemptCount=AttemptCount+1,LastAttemptAtUtc=SYSUTCDATETIME(),LeaseUntilUtc=DATEADD(minute,5,SYSUTCDATETIME())
            OUTPUT inserted.DeliveryId,inserted.TenantId,inserted.EventType,inserted.Channel,inserted.EntityId,inserted.RecipientReference,inserted.TemplateKey,inserted.AttemptCount;
            """, connection, transaction);
        await using var reader = await claim.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { await reader.CloseAsync(); await transaction.CommitAsync(token); return false; }
        var item = new WorkItem(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7));
        await reader.CloseAsync(); await transaction.CommitAsync(token);
        var result = await Deliver(item, token);
        await using var update = new SqlCommand("""
            UPDATE commerce.StorefrontAutomatedNotificationDeliveries SET Status=@status,SentAtUtc=CASE WHEN @status=N'SENT' THEN SYSUTCDATETIME() ELSE SentAtUtc END,
              ProviderMessageReference=@provider,ErrorCategory=@category,ErrorMessage=@message,LeaseUntilUtc=NULL,
              NextAttemptAtUtc=CASE WHEN @status=N'PENDING' THEN DATEADD(minute,CASE WHEN AttemptCount=1 THEN 1 WHEN AttemptCount=2 THEN 5 WHEN AttemptCount=3 THEN 25 ELSE 60 END,SYSUTCDATETIME()) ELSE NULL END
            WHERE DeliveryId=@id AND Status=N'PROCESSING';
            """, connection);
        update.Parameters.AddWithValue("@status", result.Status); update.Parameters.AddWithValue("@provider", (object?)result.ProviderReference ?? DBNull.Value);
        update.Parameters.AddWithValue("@category", (object?)result.Category ?? DBNull.Value); update.Parameters.AddWithValue("@message", (object?)result.Message ?? DBNull.Value); update.Parameters.AddWithValue("@id", item.DeliveryId);
        await update.ExecuteNonQueryAsync(token);
        if (item.EventType == "PRODUCT_RESTOCK" && item.Channel == "WHATSAPP" && result.MarkRequestNotified)
        {
            await using var requestUpdate = new SqlCommand("UPDATE commerce.ProductAvailabilityRequests SET RequestStatus=N'NOTIFIED',NotifiedAtUtc=SYSUTCDATETIME() WHERE AvailabilityRequestId=@id AND TenantId=@tenant AND RequestStatus=N'NOTIFICATION_PENDING';", connection);
            requestUpdate.Parameters.AddWithValue("@id", item.EntityId); requestUpdate.Parameters.AddWithValue("@tenant", item.TenantId); await requestUpdate.ExecuteNonQueryAsync(token);
        }
        return true;
    }

    private async Task<DeliveryResult> Deliver(WorkItem item, CancellationToken token)
    {
        return item.EventType switch
        {
            "STOREFRONT_ORDER_CREATED" when item.Channel == "EMAIL" => await DeliverOrderEmail(item, token),
            "PRODUCT_RESTOCK" when item.Channel == "WHATSAPP" => await DeliverRestock(item, token),
            _ => new("SKIPPED", null, "UNSUPPORTED_EVENT", "The notification event is not supported.", false)
        };
    }

    private async Task<DeliveryResult> DeliverOrderEmail(WorkItem item, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.Recipient) || !MailAddress.TryCreate(item.Recipient, out _)) return new("SKIPPED", null, "NO_VALID_RECIPIENT", "No valid retailer email is configured.", false);
        try
        {
            var smtp = await LoadEmailSettings(item.TenantId, token);
            if (!smtp.Enabled) return new("SKIPPED", null, "DISABLED", "Email notifications are disabled.", false);
            if (!smtp.IsConfigured) return new("SKIPPED", null, "NOT_CONFIGURED", "SMTP email configuration is incomplete.", false);
            if (smtp.IsCustom && !await IsAllowedSmtpHost(smtp.Host!, token)) return new("SKIPPED", null, "UNSAFE_SMTP_HOST", "The configured SMTP host is not allowed.", false);
            await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                SELECT i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,COALESCE(c.CustomerName,N'Customer'),COALESCE(c.Mobile,N''),w.FulfillmentMethod,w.PaymentType,t.Name,
                  (SELECT STRING_AGG(CONCAT(p.ProductName,N' x ',CONVERT(nvarchar(30),x.Quantity)),N'<br/>') FROM sales.SalesInvoiceItems x JOIN master.Products p ON p.ProductId=x.ProductId AND p.TenantId=i.TenantId WHERE x.InvoiceId=i.InvoiceId),
                  COALESCE((SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=i.TenantId AND cp.InvoiceId=i.InvoiceId ORDER BY cp.AttemptNumber DESC),N'NOT_STARTED')
                FROM sales.SalesInvoices i JOIN core.Tenants t ON t.TenantId=i.TenantId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId
                LEFT JOIN integration.WhatsAppCommerceOrders w ON w.InvoiceId=i.InvoiceId AND w.TenantId=i.TenantId WHERE i.TenantId=@tenant AND i.InvoiceId=@order;
                """, connection);
            command.Parameters.AddWithValue("@tenant", item.TenantId); command.Parameters.AddWithValue("@order", item.EntityId);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return new("FAILED", null, "ORDER_NOT_FOUND", "The order snapshot is unavailable.", false);
            var order = WebUtility.HtmlEncode(reader.GetString(0)); var date = reader.GetDateTimeOffset(1).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture); var total = reader.GetDecimal(2).ToString("0.00", CultureInfo.InvariantCulture);
            var status = WebUtility.HtmlEncode(reader.GetString(3)); var customer = WebUtility.HtmlEncode(reader.GetString(4)); var mobile = WebUtility.HtmlEncode(reader.GetString(5));
            var fulfilment = WebUtility.HtmlEncode(reader.IsDBNull(6) ? "DELIVERY" : reader.GetString(6)); var payment = WebUtility.HtmlEncode(reader.IsDBNull(7) ? "PENDING" : reader.GetString(7));
            var store = WebUtility.HtmlEncode(reader.GetString(8)); var items = reader.IsDBNull(9) ? "No item summary available." : reader.GetString(9); var paymentStatus = WebUtility.HtmlEncode(reader.GetString(10));
            var body = $"<p><strong>New storefront order #{order}</strong></p><p>{store}</p><p>{date}<br/>Customer: {customer} ({mobile})<br/>Fulfilment: {fulfilment}<br/>Payment: {payment} / {paymentStatus}<br/>Status: {status}<br/>Total: INR {total}</p><p>{items}</p>";
            using var message = new MailMessage { From = new MailAddress(smtp.FromAddress!, smtp.FromName), Subject = $"New storefront order #{order}", Body = body, IsBodyHtml = true };
            message.To.Add(item.Recipient); if (!string.IsNullOrWhiteSpace(smtp.ReplyTo)) message.ReplyToList.Add(new MailAddress(smtp.ReplyTo));
            using var client = new SmtpClient(smtp.Host!, smtp.Port) { EnableSsl = smtp.SecurityMode is "STARTTLS" or "SSL", Timeout = 30000 };
            if (!string.IsNullOrWhiteSpace(smtp.Username)) client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
            await client.SendMailAsync(message, token); return new("SENT", $"smtp:{item.DeliveryId:N}", null, null, false);
        }
        catch (SmtpException ex) { return new(item.AttemptCount >= settings.MaxAttempts ? "FAILED" : "PENDING", null, ClassifySmtp(ex), "The email provider could not deliver the notification.", false); }
        catch (InvalidOperationException) { return new("SKIPPED", null, "CONFIGURATION", "SMTP email configuration is invalid.", false); }
    }

    private async Task<SmtpDeliverySettings> LoadEmailSettings(Guid tenantId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT AutomatedEmailMode,NotificationSenderDisplayName,NotificationReplyToEmail,CustomSmtpHost,CustomSmtpPort,CustomSmtpSecurityMode,CustomSmtpUsername,CustomSmtpPasswordProtected,CustomSmtpFromEmail FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId); await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return new(false, null, 0, null, null, null, null, null, null, false, false);
        var mode = reader.IsDBNull(0) ? "KHATADHARI_DEFAULT" : reader.GetString(0);
        if (mode.Equals("CUSTOM_SMTP", StringComparison.OrdinalIgnoreCase))
        {
            var protectedPassword = reader.IsDBNull(7) ? null : reader.GetString(7); string? password = null;
            if (!string.IsNullOrWhiteSpace(protectedPassword)) password = protection.CreateProtector("WhatsBiz.Storefront.CustomSmtpPassword.v61").Unprotect(protectedPassword);
            var storedHost = reader.IsDBNull(3) ? null : reader.GetString(3);
            var host = "smtp.gmail.com"; var port = 587; var security = "STARTTLS";
            var username = reader.IsDBNull(6) ? null : reader.GetString(6); var from = reader.IsDBNull(8) ? null : reader.GetString(8);
            var isSupportedGmail = string.IsNullOrWhiteSpace(storedHost) || string.Equals(storedHost, host, StringComparison.OrdinalIgnoreCase);
            return new(true, host, port, security, username, password, from, reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), isSupportedGmail && !string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(from) && string.Equals(username, from, StringComparison.OrdinalIgnoreCase), true);
        }
        return new(settings.Email.Enabled, settings.Email.Host, settings.Email.Port, settings.Email.EnableSsl ? "STARTTLS" : "NONE", settings.Email.Username, settings.Email.Password, settings.Email.FromAddress, reader.IsDBNull(1) ? settings.Email.FromName : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), !string.IsNullOrWhiteSpace(settings.Email.Host) && !string.IsNullOrWhiteSpace(settings.Email.FromAddress), false);
    }
    private async Task<bool> IsAllowedSmtpHost(string host, CancellationToken token)
    {
        if (!settings.CustomSmtpAllowedHosts.Any(x => string.Equals(x.Trim(), host, StringComparison.OrdinalIgnoreCase))) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var literal) && IsPrivate(literal)) return false;
        var addresses = await Dns.GetHostAddressesAsync(host, token); return addresses.Length > 0 && addresses.All(a => !IsPrivate(a));
    }
    private static bool IsPrivate(IPAddress ip) { if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true; if (ip.IsIPv4MappedToIPv6) ip=ip.MapToIPv4(); var b=ip.GetAddressBytes(); return ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && (b[0]==10 || b[0]==127 || b[0]==169&&b[1]==254 || b[0]==172&&b[1]>=16&&b[1]<=31 || b[0]==192&&b[1]==168); }
    private static string ClassifySmtp(SmtpException ex) { var text=ex.Message.ToLowerInvariant(); return text.Contains("553")||text.Contains("sender") ? "SENDER_REJECTED" : text.Contains("authentication")||text.Contains("credentials") ? "AUTHENTICATION_FAILED" : text.Contains("tls")||text.Contains("ssl")||text.Contains("certificate") ? "TLS_FAILED" : text.Contains("timeout") ? "TIMEOUT" : "SMTP"; }

    private async Task<DeliveryResult> DeliverRestock(WorkItem item, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT r.ContactMobile,r.ConsentGiven,r.ProductId,p.ProductName,t.Name,t.TenantKey,sc.RestockWhatsAppNotificationsEnabled,sc.RestockWhatsAppTemplateName,c.Mobile,w.ProviderMode,w.ApiVersion,w.PhoneNumberId,w.AccessTokenProtected,w.TestRecipientNumber,
                   CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM inventory.InventoryBalances b JOIN inventory.Warehouses wh ON wh.WarehouseId=b.WarehouseId AND wh.TenantId=b.TenantId WHERE b.TenantId=r.TenantId AND b.ProductId=r.ProductId AND wh.IsActive=1 AND wh.IsDeleted=0 GROUP BY b.TenantId,b.ProductId HAVING SUM(b.QuantityOnHand-b.QuantityReserved)>0) THEN 1 ELSE 0 END)
            FROM commerce.ProductAvailabilityRequests r JOIN master.Products p ON p.ProductId=r.ProductId AND p.TenantId=r.TenantId JOIN core.Tenants t ON t.TenantId=r.TenantId
            JOIN commerce.StorefrontConfigurations sc ON sc.TenantId=r.TenantId LEFT JOIN sales.Customers c ON c.CustomerId=r.CustomerId AND c.TenantId=r.TenantId
            LEFT JOIN integration.WhatsAppConfigurations w ON w.TenantId=r.TenantId AND w.IsEnabled=1
            WHERE r.AvailabilityRequestId=@id AND r.TenantId=@tenant;
            """, connection);
        command.Parameters.AddWithValue("@id", item.EntityId); command.Parameters.AddWithValue("@tenant", item.TenantId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return new("FAILED", null, "REQUEST_NOT_FOUND", "The availability request is unavailable.", false);
        var mobile = reader.IsDBNull(8) ? (reader.IsDBNull(0) ? null : reader.GetString(0)) : reader.GetString(8); var optIn = reader.GetBoolean(1);
        var enabled = reader.GetBoolean(6); var template = reader.IsDBNull(7) ? null : reader.GetString(7); var mode = reader.IsDBNull(9) ? null : reader.GetString(9);
        var api = reader.IsDBNull(10) ? null : reader.GetString(10); var phoneId = reader.IsDBNull(11) ? null : reader.GetString(11); var protectedToken = reader.IsDBNull(12) ? null : reader.GetString(12); var testRecipient = reader.IsDBNull(13) ? null : reader.GetString(13);
        var productId = reader.GetGuid(2); var product = reader.GetString(3); var store = reader.GetString(4); var key = reader.GetString(5); var stockAvailable = reader.GetBoolean(14); await reader.CloseAsync();
        if (!enabled) return new("SKIPPED", null, "DISABLED", "Restock notifications are disabled for this retailer.", false);
        if (!stockAvailable) return new("PENDING", null, "STOCK_UNAVAILABLE", "The product is unavailable again; delivery will be retried when stock returns.", false);
        if (!optIn || string.IsNullOrWhiteSpace(mobile)) return new("SKIPPED", null, "NO_CONSENT", "The customer has not provided notification consent.", false);
        if (string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(phoneId) || string.IsNullOrWhiteSpace(protectedToken) || string.IsNullOrWhiteSpace(template)) return new("SKIPPED", null, "NOT_CONFIGURED", "WhatsApp provider or approved template is not configured.", false);
        if (mode.Equals("MOCK", StringComparison.OrdinalIgnoreCase)) return new("SKIPPED", null, "MOCK_NOT_DELIVERED", "MOCK mode does not represent an external delivery.", false);
        var recipient = mode.Equals("META_TEST", StringComparison.OrdinalIgnoreCase) ? testRecipient : mobile;
        if (string.IsNullOrWhiteSpace(recipient)) return new("SKIPPED", null, "NO_RECIPIENT", "No eligible WhatsApp recipient is configured.", false);
        try
        {
            var accessToken = protection.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1").Unprotect(protectedToken);
            var link = $"{(configuration["Storefront:BaseUrl"] ?? "https://khatadhari.com").TrimEnd('/')}/store/{Uri.EscapeDataString(key)}/products/{productId:N}";
            var message = $"Good news! {product} is back in stock at {store}. View it here: {link}";
            var sent = await providers.Resolve(mode).SendTransactionalAsync(new(api, phoneId, accessToken, recipient, "PRODUCT_RESTOCK", template, "en_US", message, [product, store, link]), token);
            return sent.Succeeded ? new("SENT", sent.ProviderMessageId, null, null, true) : new(item.AttemptCount >= settings.MaxAttempts ? "FAILED" : "PENDING", null, "PROVIDER", sent.SafeMessage, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(item.AttemptCount >= settings.MaxAttempts ? "FAILED" : "PENDING", null, "PROVIDER", "The WhatsApp provider could not deliver the notification.", false); }
    }

    private sealed record RestockRequest(Guid RequestId, Guid TenantId, Guid ProductId, Guid? CustomerId, string? ContactMobile, string ProductName, string StoreName, string TenantKey, string? CustomerMobile, bool OptIn, bool Enabled, string? Template);
    private sealed record WorkItem(Guid DeliveryId, Guid TenantId, string EventType, string Channel, Guid EntityId, string? Recipient, string? Template, int AttemptCount);
    private sealed record DeliveryResult(string Status, string? ProviderReference, string? Category, string? Message, bool MarkRequestNotified);
    private sealed record SmtpDeliverySettings(bool Enabled, string? Host, int Port, string? SecurityMode, string? Username, string? Password, string? FromAddress, string? FromName, string? ReplyTo, bool IsConfigured, bool IsCustom);
}
