using System.Net.Http.Headers;
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

namespace WhatsBiz.Infrastructure.WhatsApp;

public sealed partial class WhatsAppService(IConfiguration configuration, IHttpClientFactory clients,
    IDataProtectionProvider dataProtectionProvider, IFeatureService features, ILogger<WhatsAppService> logger) : IWhatsAppService
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
            UPDATE integration.WhatsAppConfigurations SET ProviderMode=@mode,WhatsAppBusinessAccountId=@waba,PhoneNumberId=@phone,
              AccessTokenProtected=@access,WebhookVerifyTokenProtected=@verify,AppSecretProtected=@appSecret,
              ApiVersion=@version,IsEnabled=@enabled,ConnectionStatus=@status,LastValidatedOn=NULL,LastError=NULL,
              ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor WHERE TenantId=@tenant;
            IF @@ROWCOUNT=0 INSERT integration.WhatsAppConfigurations
              (WhatsAppConfigurationId,TenantId,ProviderMode,WhatsAppBusinessAccountId,PhoneNumberId,AccessTokenProtected,
               WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,IsEnabled,ConnectionStatus,CreatedBy)
              VALUES(NEWID(),@tenant,@mode,@waba,@phone,@access,@verify,@appSecret,@version,@enabled,@status,@actor);
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@mode", input.ProviderMode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@waba", (object?)input.WhatsAppBusinessAccountId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@phone", (object?)input.PhoneNumberId?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@access", (object?)access ?? DBNull.Value);
        command.Parameters.AddWithValue("@verify", (object?)verify ?? DBNull.Value); command.Parameters.AddWithValue("@appSecret", (object?)appSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("@version", string.IsNullOrWhiteSpace(input.ApiVersion) ? DBNull.Value : input.ApiVersion.Trim()); command.Parameters.AddWithValue("@enabled", input.IsEnabled);
        command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@actor", actor ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(token); return await GetConfigurationAsync(tenantId, token);
    }

    public async Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token) ?? throw new BusinessRuleException("WhatsApp is not configured.");
        if (!row.IsEnabled) throw new BusinessRuleException("WhatsApp configuration is disabled.");
        if (row.ProviderMode == WhatsAppProviderModes.Mock) throw new BusinessRuleException("MOCK mode does not require a Meta connection validation.");
        if (row.AccessTokenProtected is null || row.ApiVersion is null || row.WabaId is null || row.PhoneNumberId is null)
            return await RecordValidation(row, false, null, null, "Meta configuration is incomplete.", token);
        var protectedToken = string.IsNullOrWhiteSpace(replacementAccessToken) ? row.AccessTokenProtected : protector.Protect(replacementAccessToken.Trim());
        string accessToken;
        try { accessToken = protector.Unprotect(protectedToken); }
        catch (CryptographicException) { return await RecordValidation(row, false, null, null, "Stored credential cannot be decrypted. Replace the access token.", token); }
        try
        {
            var url = $"https://graph.facebook.com/{Uri.EscapeDataString(row.ApiVersion)}/{Uri.EscapeDataString(row.WabaId)}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating";
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            { WhatsAppLogs.MetaValidationFailed(logger, tenantId, (int)response.StatusCode); return await RecordValidation(row, false, null, null, $"Meta rejected the configuration (HTTP {(int)response.StatusCode}). Check the IDs, API version, token, and permissions.", token); }
            await using var stream = await response.Content.ReadAsStreamAsync(token); using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var match = document.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault(x => x.TryGetProperty("id", out var id) && id.GetString() == row.PhoneNumberId);
            if (match.ValueKind == JsonValueKind.Undefined) return await RecordValidation(row, false, null, null, "The phone number ID does not belong to the configured WhatsApp Business Account.", token);
            var phone = match.TryGetProperty("display_phone_number", out var phoneNode) ? phoneNode.GetString() : null;
            var name = match.TryGetProperty("verified_name", out var nameNode) ? nameNode.GetString() : null;
            if (!string.IsNullOrWhiteSpace(replacementAccessToken)) await UpdateAccessToken(tenantId, protectedToken, token);
            return await RecordValidation(row, true, phone, name, "Connection validated successfully.", token);
        }
        catch (HttpRequestException) { return await RecordValidation(row, false, null, null, "Meta could not be reached. Check network connectivity and try again.", token); }
        catch (JsonException) { return await RecordValidation(row, false, null, null, "Meta returned an unexpected response.", token); }
    }

    public async Task<string?> VerifyWebhookAsync(string mode, string verifyToken, string challenge, CancellationToken token)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) || string.IsNullOrEmpty(verifyToken)) return null;
        foreach (var row in await ReadEnabled(token))
            if (row.ProviderMode != WhatsAppProviderModes.Mock
                && await features.IsEnabledAsync(row.TenantId, WhatsBiz.Application.Common.Features.FeatureKeys.WhatsAppCommerce, token)
                && FixedTimeEquals(UnprotectOrNull(row.WebhookVerifyTokenProtected), verifyToken)) return challenge;
        return null;
    }

    public async Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        string? phoneNumberId = null; string? wabaId = null; var fields = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement;
            if (!root.TryGetProperty("object", out var objectNode) || objectNode.GetString() != "whatsapp_business_account") return false;
            if (root.TryGetProperty("entry", out var entries)) foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("id", out var id)) wabaId ??= id.GetString();
                if (!entry.TryGetProperty("changes", out var changes)) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (change.TryGetProperty("field", out var field)) fields.Add(field.GetString() ?? "unknown");
                    if (change.TryGetProperty("value", out var value) && value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var phone)) phoneNumberId ??= phone.GetString();
                }
            }
        }
        catch (JsonException) { return false; }
        var row = !string.IsNullOrWhiteSpace(phoneNumberId) ? await ReadByPhone(phoneNumberId, token)
            : !string.IsNullOrWhiteSpace(wabaId) ? await ReadByWaba(wabaId, token) : null;
        if (row is null || row.ProviderMode == WhatsAppProviderModes.Mock || !row.IsEnabled || !string.Equals(row.WabaId, wabaId, StringComparison.Ordinal)
            || !await features.IsEnabledAsync(row.TenantId, WhatsBiz.Application.Common.Features.FeatureKeys.WhatsAppCommerce, token)) return false;
        var appSecret = UnprotectOrNull(row.AppSecretProtected);
        if (string.IsNullOrWhiteSpace(appSecret) || !ValidSignature(signature, body.Span, appSecret)) return false;
        WhatsAppLogs.WebhookReceived(logger, row.TenantId, phoneNumberId ?? row.PhoneNumberId ?? "unknown", string.Join(',', fields.Distinct(StringComparer.Ordinal))); return true;
    }

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
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand($"SELECT TOP(1) TenantId,ProviderMode,WhatsAppBusinessAccountId,PhoneNumberId,DisplayPhoneNumber,BusinessDisplayName,AccessTokenProtected,WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,IsEnabled,ConnectionStatus,LastValidatedOn,LastError FROM integration.WhatsAppConfigurations WHERE {column}=@value;", c); q.Parameters.AddWithValue("@value", value); await using var r = await q.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? Map(r) : null; }
    private async Task<IReadOnlyCollection<ConfigRow>> ReadEnabled(CancellationToken token)
    { var rows = new List<ConfigRow>(); await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("SELECT TenantId,ProviderMode,WhatsAppBusinessAccountId,PhoneNumberId,DisplayPhoneNumber,BusinessDisplayName,AccessTokenProtected,WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,IsEnabled,ConnectionStatus,LastValidatedOn,LastError FROM integration.WhatsAppConfigurations WHERE IsEnabled=1;", c); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) rows.Add(Map(r)); return rows; }
    private static ConfigRow Map(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetBoolean(10), r.GetString(11), r.IsDBNull(12) ? null : r.GetDateTimeOffset(12), r.IsDBNull(13) ? null : r.GetString(13));
    private static WhatsAppConfigurationDto ToDto(ConfigRow x) => new(x.ProviderMode, x.WabaId, x.PhoneNumberId, x.DisplayPhoneNumber, x.BusinessDisplayName, x.ApiVersion, x.IsEnabled, x.ConnectionStatus, x.LastValidatedOn, x.LastError, x.AccessTokenProtected is not null, x.WebhookVerifyTokenProtected is not null, x.AppSecretProtected is not null);
    private static WhatsAppConfigurationDto Empty() => new(WhatsAppProviderModes.Mock, null, null, null, null, null, false, WhatsAppConnectionStatuses.NotConfigured, null, null, false, false, false);
    private string? ProtectReplacement(string? value, string? current, string label, bool optional) { if (!string.IsNullOrWhiteSpace(value)) return protector.Protect(value.Trim()); if (!string.IsNullOrWhiteSpace(current)) return current; if (optional) return null; throw new BusinessRuleException($"The {label} is required."); }
    private string? UnprotectOrNull(string? value) { if (value is null) return null; try { return protector.Unprotect(value); } catch (CryptographicException) { return null; } }
    private static void ValidateInput(SaveWhatsAppConfigurationInput x) { if (!WhatsAppProviderModes.All.Contains(x.ProviderMode)) throw new BusinessRuleException("Provider mode must be MOCK, META_TEST, or LIVE."); if (x.ProviderMode.Equals(WhatsAppProviderModes.Mock, StringComparison.OrdinalIgnoreCase)) return; if (string.IsNullOrWhiteSpace(x.WhatsAppBusinessAccountId) || !Digits().IsMatch(x.WhatsAppBusinessAccountId.Trim()) || string.IsNullOrWhiteSpace(x.PhoneNumberId) || !Digits().IsMatch(x.PhoneNumberId.Trim())) throw new BusinessRuleException("WABA ID and phone number ID must contain only digits."); if (string.IsNullOrWhiteSpace(x.ApiVersion) || !Version().IsMatch(x.ApiVersion.Trim())) throw new BusinessRuleException("API version must use Meta's vNN.N format."); }
    private static bool FixedTimeEquals(string? a, string b) { if (a is null) return false; var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b); return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y); }
    internal static bool ValidSignature(string? signature, ReadOnlySpan<byte> body, string secret) { if (signature is null || !signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false; try { var supplied = Convert.FromHexString(signature[7..]); var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body); return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected); } catch (FormatException) { return false; } }
    [GeneratedRegex("^[0-9]+$")] private static partial Regex Digits();
    [GeneratedRegex("^v[0-9]{1,3}\\.[0-9]+$")] private static partial Regex Version();
    private sealed record ConfigRow(Guid TenantId, string ProviderMode, string? WabaId, string? PhoneNumberId, string? DisplayPhoneNumber, string? BusinessDisplayName, string? AccessTokenProtected, string? WebhookVerifyTokenProtected, string? AppSecretProtected, string? ApiVersion, bool IsEnabled, string ConnectionStatus, DateTimeOffset? LastValidatedOn, string? LastError);
}

internal static partial class WhatsAppLogs
{
    [LoggerMessage(2101, LogLevel.Warning, "Meta WhatsApp connection validation failed for tenant {TenantId} with HTTP {StatusCode}.")]
    public static partial void MetaValidationFailed(ILogger logger, Guid tenantId, int statusCode);
    [LoggerMessage(2102, LogLevel.Information, "WhatsApp webhook received for tenant {TenantId}, phone number ID {PhoneNumberId}, fields {Fields}.")]
    public static partial void WebhookReceived(ILogger logger, Guid tenantId, string phoneNumberId, string fields);
}
