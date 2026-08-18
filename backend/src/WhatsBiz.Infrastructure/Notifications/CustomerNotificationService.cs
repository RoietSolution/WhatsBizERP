using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.CustomerNotifications;

namespace WhatsBiz.Infrastructure.Notifications;

public sealed class CustomerNotificationService(IConfiguration configuration, IOptionsMonitor<FeatureOptions> features, ILogger<CustomerNotificationService> logger) : ICustomerNotificationService
{
    public const string DefaultWhatsAppTemplate = "Thank you for shopping with {{company_name}}!\n\nInvoice: {{invoice_no}}\nAmount: {{currency}}{{total_amount}}\n\nWe appreciate your business.\nVisit us again!";
    public const string DefaultSmsTemplate = "Thank you for shopping with {{company_name}}. Invoice {{invoice_no}}, Amount {{currency}}{{total_amount}}. We appreciate your business.";
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task QueueInvoice(Guid invoiceId, string eventType, CancellationToken token)
    {
        var featureState = features.CurrentValue;
        if (!featureState.WhatsApp.Enabled && !featureState.Sms.Enabled)
        {
            NotificationLogs.AllChannelsDisabled(logger);
            return;
        }
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(token);
            var settings = await ReadSettings(connection, token);
            if (!settings.Enabled || (eventType == CustomerNotificationEvents.SuccessfulSale && !settings.SuccessfulSale)
                || (eventType == CustomerNotificationEvents.SuccessfulPayment && !settings.SuccessfulPayment)) return;
            await using var query = new SqlCommand("""
                SELECT i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.PaidAmount,i.BalanceAmount,
                       c.CustomerId,c.CustomerName,c.Mobile,c.Currency,co.CompanyName,co.Phone,
                       CONCAT_WS(N', ',NULLIF(co.AddressLine1,N''),NULLIF(co.AddressLine2,N''),NULLIF(co.City,N''),NULLIF(co.State,N''),NULLIF(co.PostalCode,N'')),
                       co.Country,COALESCE((SELECT STRING_AGG(pm.MethodName,N', ') FROM sales.SalesPayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId WHERE p.InvoiceId=i.InvoiceId AND p.Status=N'COMPLETED'),N'')
                FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId
                CROSS JOIN (SELECT TOP(1) CompanyName,Phone,AddressLine1,AddressLine2,City,State,PostalCode,Country FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn) co
                WHERE i.InvoiceId=@invoiceId;
                """, connection);
            query.Parameters.AddWithValue("@invoiceId", invoiceId);
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return;
            var data = new TemplateData(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetGuid(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14));
            await reader.CloseAsync();
            var recipient = PhoneNumberNormalizer.Normalize(data.Mobile, data.CompanyCountry.Equals("India", StringComparison.OrdinalIgnoreCase) ? "91" : null);
            if (settings.WhatsAppEnabled && featureState.WhatsApp.Enabled) await Insert(connection, data, eventType, "WHATSAPP", recipient, settings.WhatsAppTemplate, token);
            else if (settings.WhatsAppEnabled) NotificationLogs.ChannelDisabled(logger, "WhatsApp");
            if (settings.SmsEnabled && featureState.Sms.Enabled) await Insert(connection, data, eventType, "SMS", recipient, settings.SmsTemplate, token);
            else if (settings.SmsEnabled) NotificationLogs.ChannelDisabled(logger, "SMS");
        }
        catch (Exception exception)
        {
            NotificationLogs.QueueFailed(logger, invoiceId, exception);
        }
    }

    public async Task<CustomerNotificationSettingsDto> GetSettings(CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); return await ReadSettings(connection, token);
    }

    public async Task SaveSettings(CustomerNotificationSettingsInput input, string? user, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.WhatsAppTemplate) || input.WhatsAppTemplate.Length > 4000) throw new ArgumentException("WhatsApp template is required and must not exceed 4000 characters.");
        if (string.IsNullOrWhiteSpace(input.SmsTemplate) || input.SmsTemplate.Length > 1000) throw new ArgumentException("SMS template is required and must not exceed 1000 characters.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        foreach (var item in Settings(input))
        {
            await using var command = new SqlCommand("""
                UPDATE admin.ApplicationSettings SET SettingValue=@value,DataType=@type,Category=N'Customer Notifications',ModifiedOn=SYSDATETIMEOFFSET(),ModifiedBy=@user WHERE SettingKey=@key;
                IF @@ROWCOUNT=0 INSERT admin.ApplicationSettings(CompanyId,SettingKey,SettingValue,DataType,Category,ModifiedOn,ModifiedBy)
                VALUES((SELECT TOP(1) CompanyId FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn),@key,@value,@type,N'Customer Notifications',SYSDATETIMEOFFSET(),@user);
                """, connection, transaction);
            command.Parameters.AddWithValue("@key", item.Key); command.Parameters.AddWithValue("@value", item.Value); command.Parameters.AddWithValue("@type", item.Type); command.Parameters.AddWithValue("@user", user ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    public async Task<IReadOnlyCollection<CustomerNotificationHistoryDto>> History(int take, CancellationToken token)
    {
        var rows = new List<CustomerNotificationHistoryDto>(); await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT TOP(@take) n.CustomerNotificationId,n.CreatedOn,c.CustomerName,i.InvoiceNumber,n.EventType,n.Channel,n.Recipient,n.Status,n.AttemptCount,n.ErrorMessage,n.SentOn,n.LastAttemptOn FROM integration.CustomerNotifications n JOIN sales.Customers c ON c.CustomerId=n.CustomerId JOIN sales.SalesInvoices i ON i.InvoiceId=n.DocumentId ORDER BY n.CreatedOn DESC;", connection);
        command.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 500)); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetDateTimeOffset(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10), reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11)));
        return rows;
    }

    public async Task Retry(Guid notificationId, string? user, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var lookup = new SqlCommand("SELECT c.Mobile,co.Country,n.Channel FROM integration.CustomerNotifications n JOIN sales.Customers c ON c.CustomerId=n.CustomerId CROSS JOIN(SELECT TOP(1) Country FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn)co WHERE n.CustomerNotificationId=@id AND n.Status=N'FAILED';", connection);
        lookup.Parameters.AddWithValue("@id", notificationId); await using var reader = await lookup.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new ArgumentException("Only failed notifications can be retried.");
        var mobile = reader.IsDBNull(0) ? null : reader.GetString(0); var country = reader.GetString(1); var channel = reader.GetString(2); await reader.CloseAsync();
        if (!ChannelEnabled(channel)) throw new BusinessRuleException($"{channel} notifications are currently disabled.");
        var recipient = PhoneNumberNormalizer.Normalize(mobile, country.Equals("India", StringComparison.OrdinalIgnoreCase) ? "91" : null);
        if (recipient is null) throw new ArgumentException("The customer mobile number is still missing or invalid.");
        await using var command = new SqlCommand("UPDATE integration.CustomerNotifications SET Recipient=@recipient,Status=N'PENDING',AttemptCount=0,NextAttemptOn=SYSUTCDATETIME(),ErrorMessage=NULL,ProviderMessageId=NULL,SentOn=NULL,ModifiedBy=@user WHERE CustomerNotificationId=@id AND Status=N'FAILED';", connection);
        command.Parameters.AddWithValue("@id", notificationId); command.Parameters.AddWithValue("@recipient", recipient); command.Parameters.AddWithValue("@user", user ?? (object)DBNull.Value); await command.ExecuteNonQueryAsync(token);
    }

    public Task<NotificationConfigurationStatusDto> ConfigurationStatus(CancellationToken token)
    {
        var whatsApp = features.CurrentValue.WhatsApp.Enabled && ProviderConfigured("WhatsApp"); var sms = features.CurrentValue.Sms.Enabled && ProviderConfigured("Sms");
        return Task.FromResult(new NotificationConfigurationStatusDto(whatsApp, sms, whatsApp || sms ? "Enabled and configured channels are available. No test message was sent." : "Channels are disabled or provider configuration is incomplete. No test message was sent."));
    }
    private bool ChannelEnabled(string channel) => channel.Equals("WHATSAPP", StringComparison.OrdinalIgnoreCase) ? features.CurrentValue.WhatsApp.Enabled : channel.Equals("SMS", StringComparison.OrdinalIgnoreCase) && features.CurrentValue.Sms.Enabled;
    private bool ProviderConfigured(string channel) => Uri.TryCreate(configuration[$"CustomerNotifications:{channel}:Endpoint"], UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(configuration[$"CustomerNotifications:{channel}:AccessToken"]);

    private static IEnumerable<(string Key, string Value, string Type)> Settings(CustomerNotificationSettingsInput x)
    {
        yield return ("CustomerNotifications.Enabled", x.Enabled.ToString(CultureInfo.InvariantCulture), "Boolean"); yield return ("CustomerNotifications.WhatsApp.Enabled", x.WhatsAppEnabled.ToString(CultureInfo.InvariantCulture), "Boolean");
        yield return ("CustomerNotifications.Sms.Enabled", x.SmsEnabled.ToString(CultureInfo.InvariantCulture), "Boolean"); yield return ("CustomerNotifications.Events.SuccessfulSale", x.SuccessfulSale.ToString(CultureInfo.InvariantCulture), "Boolean");
        yield return ("CustomerNotifications.Events.SuccessfulPayment", x.SuccessfulPayment.ToString(CultureInfo.InvariantCulture), "Boolean"); yield return ("CustomerNotifications.WhatsApp.Template", x.WhatsAppTemplate, "String"); yield return ("CustomerNotifications.Sms.Template", x.SmsTemplate, "String");
    }
    private static async Task<CustomerNotificationSettingsDto> ReadSettings(SqlConnection connection, CancellationToken token)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); await using var command = new SqlCommand("SELECT SettingKey,SettingValue FROM admin.ApplicationSettings WHERE SettingKey LIKE N'CustomerNotifications.%';", connection); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        bool B(string key) => values.TryGetValue(key, out var value) && bool.TryParse(value, out var result) && result; string S(string key, string fallback) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        return new(B("CustomerNotifications.Enabled"), B("CustomerNotifications.WhatsApp.Enabled"), B("CustomerNotifications.Sms.Enabled"), B("CustomerNotifications.Events.SuccessfulSale"), B("CustomerNotifications.Events.SuccessfulPayment"), S("CustomerNotifications.WhatsApp.Template", DefaultWhatsAppTemplate), S("CustomerNotifications.Sms.Template", DefaultSmsTemplate));
    }
    private static async Task Insert(SqlConnection connection, TemplateData data, string eventType, string channel, string? recipient, string template, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM integration.CustomerNotifications WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@documentId AND DocumentType=N'SALES_INVOICE' AND CustomerId=@customerId AND Channel=@channel AND EventType=@eventType)
            INSERT integration.CustomerNotifications(CustomerId,DocumentId,DocumentType,EventType,Channel,Recipient,MessageTemplate,Message,Status,ErrorMessage,NextAttemptOn)
            VALUES(@customerId,@documentId,N'SALES_INVOICE',@eventType,@channel,@recipient,@template,@message,@status,CASE WHEN @recipient IS NULL THEN N'Customer mobile number is missing or invalid.' END,CASE WHEN @recipient IS NULL THEN NULL ELSE SYSUTCDATETIME() END);
            """, connection);
        command.Parameters.AddWithValue("@customerId", data.CustomerId); command.Parameters.AddWithValue("@documentId", data.InvoiceId); command.Parameters.AddWithValue("@eventType", eventType); command.Parameters.AddWithValue("@channel", channel); command.Parameters.AddWithValue("@recipient", recipient ?? (object)DBNull.Value); command.Parameters.AddWithValue("@template", template); command.Parameters.AddWithValue("@message", MessageTemplateRenderer.Render(template, data)); command.Parameters.AddWithValue("@status", recipient is null ? "FAILED" : "PENDING"); await command.ExecuteNonQueryAsync(token);
    }
    internal sealed record TemplateData(Guid InvoiceId, string InvoiceNumber, DateTimeOffset InvoiceDate, decimal TotalAmount, decimal PaidAmount, decimal BalanceAmount, Guid CustomerId, string CustomerName, string? Mobile, string Currency, string CompanyName, string? StorePhone, string StoreAddress, string CompanyCountry, string PaymentMethod);
}

internal static partial class PhoneNumberNormalizer
{
    [GeneratedRegex("[^0-9+]")] private static partial Regex NonPhoneCharacters();
    public static string? Normalize(string? value, string? defaultCountryCode) { if (string.IsNullOrWhiteSpace(value)) return null; var clean = NonPhoneCharacters().Replace(value.Trim(), ""); if (clean.StartsWith("00", StringComparison.Ordinal)) clean = "+" + clean[2..]; if (clean.StartsWith('+')) return clean.Length is >= 9 and <= 16 && clean[1..].All(char.IsDigit) ? clean : null; var digits = new string(clean.Where(char.IsDigit).ToArray()); if (digits.Length == 10 && defaultCountryCode is not null) return "+" + defaultCountryCode + digits; if (digits.Length is >= 11 and <= 15) return "+" + digits; return null; }
}

internal static partial class MessageTemplateRenderer
{
    [GeneratedRegex("{{\\s*([a-z_]+)\\s*}}", RegexOptions.IgnoreCase)] private static partial Regex Placeholder();
    public static string Render(string template, CustomerNotificationService.TemplateData d) { var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["company_name"] = d.CompanyName, ["customer_name"] = d.CustomerName, ["invoice_no"] = d.InvoiceNumber, ["invoice_date"] = d.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture), ["total_amount"] = d.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture), ["paid_amount"] = d.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture), ["balance_amount"] = d.BalanceAmount.ToString("0.00", CultureInfo.InvariantCulture), ["payment_method"] = d.PaymentMethod, ["currency"] = d.Currency == "INR" ? "₹" : d.Currency + " ", ["store_phone"] = d.StorePhone ?? "", ["store_address"] = d.StoreAddress }; return Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : ""); }
}

internal sealed record ProviderResult(bool Succeeded, string? ProviderMessageId, string? ErrorMessage);
internal interface ICustomerMessageProvider { string Channel { get; } Task<ProviderResult> Send(string recipient, string message, Guid notificationId, CancellationToken token); }
internal abstract class HttpCustomerMessageProvider(IHttpClientFactory clients, IConfiguration configuration, IOptionsMonitor<FeatureOptions> features, ILogger logger, string channel) : ICustomerMessageProvider
{
    public string Channel => channel;
    public async Task<ProviderResult> Send(string recipient, string message, Guid notificationId, CancellationToken token)
    {
        var enabled = channel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase) ? features.CurrentValue.WhatsApp.Enabled : features.CurrentValue.Sms.Enabled;
        if (!enabled)
        {
            NotificationLogs.ChannelDisabled(logger, channel);
            return new(false, null, $"FEATURE_DISABLED: {channel} notifications are currently disabled.");
        }
        var endpoint = configuration[$"CustomerNotifications:{channel}:Endpoint"]; var accessToken = configuration[$"CustomerNotifications:{channel}:AccessToken"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(accessToken)) return new(false, null, "NOT_CONFIGURED: provider endpoint or access token is missing.");
        try { using var request = new HttpRequestMessage(HttpMethod.Post, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken); request.Content = JsonContent.Create(new { recipient, message, referenceId = notificationId }); using var response = await clients.CreateClient("CustomerNotifications").SendAsync(request, token); if (!response.IsSuccessStatusCode) { NotificationLogs.ProviderFailed(logger, channel, notificationId, $"HTTP_{(int)response.StatusCode}"); return new(false, null, $"Provider returned HTTP {(int)response.StatusCode}."); } var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token); var id = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("messageId", out var property) ? property.GetString() : null; return new(true, id, null); }
        catch (Exception exception) { NotificationLogs.ProviderFailed(logger, channel, notificationId, exception.GetType().Name); return new(false, null, "Provider request failed."); }
    }
}
internal sealed class WhatsAppProvider(IHttpClientFactory clients, IConfiguration configuration, IOptionsMonitor<FeatureOptions> features, ILogger<WhatsAppProvider> logger) : HttpCustomerMessageProvider(clients, configuration, features, logger, "WhatsApp");
internal sealed class SmsProvider(IHttpClientFactory clients, IConfiguration configuration, IOptionsMonitor<FeatureOptions> features, ILogger<SmsProvider> logger) : HttpCustomerMessageProvider(clients, configuration, features, logger, "Sms");

internal static partial class NotificationLogs
{
    [LoggerMessage(1001, LogLevel.Error, "Customer notification queueing failed for invoice {InvoiceId}; the financial transaction remains committed.")]
    public static partial void QueueFailed(ILogger logger, Guid invoiceId, Exception exception);

    [LoggerMessage(1002, LogLevel.Warning, "{Channel} notification {NotificationId} failed: {ErrorType}")]
    public static partial void ProviderFailed(ILogger logger, string channel, Guid notificationId, string errorType);

    [LoggerMessage(1003, LogLevel.Error, "Customer notification worker iteration failed.")]
    public static partial void WorkerFailed(ILogger logger, Exception exception);

    [LoggerMessage(1004, LogLevel.Debug, "{Channel} notification skipped because the feature is disabled.")]
    public static partial void ChannelDisabled(ILogger logger, string channel);

    [LoggerMessage(1005, LogLevel.Debug, "Customer notification queueing skipped because WhatsApp and SMS features are disabled.")]
    public static partial void AllChannelsDisabled(ILogger logger);
}
