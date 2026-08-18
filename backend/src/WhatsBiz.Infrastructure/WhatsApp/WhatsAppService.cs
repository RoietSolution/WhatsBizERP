using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsApp;

public sealed partial class WhatsAppService(IConfiguration configuration,
    IDataProtectionProvider dataProtectionProvider, IFeatureService features, IWhatsAppCommerceProviderResolver providers,
    ILogger<WhatsAppService> logger) : IWhatsAppService
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1");
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token)
    { var row = await ReadByTenant(tenantId, token); return row is null ? Empty() : ToDto(row); }

    public async Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token)
    {
        ValidateInput(input);
        var existing = await ReadByTenant(tenantId, token);
        var isMock = input.ProviderMode.Equals(WhatsAppProviderModes.Mock, StringComparison.OrdinalIgnoreCase);
        var access = ProtectReplacement(input.AccessToken, existing?.AccessTokenProtected, "access token", isMock);
        var verify = ProtectReplacement(input.WebhookVerifyToken, existing?.WebhookVerifyTokenProtected, "webhook verify token", isMock);
        var appSecret = ProtectReplacement(input.AppSecret, existing?.AppSecretProtected, "app secret", isMock);
        var status = input.IsEnabled ? WhatsAppConnectionStatuses.Configured : WhatsAppConnectionStatuses.Disabled;
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppConfigurations SET ProviderMode=@mode,MetaAppId=@appId,WhatsAppBusinessAccountId=@waba,PhoneNumberId=@phone,
              AccessTokenProtected=@access,WebhookVerifyTokenProtected=@verify,AppSecretProtected=@appSecret,
              ApiVersion=@version,TestRecipientNumber=@recipient,IsEnabled=@enabled,ConnectionStatus=@status,LastValidatedOn=NULL,LastError=NULL,
              ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor WHERE TenantId=@tenant;
            IF @@ROWCOUNT=0 INSERT integration.WhatsAppConfigurations
              (WhatsAppConfigurationId,TenantId,ProviderMode,MetaAppId,WhatsAppBusinessAccountId,PhoneNumberId,AccessTokenProtected,
               WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,TestRecipientNumber,IsEnabled,ConnectionStatus,CreatedBy)
              VALUES(NEWID(),@tenant,@mode,@appId,@waba,@phone,@access,@verify,@appSecret,@version,@recipient,@enabled,@status,@actor);
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@mode", input.ProviderMode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@appId", (object?)input.MetaAppId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@waba", (object?)input.WhatsAppBusinessAccountId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@phone", (object?)input.PhoneNumberId?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@access", (object?)access ?? DBNull.Value);
        command.Parameters.AddWithValue("@verify", (object?)verify ?? DBNull.Value); command.Parameters.AddWithValue("@appSecret", (object?)appSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("@version", string.IsNullOrWhiteSpace(input.ApiVersion) ? DBNull.Value : input.ApiVersion.Trim()); command.Parameters.AddWithValue("@enabled", input.IsEnabled);
        command.Parameters.AddWithValue("@recipient", string.IsNullOrWhiteSpace(input.TestRecipientNumber) ? DBNull.Value : NonDigits().Replace(input.TestRecipientNumber, string.Empty));
        command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@actor", actor ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(token); return await GetConfigurationAsync(tenantId, token);
    }

    public async Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token) ?? throw new BusinessRuleException("WhatsApp is not configured.");
        if (!row.IsEnabled) throw new BusinessRuleException("WhatsApp configuration is disabled.");
        if (row.ProviderMode == WhatsAppProviderModes.Mock) throw new BusinessRuleException("MOCK mode does not require a Meta connection validation.");
        if (row.ProviderMode != WhatsAppProviderModes.MetaTest) throw new BusinessRuleException("LIVE provider validation is not implemented in WC-003.");
        if (row.AccessTokenProtected is null || row.ApiVersion is null || row.WabaId is null || row.PhoneNumberId is null)
            return await RecordValidation(row, false, null, null, "Meta configuration is incomplete.", token);
        var protectedToken = string.IsNullOrWhiteSpace(replacementAccessToken) ? row.AccessTokenProtected : protector.Protect(replacementAccessToken.Trim());
        string accessToken;
        try { accessToken = protector.Unprotect(protectedToken); }
        catch (CryptographicException) { return await RecordValidation(row, false, null, null, "Stored credential cannot be decrypted. Replace the access token.", token); }
        var result = await providers.Resolve(row.ProviderMode).ValidateConnectionAsync(
            new(row.ApiVersion, row.WabaId, row.PhoneNumberId, accessToken), token);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(replacementAccessToken)) await UpdateAccessToken(tenantId, protectedToken, token);
        return await RecordValidation(row, result.Succeeded, result.DisplayPhoneNumber, result.BusinessDisplayName,
            result.SafeMessage ?? (result.Succeeded ? "Connection validated successfully." : "Meta validation failed."), token);
    }

    public async Task<WhatsAppTestMessageResult> SendTestMessageAsync(Guid tenantId, SendWhatsAppTestMessageInput input, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token) ?? throw new BusinessRuleException("WhatsApp is not configured.");
        if (!row.IsEnabled || row.ProviderMode != WhatsAppProviderModes.MetaTest) throw new BusinessRuleException("An enabled META_TEST configuration is required.");
        if (row.AccessTokenProtected is null || row.ApiVersion is null || row.PhoneNumberId is null) throw new BusinessRuleException("META_TEST configuration is incomplete.");
        var recipient = NonDigits().Replace(string.IsNullOrWhiteSpace(input.RecipientNumber) ? row.TestRecipientNumber ?? string.Empty : input.RecipientNumber, string.Empty);
        if (!Recipient().IsMatch(recipient)) throw new BusinessRuleException("Recipient must be a valid international WhatsApp number including country code.");
        var accessToken = UnprotectOrNull(row.AccessTokenProtected) ?? throw new BusinessRuleException("Stored credential cannot be decrypted. Replace the access token.");
        var message = string.IsNullOrWhiteSpace(input.Message) ? "WhatsBiz Meta Test connection successful." : input.Message.Trim();
        if (message.Length > 1000) throw new BusinessRuleException("Test message cannot exceed 1000 characters.");
        var result = await providers.Resolve(row.ProviderMode).SendTestMessageAsync(new(row.ApiVersion, row.PhoneNumberId, accessToken, recipient, message), token);
        if (result.Succeeded && result.ProviderMessageId is not null)
            await StoreEvent(row.TenantId, $"outbound:{result.ProviderMessageId}", "MESSAGE_SENT", "OUTBOUND", row.PhoneNumberId, recipient, null, result.AttemptedAt, token, result.ProviderMessageId);
        WhatsAppLogs.TransportProcessed(logger, row.ProviderMode, row.TenantId, "MESSAGE_SENT", result.ProviderMessageId ?? "none", "OUTBOUND", result.Succeeded ? "ACCEPTED" : "FAILED");
        return new(result.Succeeded, result.ProviderMessageId, result.AttemptedAt, result.SafeMessage);
    }

    public async Task<WhatsAppMetaTestDiagnosticsDto> GetDiagnosticsAsync(Guid tenantId, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) ReceivedOn,MetaMessageId FROM integration.WhatsAppWebhookEvents
            WHERE TenantId=@tenant AND EventType='MESSAGE_SENT' ORDER BY ReceivedOn DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        DateTimeOffset? lastSentOn = null; string? lastSentId = null;
        await using (var reader = await command.ExecuteReaderAsync(token))
            if (await reader.ReadAsync(token)) { lastSentOn = reader.GetDateTimeOffset(0); lastSentId = reader.IsDBNull(1) ? null : reader.GetString(1); }
        const string path = "/api/whatsapp/webhook";
        var configuredBase = configuration["WhatsApp:PublicBaseUrl"];
        var callback = Uri.TryCreate(configuredBase, UriKind.Absolute, out var baseUri) && baseUri.Scheme == Uri.UriSchemeHttps
            ? new Uri(baseUri, path).ToString() : null;
        return new(path, callback, row?.LastWebhookVerifiedOn, row?.LastWebhookReceivedOn,
            row?.LastWebhookEventType, row?.LastWebhookMetaMessageId, row?.LastWebhookReceivedOn is not null,
            row?.DuplicateWebhookCount ?? 0, lastSentOn, lastSentId);
    }

    public async Task<string?> VerifyWebhookAsync(string mode, string verifyToken, string challenge, CancellationToken token)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) || string.IsNullOrEmpty(verifyToken)) return null;
        foreach (var row in await ReadEnabled(token))
            if (row.ProviderMode != WhatsAppProviderModes.Mock
                && await features.IsEnabledAsync(row.TenantId, WhatsBiz.Application.Common.Features.FeatureKeys.WhatsAppCommerce, token)
                && FixedTimeEquals(UnprotectOrNull(row.WebhookVerifyTokenProtected), verifyToken))
            { await MarkWebhookVerified(row.TenantId, token); return challenge; }
        return null;
    }

    public async Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        string? phoneNumberId = null; string? wabaId = null; var events = new List<WebhookTransportEvent>();
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement;
            if (!root.TryGetProperty("object", out var objectNode) || objectNode.GetString() != "whatsapp_business_account") return false;
            if (root.TryGetProperty("entry", out var entries)) foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("id", out var entryId)) wabaId ??= entryId.GetString();
                if (!entry.TryGetProperty("changes", out var changes)) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;
                    if (value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var phone)) phoneNumberId ??= phone.GetString();
                    if (value.TryGetProperty("messages", out var messages)) foreach (var message in messages.EnumerateArray())
                    {
                        var id = String(message, "id"); if (string.IsNullOrWhiteSpace(id)) continue;
                        events.Add(new($"message:{id}", id, "MESSAGE_RECEIVED", "INBOUND", String(message, "from"),
                            String(message, "type"), null, Timestamp(message)));
                    }
                    if (value.TryGetProperty("statuses", out var statuses)) foreach (var status in statuses.EnumerateArray())
                    {
                        var id = String(status, "id"); var state = String(status, "status"); if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(state)) continue;
                        events.Add(new($"status:{id}:{state}", id, "MESSAGE_STATUS", "OUTBOUND", String(status, "recipient_id"),
                            null, state, Timestamp(status)));
                    }
                }
            }
        }
        catch (JsonException) { return false; }
        var row = !string.IsNullOrWhiteSpace(phoneNumberId) ? await ReadByPhone(phoneNumberId, token)
            : !string.IsNullOrWhiteSpace(wabaId) ? await ReadByWaba(wabaId, token) : null;
        if (row is null || row.ProviderMode != WhatsAppProviderModes.MetaTest || !row.IsEnabled || !string.Equals(row.WabaId, wabaId, StringComparison.Ordinal)
            || !await features.IsEnabledAsync(row.TenantId, WhatsBiz.Application.Common.Features.FeatureKeys.WhatsAppCommerce, token)) return false;
        var appSecret = UnprotectOrNull(row.AppSecretProtected);
        if (string.IsNullOrWhiteSpace(appSecret) || !ValidSignature(signature, body.Span, appSecret)) return false;
        foreach (var item in events)
        {
            var inserted = await StoreEvent(row.TenantId, item.EventKey, item.EventType, item.Direction,
                phoneNumberId ?? row.PhoneNumberId, item.ContactNumber, item.Status, item.EventTimestamp, token, item.MetaMessageId, item.MessageType);
            WhatsAppLogs.TransportProcessed(logger, row.ProviderMode, row.TenantId, item.EventType, item.MetaMessageId,
                item.Direction, inserted ? "RECORDED" : "DUPLICATE");
            await MarkWebhookReceived(row.TenantId, item, !inserted, token);
        }
        WhatsAppLogs.WebhookReceived(logger, row.TenantId, phoneNumberId ?? row.PhoneNumberId ?? "unknown",
            string.Join(',', events.Select(x => x.EventType).Distinct(StringComparer.Ordinal))); return true;
    }

    private async Task<bool> StoreEvent(Guid tenantId, string eventKey, string eventType, string direction,
        string? phoneNumberId, string? contactNumber, string? status, DateTimeOffset eventTimestamp,
        CancellationToken token, string? metaMessageId = null, string? messageType = null)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                INSERT integration.WhatsAppWebhookEvents(WhatsAppWebhookEventId,TenantId,ProviderMode,EventKey,
                  MetaMessageId,EventType,Direction,PhoneNumberId,ContactNumber,MessageType,MessageStatus,
                  EventTimestamp,ProcessingStatus,ReceivedOn)
                VALUES(NEWID(),@tenant,'META_TEST',@key,@message,@type,@direction,@phone,@contact,@messageType,@status,
                  @timestamp,'PROCESSED',SYSUTCDATETIME());
                """, connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@key", eventKey);
            command.Parameters.AddWithValue("@message", (object?)metaMessageId ?? (eventKey.StartsWith("outbound:", StringComparison.Ordinal) ? eventKey[9..] : DBNull.Value));
            command.Parameters.AddWithValue("@type", eventType); command.Parameters.AddWithValue("@direction", direction);
            command.Parameters.AddWithValue("@phone", (object?)phoneNumberId ?? DBNull.Value); command.Parameters.AddWithValue("@contact", (object?)contactNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@messageType", (object?)messageType ?? DBNull.Value); command.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@timestamp", eventTimestamp); await command.ExecuteNonQueryAsync(token); return true;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627) { return false; }
    }

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static DateTimeOffset Timestamp(JsonElement element) => element.TryGetProperty("timestamp", out var value)
        && long.TryParse(value.GetString(), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;

    private async Task MarkWebhookVerified(Guid tenantId, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET LastWebhookVerifiedOn=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", c); q.Parameters.AddWithValue("@tenant", tenantId); await q.ExecuteNonQueryAsync(token); }
    private async Task MarkWebhookReceived(Guid tenantId, WebhookTransportEvent item, bool duplicate, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET LastWebhookReceivedOn=SYSUTCDATETIME(),LastWebhookEventType=@type,LastWebhookMetaMessageId=@message,DuplicateWebhookCount=DuplicateWebhookCount+@duplicate,ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", c); q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@type", item.EventType); q.Parameters.AddWithValue("@message", item.MetaMessageId); q.Parameters.AddWithValue("@duplicate", duplicate ? 1 : 0); await q.ExecuteNonQueryAsync(token); }

    private async Task<WhatsAppConnectionResult> RecordValidation(ConfigRow row, bool success, string? phone, string? name, string message, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow; await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET DisplayPhoneNumber=@phone,BusinessDisplayName=@name,ConnectionStatus=@status,LastValidatedOn=@now,LastError=@error,ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", connection);
        command.Parameters.AddWithValue("@phone", phone ?? (object)DBNull.Value); command.Parameters.AddWithValue("@name", name ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@status", success ? WhatsAppConnectionStatuses.Connected : WhatsAppConnectionStatuses.Error); command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@error", success ? DBNull.Value : message); command.Parameters.AddWithValue("@tenant", row.TenantId); await command.ExecuteNonQueryAsync(token);
        return new(success, success ? WhatsAppConnectionStatuses.Connected : WhatsAppConnectionStatuses.Error, phone, name, now, message);
    }

    private async Task UpdateAccessToken(Guid tenantId, string protectedToken, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET AccessTokenProtected=@token,ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", c); q.Parameters.AddWithValue("@token", protectedToken); q.Parameters.AddWithValue("@tenant", tenantId); await q.ExecuteNonQueryAsync(token); }
    private Task<ConfigRow?> ReadByTenant(Guid tenantId, CancellationToken token) => ReadOne("TenantId", tenantId, token);
    private Task<ConfigRow?> ReadByPhone(string phone, CancellationToken token) => ReadOne("PhoneNumberId", phone, token);
    private Task<ConfigRow?> ReadByWaba(string wabaId, CancellationToken token) => ReadOne("WhatsAppBusinessAccountId", wabaId, token);
    private async Task<ConfigRow?> ReadOne(string column, object value, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand($"SELECT TOP(1) TenantId,ProviderMode,MetaAppId,WhatsAppBusinessAccountId,PhoneNumberId,DisplayPhoneNumber,BusinessDisplayName,AccessTokenProtected,WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,TestRecipientNumber,IsEnabled,ConnectionStatus,LastValidatedOn,LastError,LastWebhookVerifiedOn,LastWebhookReceivedOn,LastWebhookEventType,LastWebhookMetaMessageId,DuplicateWebhookCount FROM integration.WhatsAppConfigurations WHERE {column}=@value;", c); q.Parameters.AddWithValue("@value", value); await using var r = await q.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? Map(r) : null; }
    private async Task<IReadOnlyCollection<ConfigRow>> ReadEnabled(CancellationToken token)
    { var rows = new List<ConfigRow>(); await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("SELECT TenantId,ProviderMode,MetaAppId,WhatsAppBusinessAccountId,PhoneNumberId,DisplayPhoneNumber,BusinessDisplayName,AccessTokenProtected,WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,TestRecipientNumber,IsEnabled,ConnectionStatus,LastValidatedOn,LastError,LastWebhookVerifiedOn,LastWebhookReceivedOn,LastWebhookEventType,LastWebhookMetaMessageId,DuplicateWebhookCount FROM integration.WhatsAppConfigurations WHERE IsEnabled=1;", c); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) rows.Add(Map(r)); return rows; }
    private static ConfigRow Map(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetBoolean(12), r.GetString(13), r.IsDBNull(14) ? null : r.GetDateTimeOffset(14), r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? null : r.GetDateTimeOffset(16), r.IsDBNull(17) ? null : r.GetDateTimeOffset(17), r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? null : r.GetString(19), r.GetInt64(20));
    private static WhatsAppConfigurationDto ToDto(ConfigRow x) => new(x.ProviderMode, x.MetaAppId, x.WabaId, x.PhoneNumberId, x.DisplayPhoneNumber, x.BusinessDisplayName, x.ApiVersion, x.TestRecipientNumber, x.IsEnabled, x.ConnectionStatus, x.LastValidatedOn, x.LastError, x.AccessTokenProtected is not null, x.WebhookVerifyTokenProtected is not null, x.AppSecretProtected is not null);
    private static WhatsAppConfigurationDto Empty() => new(WhatsAppProviderModes.Mock, null, null, null, null, null, null, null, false, WhatsAppConnectionStatuses.NotConfigured, null, null, false, false, false);
    private string? ProtectReplacement(string? value, string? current, string label, bool optional) { if (!string.IsNullOrWhiteSpace(value)) return protector.Protect(value.Trim()); if (!string.IsNullOrWhiteSpace(current)) return current; if (optional) return null; throw new BusinessRuleException($"The {label} is required."); }
    private string? UnprotectOrNull(string? value) { if (value is null) return null; try { return protector.Unprotect(value); } catch (CryptographicException) { return null; } }
    private static void ValidateInput(SaveWhatsAppConfigurationInput x) { if (!WhatsAppProviderModes.All.Contains(x.ProviderMode)) throw new BusinessRuleException("Provider mode must be MOCK, META_TEST, or LIVE."); if (x.ProviderMode.Equals(WhatsAppProviderModes.Mock, StringComparison.OrdinalIgnoreCase)) return; if (string.IsNullOrWhiteSpace(x.MetaAppId) || !Digits().IsMatch(x.MetaAppId.Trim()) || string.IsNullOrWhiteSpace(x.WhatsAppBusinessAccountId) || !Digits().IsMatch(x.WhatsAppBusinessAccountId.Trim()) || string.IsNullOrWhiteSpace(x.PhoneNumberId) || !Digits().IsMatch(x.PhoneNumberId.Trim())) throw new BusinessRuleException("Meta App ID, WABA ID, and phone number ID must contain only digits."); if (string.IsNullOrWhiteSpace(x.ApiVersion) || !Version().IsMatch(x.ApiVersion.Trim())) throw new BusinessRuleException("API version must use Meta's vNN.N format."); if (!string.IsNullOrWhiteSpace(x.TestRecipientNumber) && !Recipient().IsMatch(NonDigits().Replace(x.TestRecipientNumber, string.Empty))) throw new BusinessRuleException("Test recipient must be a valid international WhatsApp number including country code."); }
    private static bool FixedTimeEquals(string? a, string b) { if (a is null) return false; var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b); return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y); }
    internal static bool ValidSignature(string? signature, ReadOnlySpan<byte> body, string secret) { if (signature is null || !signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false; try { var supplied = Convert.FromHexString(signature[7..]); var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body); return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected); } catch (FormatException) { return false; } }
    [GeneratedRegex("^[0-9]+$")] private static partial Regex Digits();
    [GeneratedRegex("[^0-9]")] private static partial Regex NonDigits();
    [GeneratedRegex("^[1-9][0-9]{7,14}$")] private static partial Regex Recipient();
    [GeneratedRegex("^v[0-9]{1,3}\\.[0-9]+$")] private static partial Regex Version();
    private sealed record ConfigRow(Guid TenantId, string ProviderMode, string? MetaAppId, string? WabaId, string? PhoneNumberId, string? DisplayPhoneNumber, string? BusinessDisplayName, string? AccessTokenProtected, string? WebhookVerifyTokenProtected, string? AppSecretProtected, string? ApiVersion, string? TestRecipientNumber, bool IsEnabled, string ConnectionStatus, DateTimeOffset? LastValidatedOn, string? LastError, DateTimeOffset? LastWebhookVerifiedOn, DateTimeOffset? LastWebhookReceivedOn, string? LastWebhookEventType, string? LastWebhookMetaMessageId, long DuplicateWebhookCount);
    private sealed record WebhookTransportEvent(string EventKey, string MetaMessageId, string EventType,
        string Direction, string? ContactNumber, string? MessageType, string? Status, DateTimeOffset EventTimestamp);
}

internal static partial class WhatsAppLogs
{
    [LoggerMessage(2101, LogLevel.Warning, "Meta WhatsApp connection validation failed for tenant {TenantId} with HTTP {StatusCode}.")]
    public static partial void MetaValidationFailed(ILogger logger, Guid tenantId, int statusCode);
    [LoggerMessage(2102, LogLevel.Information, "WhatsApp webhook received for tenant {TenantId}, phone number ID {PhoneNumberId}, fields {Fields}.")]
    public static partial void WebhookReceived(ILogger logger, Guid tenantId, string phoneNumberId, string fields);
    [LoggerMessage(2103, LogLevel.Information, "WhatsApp transport {ProviderMode} tenant {TenantId} event {EventType} message {MetaMessageId} direction {Direction} result {Result}.")]
    public static partial void TransportProcessed(ILogger logger, string providerMode, Guid tenantId, string eventType,
        string metaMessageId, string direction, string result);
}
