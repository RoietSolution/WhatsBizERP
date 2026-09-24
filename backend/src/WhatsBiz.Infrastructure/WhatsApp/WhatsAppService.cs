using System.Diagnostics;
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
using WhatsBiz.Application.Features.Referrals;

namespace WhatsBiz.Infrastructure.WhatsApp;

public sealed partial class WhatsAppService(IConfiguration configuration,
    IDataProtectionProvider dataProtectionProvider, IFeatureService features, IWhatsAppCommerceProviderResolver providers,
    ILogger<WhatsAppService> logger, IHttpClientFactory clients, ICustomerReferralService? referrals = null,
    IWhatsAppInboundCommerceHandler? inboundCommerce = null, IWhatsAppUsageBillingService? usageBilling = null) : IWhatsAppService
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1");
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<WhatsAppOnboardingConfigurationDto> GetOnboardingConfigurationAsync(CancellationToken token)
    { var platform=await ReadPlatform(token); var id=configuration["WhatsApp:Meta:EmbeddedSignupConfigurationId"]; return new(platform?.MetaAppId,id,configuration["WhatsApp:Meta:GraphApiVersion"] ?? configuration["WhatsApp:Meta:GraphApiVersion"] ?? "v23.0", platform?.IsEnabled==true && !string.IsNullOrWhiteSpace(id)); }

    public async Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token)
    { var row = await ReadByTenant(tenantId, token); if (row is null) return Empty(); var platform=await ReadPlatform(token); return ToDto(row, platform); }

    public async Task<WhatsAppPlatformConfigurationDto> GetPlatformConfigurationAsync(CancellationToken token)
    { var row=await ReadPlatform(token); return row is null ? new(null,false,false,false,null) : new(row.MetaAppId,row.IsEnabled,row.AppSecretProtected is not null,row.WebhookVerifyTokenProtected is not null,row.ModifiedOn); }

    public async Task<WhatsAppPlatformConfigurationDto> SavePlatformConfigurationAsync(SaveWhatsAppPlatformConfigurationInput input, string? actor, CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(input.MetaAppId)||!Digits().IsMatch(input.MetaAppId.Trim()))throw new BusinessRuleException("Meta App ID must contain only digits.");
        var existing=await ReadPlatform(token);var secret=ProtectReplacement(input.AppSecret,existing?.AppSecretProtected,"platform app secret",false);var verify=ProtectReplacement(input.WebhookVerifyToken,existing?.WebhookVerifyTokenProtected,"platform webhook verify token",false);
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var command=new SqlCommand("""
MERGE integration.WhatsAppPlatformConfiguration AS t USING(SELECT CONVERT(tinyint,1) PlatformConfigurationId) s ON t.PlatformConfigurationId=s.PlatformConfigurationId
WHEN MATCHED THEN UPDATE SET MetaAppId=@app,AppSecretProtected=@secret,WebhookVerifyTokenProtected=@verify,IsEnabled=@enabled,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor
WHEN NOT MATCHED THEN INSERT(PlatformConfigurationId,MetaAppId,AppSecretProtected,WebhookVerifyTokenProtected,IsEnabled,CreatedBy) VALUES(1,@app,@secret,@verify,@enabled,@actor);
""",connection);command.Parameters.AddWithValue("@app",input.MetaAppId.Trim());command.Parameters.AddWithValue("@secret",secret!);command.Parameters.AddWithValue("@verify",verify!);command.Parameters.AddWithValue("@enabled",input.IsEnabled);command.Parameters.AddWithValue("@actor",actor??(object)DBNull.Value);await command.ExecuteNonQueryAsync(token);return await GetPlatformConfigurationAsync(token);
    }

    public async Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> GetRetailerConnectionsAsync(CancellationToken token)
    {
        var platform=await ReadPlatform(token);var shared=platform?.IsEnabled==true;var rows=new List<RetailerWhatsAppConnectionDto>();await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var command=new SqlCommand("""
SELECT t.TenantId,t.TenantKey,t.Name,t.IsActive,c.ProviderMode,c.WhatsAppBusinessAccountId,c.PhoneNumberId,c.DisplayPhoneNumber,c.BusinessDisplayName,ISNULL(c.IsEnabled,0),ISNULL(c.ConnectionStatus,'NOT_CONFIGURED'),c.LastValidatedOn
FROM core.Tenants t LEFT JOIN integration.WhatsAppConfigurations c ON c.TenantId=t.TenantId ORDER BY t.Name;
""",connection);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.GetBoolean(9),reader.GetString(10),reader.IsDBNull(11)?null:reader.GetDateTimeOffset(11),shared&&!reader.IsDBNull(4)&&reader.GetString(4)!=WhatsAppProviderModes.Mock));return rows;
    }

    public async Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token)
    {
        var existing = await ReadByTenant(tenantId, token);
        var isMock = input.ProviderMode.Equals(WhatsAppProviderModes.Mock, StringComparison.OrdinalIgnoreCase);
        var isLive = input.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase);
        var platform = await ReadPlatform(token);
        var useShared = !isMock && platform?.IsEnabled == true;
        ValidateInput(input, useShared);
        if (isLive && !useShared) throw new BusinessRuleException("The shared KhataDhari Meta App configuration must be enabled before LIVE retailer connections can be saved.");
        var keepLiveConfiguration = isLive && existing?.ProviderMode == WhatsAppProviderModes.Live;
        var access = ProtectReplacement(input.AccessToken, ExistingAccessTokenForMode(input.ProviderMode, existing?.ProviderMode, existing?.AccessTokenProtected), "access token", isMock || isLive);
        var verify = useShared ? null : ProtectReplacement(input.WebhookVerifyToken, existing?.WebhookVerifyTokenProtected, "webhook verify token", isMock);
        var appSecret = useShared ? null : ProtectReplacement(input.AppSecret, existing?.AppSecretProtected, "app secret", isMock);
        var waba = PreferProvided(input.WhatsAppBusinessAccountId, keepLiveConfiguration ? existing?.WabaId : null);
        var phone = PreferProvided(input.PhoneNumberId, keepLiveConfiguration ? existing?.PhoneNumberId : null);
        var version = PreferProvided(input.ApiVersion, keepLiveConfiguration ? existing?.ApiVersion : null) ?? (isLive ? configuration["WhatsApp:Meta:GraphApiVersion"] : null);
        var remainsConnected = IsExistingLiveConnectionUnchanged(input.ProviderMode, input.IsEnabled,
            existing?.ProviderMode, existing?.IsEnabled == true, existing?.ConnectionStatus,
            waba, existing?.WabaId, phone, existing?.PhoneNumberId, access, existing?.AccessTokenProtected);
        var hasLiveConnection = isLive && !string.IsNullOrWhiteSpace(waba) && !string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(access);
        var status = ResolveSaveStatus(isLive, input.IsEnabled, hasLiveConnection, remainsConnected);
        var lastValidatedOn = remainsConnected ? existing!.LastValidatedOn : null;
        var lastError = remainsConnected ? existing!.LastError : null;
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var active = new SqlCommand("SELECT COUNT(1) FROM core.Tenants WHERE TenantId=@tenant AND IsActive=1;",connection);active.Parameters.AddWithValue("@tenant",tenantId);if(Convert.ToInt32(await active.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture)!=1)throw new BusinessRuleException("The retailer tenant is inactive or unavailable.");
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppConfigurations SET ProviderMode=@mode,MetaAppId=@appId,WhatsAppBusinessAccountId=@waba,PhoneNumberId=@phone,
              AccessTokenProtected=@access,WebhookVerifyTokenProtected=@verify,AppSecretProtected=@appSecret,
              ApiVersion=@version,TestRecipientNumber=@recipient,IsEnabled=@enabled,ConnectionStatus=@status,LastValidatedOn=@lastValidatedOn,LastError=@lastError,
              ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor WHERE TenantId=@tenant;
            IF @@ROWCOUNT=0 INSERT integration.WhatsAppConfigurations
              (WhatsAppConfigurationId,TenantId,ProviderMode,MetaAppId,WhatsAppBusinessAccountId,PhoneNumberId,AccessTokenProtected,
               WebhookVerifyTokenProtected,AppSecretProtected,ApiVersion,TestRecipientNumber,IsEnabled,ConnectionStatus,CreatedBy)
              VALUES(NEWID(),@tenant,@mode,@appId,@waba,@phone,@access,@verify,@appSecret,@version,@recipient,@enabled,@status,@actor);
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@mode", input.ProviderMode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@appId", useShared ? DBNull.Value : (object?)input.MetaAppId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@waba", (object?)waba ?? DBNull.Value);
        command.Parameters.AddWithValue("@phone", (object?)phone ?? DBNull.Value); command.Parameters.AddWithValue("@access", (object?)access ?? DBNull.Value);
        command.Parameters.AddWithValue("@verify", (object?)verify ?? DBNull.Value); command.Parameters.AddWithValue("@appSecret", (object?)appSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("@version", (object?)version ?? DBNull.Value); command.Parameters.AddWithValue("@enabled", input.IsEnabled);
        command.Parameters.AddWithValue("@recipient", string.IsNullOrWhiteSpace(input.TestRecipientNumber) ? DBNull.Value : NonDigits().Replace(input.TestRecipientNumber, string.Empty));
        command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@lastValidatedOn", (object?)lastValidatedOn ?? DBNull.Value); command.Parameters.AddWithValue("@lastError", (object?)lastError ?? DBNull.Value); command.Parameters.AddWithValue("@actor", actor ?? (object)DBNull.Value);
        try { await command.ExecuteNonQueryAsync(token); }
        catch(SqlException ex) when(ex.Number is 2601 or 2627){throw new BusinessRuleException("This WABA or Phone Number ID is already assigned to another retailer.");}
        return await GetConfigurationAsync(tenantId, token);
    }

    public async Task<WhatsAppConnectionResult> CompleteOnboardingAsync(Guid tenantId, WhatsAppOnboardingCompletionInput input, string? actor, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.AuthorizationCode)) throw new BusinessRuleException("Meta onboarding did not return an authorization code.");
        var platform=await ReadPlatform(token); if (platform is null || !platform.IsEnabled) throw new BusinessRuleException("The shared KhataDhari Meta App is not configured.");
        var secret=UnprotectOrNull(platform.AppSecretProtected); if (secret is null) throw new BusinessRuleException("The shared Meta App secret is not available.");
        var version=string.IsNullOrWhiteSpace(input.ApiVersion)?configuration["WhatsApp:Meta:GraphApiVersion"] ?? "v23.0":input.ApiVersion.Trim();
        var endpoint=$"{configuration["WhatsApp:Meta:GraphBaseUrl"]?.TrimEnd('/')}/{Uri.EscapeDataString(version)}/oauth/access_token?client_id={Uri.EscapeDataString(platform.MetaAppId)}&client_secret={Uri.EscapeDataString(secret)}&code={Uri.EscapeDataString(input.AuthorizationCode)}";
        using var response=await clients.CreateClient("MetaWhatsApp").GetAsync(endpoint,token); if(!response.IsSuccessStatusCode) throw new BusinessRuleException("Meta authorization could not be completed. Please retry onboarding.");
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); if(!json.RootElement.TryGetProperty("access_token",out var access)||string.IsNullOrWhiteSpace(access.GetString())) throw new BusinessRuleException("Meta authorization did not return a usable business credential.");
        var accessToken=access.GetString()!; var waba=input.WhatsAppBusinessAccountId; if(string.IsNullOrWhiteSpace(waba)) throw new BusinessRuleException("Meta onboarding did not identify a WhatsApp Business Account.");
        var phone=input.PhoneNumberId; if(string.IsNullOrWhiteSpace(phone)) throw new BusinessRuleException("Meta onboarding did not identify a phone number.");
        using (var subscribe = new HttpRequestMessage(HttpMethod.Post, $"{configuration["WhatsApp:Meta:GraphBaseUrl"]?.TrimEnd('/')}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(waba)}/subscribed_apps"))
        { subscribe.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken); using var subscribed = await clients.CreateClient("MetaWhatsApp").SendAsync(subscribe, token); if(!subscribed.IsSuccessStatusCode) throw new BusinessRuleException("Meta webhook subscription could not be completed. Please retry onboarding."); }
        await SaveConfigurationAsync(tenantId,new(WhatsAppProviderModes.Live,null,waba,phone,version,null,true,accessToken,null,null),actor,token);
        return await ValidateConnectionAsync(tenantId, null, token);
    }

    public async Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token) ?? throw new BusinessRuleException("WhatsApp is not configured.");
        if (!row.IsEnabled) throw new BusinessRuleException("WhatsApp configuration is disabled.");
        if (row.ProviderMode == WhatsAppProviderModes.Mock) throw new BusinessRuleException("MOCK mode does not require a Meta connection validation.");
        if (row.ProviderMode is not (WhatsAppProviderModes.MetaTest or WhatsAppProviderModes.Live)) throw new BusinessRuleException("Select a Meta provider mode before validation.");
        if (row.ProviderMode == WhatsAppProviderModes.Live && (await ReadPlatform(token))?.IsEnabled != true) throw new BusinessRuleException("The shared KhataDhari Meta App configuration is disabled.");
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
            await StoreEvent(row.TenantId, row.ProviderMode, $"outbound:{result.ProviderMessageId}", "MESSAGE_SENT", "OUTBOUND", row.PhoneNumberId, recipient, null, result.AttemptedAt, token, result.ProviderMessageId);
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

    public async Task<WhatsAppSubscriptionDiagnosticDto> GetSubscriptionDiagnosticAsync(Guid tenantId, CancellationToken token)
    {
        var row = await ReadByTenant(tenantId, token);
        var platform = await ReadPlatform(token);
        if (row is null || !row.IsEnabled || !row.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(row.WabaId) || string.IsNullOrWhiteSpace(row.PhoneNumberId) || string.IsNullOrWhiteSpace(row.ApiVersion)
            || string.IsNullOrWhiteSpace(row.AccessTokenProtected))
            return new(false, MaskIdentifier(row?.WabaId), Array.Empty<string>(), false, Array.Empty<string>(), null, "An enabled LIVE configuration with a WABA and protected access token is required.");
        if (platform?.IsEnabled != true || string.IsNullOrWhiteSpace(platform.MetaAppId))
            return new(false, MaskIdentifier(row.WabaId), Array.Empty<string>(), false, Array.Empty<string>(), null, "The shared Meta platform configuration is not enabled.");

        string accessToken;
        try { accessToken = protector.Unprotect(row.AccessTokenProtected); }
        catch (CryptographicException) { return new(false, MaskIdentifier(row.WabaId), Array.Empty<string>(), false, Array.Empty<string>(), null, "The stored LIVE credential could not be decrypted."); }

        var provider = providers.Resolve(WhatsAppProviderModes.Live);
        var subscription = await provider.GetSubscribedAppsAsync(row.ApiVersion, row.WabaId, accessToken, token);
        var phoneAssets = await provider.GetPhoneNumbersAsync(row.ApiVersion, row.WabaId, accessToken, token);
        var phoneDetails = await provider.GetPhoneNumberDetailsAsync(row.ApiVersion, row.PhoneNumberId, accessToken, token);
        var configuredDisplay = NormalizeDigits(row.DisplayPhoneNumber);
        var applicationIds = subscription.ApplicationIds.Select(MaskIdentifier).ToArray();
        var matches = subscription.ApplicationIds.Any(x => string.Equals(x, platform.MetaAppId, StringComparison.Ordinal));
        var assets = phoneAssets.Assets.Select(asset => MapPhoneAsset(asset, row.PhoneNumberId, row.DisplayPhoneNumber)).ToArray();
        var matchingIdCount = phoneAssets.Assets.Count(x => string.Equals(x.Id, row.PhoneNumberId, StringComparison.Ordinal));
        var otherPhoneAssets = phoneAssets.Assets.Any(x => !string.Equals(x.Id, row.PhoneNumberId, StringComparison.Ordinal));
        var displayMatch = phoneAssets.Assets.Any(x => !string.IsNullOrWhiteSpace(configuredDisplay)
            && string.Equals(configuredDisplay, NormalizeDigits(x.DisplayPhoneNumber), StringComparison.Ordinal));
        var errors = new[] { subscription.SafeError, phoneAssets.SafeError, phoneDetails.SafeError }.Where(x => !string.IsNullOrWhiteSpace(x));
        var configuredPhoneAsset = new WhatsAppConfiguredPhoneDiagnostic(MaskIdentifier(row.PhoneNumberId), phoneDetails.IsOnBizApp,
            phoneDetails.PlatformType, phoneDetails.SafeError);
        return new(subscription.Succeeded && phoneAssets.Succeeded && phoneDetails.Succeeded, MaskIdentifier(row.WabaId), applicationIds, matches,
            subscription.SubscribedFields, subscription.MessagesSubscribed, errors.Any() ? string.Join(" ", errors) : null,
            assets, phoneAssets.Succeeded ? matchingIdCount == 1 : null,
            phoneAssets.Succeeded ? displayMatch : null,
            phoneAssets.Succeeded ? otherPhoneAssets : null, configuredPhoneAsset);
    }

    public async Task<string?> VerifyWebhookAsync(string? mode, string? verifyToken, string? challenge, CancellationToken token)
    {
        var modeValid = string.Equals(mode, "subscribe", StringComparison.Ordinal);
        WhatsAppLogs.WebhookVerificationReceived(logger, modeValid);
        if (!modeValid || string.IsNullOrEmpty(verifyToken) || challenge is null)
        {
            WhatsAppLogs.WebhookVerificationTokenResult(logger, "NONE", false);
            return null;
        }

        var platform = await ReadPlatform(token);
        if (platform?.IsEnabled == true)
        {
            WhatsAppLogs.WebhookVerificationConfiguration(logger, "SHARED_PLATFORM", true);
            var configuredToken = UnprotectOrNull(platform.WebhookVerifyTokenProtected);
            if (configuredToken is null)
                WhatsAppLogs.WebhookVerificationCredentialUnreadable(logger, "SHARED_PLATFORM");
            var matched = FixedTimeEquals(configuredToken, verifyToken);
            WhatsAppLogs.WebhookVerificationTokenResult(logger, "SHARED_PLATFORM", matched);
            if (!matched) return null;
            WhatsAppLogs.WebhookVerificationChallengeReturned(logger, "SHARED_PLATFORM");
            return challenge;
        }

        var rows = await ReadWebhookVerificationCandidates(token);
        WhatsAppLogs.WebhookVerificationConfiguration(logger, "TENANT_CONFIGURATION", rows.Count > 0);
        var candidates = new List<(Guid TenantId, string? VerifyToken)>(rows.Count);
        foreach (var row in rows)
        {
            var configuredToken = UnprotectOrNull(row.WebhookVerifyTokenProtected);
            if (configuredToken is null)
                WhatsAppLogs.WebhookVerificationCredentialUnreadable(logger, "TENANT_CONFIGURATION");
            candidates.Add((row.TenantId, configuredToken));
        }

        var tenantId = ResolveUniqueTenantToken(candidates, verifyToken);
        var tenantMatched = tenantId is not null;
        WhatsAppLogs.WebhookVerificationTokenResult(logger, "TENANT_CONFIGURATION", tenantMatched);
        if (tenantId is not Guid matchedTenantId) return null;
        await MarkWebhookVerified(matchedTenantId, token);
        WhatsAppLogs.WebhookVerificationChallengeReturned(logger, "TENANT_CONFIGURATION");
        return challenge;
    }

    public async Task<WhatsAppWebhookReceiveResult> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var signaturePresent = !string.IsNullOrWhiteSpace(signature);
        WhatsAppLogs.WebhookPostReceived(logger, signaturePresent);

        WhatsAppWebhookReceiveResult Finish(WhatsAppWebhookReceiveResult result, string reason)
        {
            var status = result switch
            {
                WhatsAppWebhookReceiveResult.Acknowledged => 200,
                WhatsAppWebhookReceiveResult.InvalidPayload => 400,
                WhatsAppWebhookReceiveResult.PersistenceFailure => 500,
                _ => 401
            };
            WhatsAppLogs.WebhookPostOutcome(logger, result == WhatsAppWebhookReceiveResult.Acknowledged, reason);
            WhatsAppLogs.WebhookPostAcknowledged(logger, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }

        PlatformRow? platform;
        try { platform = await ReadPlatform(token); }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            WhatsAppLogs.WebhookPostFailure(logger, "PLATFORM_CONFIGURATION_READ_FAILED", exception.GetType().Name);
            return Finish(WhatsAppWebhookReceiveResult.PersistenceFailure, "PLATFORM_CONFIGURATION_READ_FAILED");
        }

        var sharedSignatureValid = false;
        if (platform?.IsEnabled == true)
        {
            WhatsAppLogs.WebhookPostConfiguration(logger, "SHARED_PLATFORM", true);
            var sharedSecret = UnprotectOrNull(platform.AppSecretProtected);
            var decrypted = !string.IsNullOrWhiteSpace(sharedSecret);
            sharedSignatureValid = decrypted && ValidSignature(signature, body.Span, sharedSecret!);
            WhatsAppLogs.WebhookPostSignature(logger, "SHARED_PLATFORM", decrypted, sharedSignatureValid);
            if (!sharedSignatureValid)
                return Finish(WhatsAppWebhookReceiveResult.InvalidSignature,
                    signaturePresent ? "INVALID_SIGNATURE" : "MISSING_SIGNATURE");
        }

        IReadOnlyCollection<WebhookEnvelope> envelopes;
        try { envelopes = ParseWebhook(body); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            WhatsAppLogs.WebhookPostPayloadRejected(logger, exception.GetType().Name);
            return Finish(sharedSignatureValid ? WhatsAppWebhookReceiveResult.InvalidPayload : WhatsAppWebhookReceiveResult.InvalidSignature,
                "INVALID_PAYLOAD");
        }

        if (envelopes.Count == 0)
            return sharedSignatureValid
                ? Finish(WhatsAppWebhookReceiveResult.Acknowledged, "IGNORED_UNSUPPORTED_ENVELOPE")
                : Finish(WhatsAppWebhookReceiveResult.InvalidSignature, "CONFIGURATION_NOT_RESOLVED");

        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        processingCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("WhatsApp:Webhook:DownstreamTimeoutSeconds") ?? 8, 1, 15)));
        var processingToken = processingCts.Token;

        foreach (var envelope in envelopes)
        {
            WhatsAppLogs.WebhookPostEnvelope(logger, envelope.ChangeType, MaskIdentifier(envelope.PhoneNumberId), envelope.Events.Count);
            if (envelope.Events.Count == 0 && string.IsNullOrWhiteSpace(envelope.PhoneNumberId))
            {
                if (!sharedSignatureValid)
                    return Finish(WhatsAppWebhookReceiveResult.InvalidSignature, "CONFIGURATION_NOT_RESOLVED");
                WhatsAppLogs.WebhookPostIgnored(logger, envelope.ChangeType, "NON_ACTIONABLE_CHANGE");
                continue;
            }

            if (string.IsNullOrWhiteSpace(envelope.PhoneNumberId))
                return Finish(WhatsAppWebhookReceiveResult.InvalidPayload, "ACTIONABLE_PHONE_ID_MISSING");

            ConfigRow? row;
            try { row = await ReadByPhone(envelope.PhoneNumberId, token); }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                WhatsAppLogs.WebhookPostFailure(logger, "TENANT_CONFIGURATION_READ_FAILED", exception.GetType().Name);
                return Finish(WhatsAppWebhookReceiveResult.PersistenceFailure, "TENANT_CONFIGURATION_READ_FAILED");
            }
            var configurationResolved = row is not null && row.ProviderMode != WhatsAppProviderModes.Mock && row.IsEnabled
                && string.Equals(row.WabaId, envelope.WabaId, StringComparison.Ordinal);
            WhatsAppLogs.WebhookPostConfiguration(logger, "TENANT_WABA_PHONE", configurationResolved);
            if (!configurationResolved)
            {
                WhatsAppLogs.WebhookPostConfigurationNotResolved(logger, MaskIdentifier(envelope.PhoneNumberId), MaskIdentifier(envelope.WabaId));
                if (!sharedSignatureValid)
                    return Finish(WhatsAppWebhookReceiveResult.InvalidSignature, "CONFIGURATION_NOT_RESOLVED");
                continue;
            }

            if (!sharedSignatureValid)
            {
                if (row!.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase))
                {
                    WhatsAppLogs.WebhookPostConfiguration(logger, "SHARED_PLATFORM_REQUIRED_FOR_LIVE", false);
                    return Finish(WhatsAppWebhookReceiveResult.InvalidSignature, "APP_AUTHENTICATION_CONFIGURATION_NOT_RESOLVED");
                }
                var tenantSecret = UnprotectOrNull(row!.AppSecretProtected);
                var decrypted = !string.IsNullOrWhiteSpace(tenantSecret);
                var signatureValid = decrypted && ValidSignature(signature, body.Span, tenantSecret!);
                WhatsAppLogs.WebhookPostSignature(logger, "TENANT_CONFIGURATION", decrypted, signatureValid);
                if (!signatureValid)
                    return Finish(WhatsAppWebhookReceiveResult.InvalidSignature,
                        signaturePresent ? "INVALID_SIGNATURE" : "MISSING_SIGNATURE");
            }

            foreach (var item in envelope.Events)
            {
                bool inserted;
                try
                {
                    inserted = await StoreEvent(row!.TenantId, row.ProviderMode, item.EventKey, item.EventType, item.Direction,
                        envelope.PhoneNumberId, item.ContactNumber, item.Status, item.EventTimestamp, token, item.MetaMessageId,
                        item.MessageType, item.PricingCategory, item.MetaBillable, item.PricingModel);
                }
                catch (Exception exception) when (exception is SqlException or InvalidOperationException)
                {
                    WhatsAppLogs.WebhookPostFailure(logger, "DURABLE_ACCEPTANCE_FAILED", exception.GetType().Name);
                    return Finish(WhatsAppWebhookReceiveResult.PersistenceFailure, "DURABLE_ACCEPTANCE_FAILED");
                }
                WhatsAppLogs.TransportProcessed(logger, row!.ProviderMode, row.TenantId, item.EventType, item.MetaMessageId,
                    item.Direction, inserted ? "RECORDED" : "DUPLICATE");
                try { await MarkWebhookReceived(row.TenantId, item, !inserted, token); }
                catch (Exception exception) when (exception is SqlException or InvalidOperationException)
                { WhatsAppLogs.WebhookPostFailure(logger, "DIAGNOSTICS_UPDATE_FAILED", exception.GetType().Name); }
                if (!inserted) continue;

                try
                {
                    if (!await features.IsEnabledAsync(row.TenantId,
                        WhatsBiz.Application.Common.Features.FeatureKeys.WhatsAppCommerce, processingToken))
                    {
                        WhatsAppLogs.WebhookPostProcessingSkipped(logger, row.TenantId, "FEATURE_DISABLED");
                        await SetEventProcessingStatus(row.TenantId, item.EventKey, "IGNORED", processingToken);
                        continue;
                    }
                    if (item.EventType == "MESSAGE_STATUS" && usageBilling is not null)
                        await usageBilling.ApplyStatusAsync(new(row.TenantId, row.ProviderMode, envelope.PhoneNumberId,
                            item.MetaMessageId, item.ContactNumber, item.Status ?? "UNKNOWN", item.EventTimestamp,
                            item.PricingCategory, item.MetaBillable, item.PricingModel), processingToken);
                    if (item.Direction == "INBOUND" && item.ContactNumber is not null)
                    {
                        await UpsertContact(row.TenantId, item, processingToken);
                        if (item.MessageText is not null)
                            await TryCaptureReferralMessage(row.TenantId, item.ContactNumber, item.MessageText, processingToken);
                        if (inboundCommerce is not null && row.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)
                            && (item.MessageText is not null || item.InteractiveActionId is not null || item.Products.Count > 0))
                            await inboundCommerce.HandleAsync(row.TenantId, row.ProviderMode, envelope.PhoneNumberId, item.ContactNumber,
                                item.MetaMessageId, new(item.MessageText, item.InteractiveActionId, item.CatalogId, item.Products), processingToken);
                    }
                    await SetEventProcessingStatus(row.TenantId, item.EventKey, "PROCESSED", processingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    WhatsAppLogs.WebhookPostFailure(logger, "DOWNSTREAM_PROCESSING_FAILED", exception.GetType().Name);
                    try { await SetEventProcessingStatus(row.TenantId, item.EventKey, "FAILED", CancellationToken.None); }
                    catch (Exception statusException) { WhatsAppLogs.WebhookPostFailure(logger, "PROCESSING_STATUS_UPDATE_FAILED", statusException.GetType().Name); }
                }
            }
            WhatsAppLogs.WebhookReceived(logger, row!.TenantId, MaskIdentifier(envelope.PhoneNumberId),
                string.Join(',', envelope.Events.Select(x => x.EventType).Distinct(StringComparer.Ordinal)));
        }
        return Finish(WhatsAppWebhookReceiveResult.Acknowledged, "ACCEPTED");
    }

    public async Task<PagedWhatsAppContacts> GetContactsAsync(Guid tenantId,string? search,string? status,int pageNumber,int pageSize,CancellationToken token)
    {
        pageNumber=Math.Max(1,pageNumber);pageSize=Math.Clamp(pageSize,1,100);
        var normalizedStatus=string.IsNullOrWhiteSpace(status)?null:status.Trim().ToUpperInvariant();
        if(normalizedStatus is not null&&!WhatsAppContactStatuses.All.Contains(normalizedStatus))throw new BusinessRuleException("WhatsApp contact status is invalid.");
        var rows=new List<WhatsAppContactDto>();var total=0;var newCount=0;var matchedCount=0;var convertedCount=0;
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
SELECT COUNT(1),SUM(CASE WHEN wc.Status=N'NEW' THEN 1 ELSE 0 END),SUM(CASE WHEN wc.Status=N'MATCHED' THEN 1 ELSE 0 END),SUM(CASE WHEN wc.Status=N'CONVERTED' THEN 1 ELSE 0 END)
FROM integration.WhatsAppContacts wc LEFT JOIN sales.Customers c ON c.CustomerId=wc.CustomerId AND c.TenantId=wc.TenantId AND c.IsDeleted=0
WHERE wc.TenantId=@tenant AND (@status IS NULL OR wc.Status=@status) AND (@search IS NULL OR wc.DisplayMobile LIKE N'%'+@search+N'%' OR wc.ProfileName LIKE N'%'+@search+N'%' OR c.CustomerName LIKE N'%'+@search+N'%');
SELECT wc.WhatsAppContactId,wc.DisplayMobile,wc.ProfileName,wc.Status,wc.CustomerId,c.CustomerCode,c.CustomerName,wc.FirstMessageAt,wc.LastMessageAt,wc.MessageCount,wc.LastMessageType
FROM integration.WhatsAppContacts wc LEFT JOIN sales.Customers c ON c.CustomerId=wc.CustomerId AND c.TenantId=wc.TenantId AND c.IsDeleted=0
WHERE wc.TenantId=@tenant AND (@status IS NULL OR wc.Status=@status) AND (@search IS NULL OR wc.DisplayMobile LIKE N'%'+@search+N'%' OR wc.ProfileName LIKE N'%'+@search+N'%' OR c.CustomerName LIKE N'%'+@search+N'%')
ORDER BY wc.LastMessageAt DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
""",connection);
        command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@status",(object?)normalizedStatus??DBNull.Value);command.Parameters.AddWithValue("@search",string.IsNullOrWhiteSpace(search)?DBNull.Value:search.Trim());command.Parameters.AddWithValue("@offset",(pageNumber-1)*pageSize);command.Parameters.AddWithValue("@size",pageSize);
        await using var reader=await command.ExecuteReaderAsync(token);if(await reader.ReadAsync(token)){total=reader.GetInt32(0);newCount=reader.IsDBNull(1)?0:reader.GetInt32(1);matchedCount=reader.IsDBNull(2)?0:reader.GetInt32(2);convertedCount=reader.IsDBNull(3)?0:reader.GetInt32(3);}await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token))rows.Add(MapContact(reader));
        return new(rows,total,newCount,matchedCount,convertedCount,pageNumber,pageSize);
    }

    public async Task<WhatsAppContactDto> LinkContactAsync(Guid tenantId,Guid contactId,Guid customerId,string? actor,CancellationToken token)
    {
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        await using var command=new SqlCommand("""
IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsDeleted=0) THROW 51000,N'Customer not found.',1;
IF NOT EXISTS(SELECT 1 FROM integration.WhatsAppContacts WHERE TenantId=@tenant AND WhatsAppContactId=@contact) THROW 51000,N'WhatsApp contact not found.',1;
DECLARE @previous uniqueidentifier=(SELECT CustomerId FROM integration.WhatsAppContacts WHERE TenantId=@tenant AND WhatsAppContactId=@contact);
UPDATE integration.WhatsAppContacts SET CustomerId=@customer,Status=N'CONVERTED',UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND WhatsAppContactId=@contact;
INSERT integration.WhatsAppContactEvents(TenantId,WhatsAppContactId,EventType,PreviousCustomerId,CustomerId,Actor,CreatedAt) VALUES(@tenant,@contact,N'LINKED',@previous,@customer,@actor,SYSUTCDATETIME());
""",connection,(SqlTransaction)transaction);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@contact",contactId);command.Parameters.AddWithValue("@customer",customerId);command.Parameters.AddWithValue("@actor",(object?)actor??DBNull.Value);
        try{await command.ExecuteNonQueryAsync(token);}catch(SqlException ex)when(ex.Number==51000){throw new BusinessRuleException(ex.Message);}
        await transaction.CommitAsync(token);return await GetContact(tenantId,contactId,token);
    }

    private async Task UpsertContact(Guid tenantId,WebhookTransportEvent item,CancellationToken token)
    {
        var normalized=NonDigits().Replace(item.ContactNumber??string.Empty,string.Empty);if(normalized.Length is <8 or >15)return;
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        await using var command=new SqlCommand("""
DECLARE @customer uniqueidentifier=(SELECT TOP(1) CustomerId FROM sales.Customers WHERE TenantId=@tenant AND IsDeleted=0 AND Mobile IS NOT NULL AND RIGHT(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),10)=RIGHT(@mobile,10) ORDER BY IsActive DESC,CreatedOn);
DECLARE @contact uniqueidentifier=(SELECT WhatsAppContactId FROM integration.WhatsAppContacts WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND NormalizedMobile=@mobile);
IF @contact IS NULL BEGIN
 SET @contact=NEWID();
 INSERT integration.WhatsAppContacts(WhatsAppContactId,TenantId,NormalizedMobile,DisplayMobile,ProfileName,Status,CustomerId,FirstMessageAt,LastMessageAt,MessageCount,LastMessageType,LastMetaMessageId,CreatedAt,UpdatedAt)
 VALUES(@contact,@tenant,@mobile,N'+'+@mobile,@name,CASE WHEN @customer IS NULL THEN N'NEW' ELSE N'MATCHED' END,@customer,@at,@at,1,@type,@message,SYSUTCDATETIME(),SYSUTCDATETIME());
 INSERT integration.WhatsAppContactEvents(TenantId,WhatsAppContactId,EventType,CustomerId,CreatedAt) VALUES(@tenant,@contact,CASE WHEN @customer IS NULL THEN N'CREATED' ELSE N'AUTO_MATCHED' END,@customer,SYSUTCDATETIME());
END ELSE BEGIN
 DECLARE @oldCustomer uniqueidentifier=(SELECT CustomerId FROM integration.WhatsAppContacts WHERE WhatsAppContactId=@contact);
 UPDATE integration.WhatsAppContacts SET ProfileName=COALESCE(NULLIF(@name,N''),ProfileName),CustomerId=COALESCE(CustomerId,@customer),Status=CASE WHEN Status=N'CONVERTED' THEN Status WHEN COALESCE(CustomerId,@customer) IS NULL THEN N'NEW' ELSE N'MATCHED' END,LastMessageAt=CASE WHEN @at>LastMessageAt THEN @at ELSE LastMessageAt END,MessageCount=MessageCount+1,LastMessageType=@type,LastMetaMessageId=@message,UpdatedAt=SYSUTCDATETIME() WHERE WhatsAppContactId=@contact AND TenantId=@tenant;
 IF @oldCustomer IS NULL AND @customer IS NOT NULL INSERT integration.WhatsAppContactEvents(TenantId,WhatsAppContactId,EventType,CustomerId,CreatedAt) VALUES(@tenant,@contact,N'AUTO_MATCHED',@customer,SYSUTCDATETIME());
END
""",connection,transaction);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@mobile",normalized);command.Parameters.AddWithValue("@name",(object?)item.ProfileName?.Trim()??DBNull.Value);command.Parameters.AddWithValue("@at",item.EventTimestamp);command.Parameters.AddWithValue("@type",(object?)item.MessageType??DBNull.Value);command.Parameters.AddWithValue("@message",item.MetaMessageId);await command.ExecuteNonQueryAsync(token);await transaction.CommitAsync(token);
    }

    private async Task<WhatsAppContactDto> GetContact(Guid tenantId,Guid contactId,CancellationToken token)
    {await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var command=new SqlCommand("SELECT wc.WhatsAppContactId,wc.DisplayMobile,wc.ProfileName,wc.Status,wc.CustomerId,c.CustomerCode,c.CustomerName,wc.FirstMessageAt,wc.LastMessageAt,wc.MessageCount,wc.LastMessageType FROM integration.WhatsAppContacts wc LEFT JOIN sales.Customers c ON c.CustomerId=wc.CustomerId AND c.TenantId=wc.TenantId AND c.IsDeleted=0 WHERE wc.TenantId=@tenant AND wc.WhatsAppContactId=@contact",connection);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@contact",contactId);await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?MapContact(reader):throw new BusinessRuleException("WhatsApp contact not found.");}
    private static WhatsAppContactDto MapContact(SqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetGuid(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.GetDateTimeOffset(7),r.GetDateTimeOffset(8),r.GetInt32(9),r.IsDBNull(10)?null:r.GetString(10));

    private async Task<bool> StoreEvent(Guid tenantId, string providerMode, string eventKey, string eventType, string direction,
        string? phoneNumberId, string? contactNumber, string? status, DateTimeOffset eventTimestamp,
        CancellationToken token, string? metaMessageId = null, string? messageType = null,
        string? pricingCategory = null, bool? metaBillable = null, string? pricingModel = null)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                INSERT integration.WhatsAppWebhookEvents(WhatsAppWebhookEventId,TenantId,ProviderMode,EventKey,
                  MetaMessageId,EventType,Direction,PhoneNumberId,ContactNumber,MessageType,MessageStatus,PricingCategory,MetaBillable,MetaPricingModel,
                  EventTimestamp,ProcessingStatus,ReceivedOn)
                VALUES(NEWID(),@tenant,@provider,@key,@message,@type,@direction,@phone,@contact,@messageType,@status,@pricingCategory,@billable,@pricingModel,
                  @timestamp,'ACCEPTED',SYSUTCDATETIME());
                """, connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@key", eventKey);
            command.Parameters.AddWithValue("@provider",providerMode);
            command.Parameters.AddWithValue("@message", (object?)metaMessageId ?? (eventKey.StartsWith("outbound:", StringComparison.Ordinal) ? eventKey[9..] : DBNull.Value));
            command.Parameters.AddWithValue("@type", eventType); command.Parameters.AddWithValue("@direction", direction);
            command.Parameters.AddWithValue("@phone", (object?)phoneNumberId ?? DBNull.Value); command.Parameters.AddWithValue("@contact", (object?)contactNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@messageType", (object?)messageType ?? DBNull.Value); command.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@pricingCategory",(object?)pricingCategory??DBNull.Value);command.Parameters.AddWithValue("@billable",(object?)metaBillable??DBNull.Value);command.Parameters.AddWithValue("@pricingModel",(object?)pricingModel??DBNull.Value);
            command.Parameters.AddWithValue("@timestamp", eventTimestamp); await command.ExecuteNonQueryAsync(token); return true;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await using var connection = new SqlConnection(ConnectionString);await connection.OpenAsync(token);
            await using var enrich = new SqlCommand("""
                UPDATE integration.WhatsAppWebhookEvents SET
                  PricingCategory=COALESCE(PricingCategory,@pricingCategory),
                  MetaBillable=COALESCE(MetaBillable,@billable),
                  MetaPricingModel=COALESCE(MetaPricingModel,@pricingModel)
                WHERE TenantId=@tenant AND EventKey=@key;
                """,connection);
            enrich.Parameters.AddWithValue("@tenant",tenantId);enrich.Parameters.AddWithValue("@key",eventKey);
            enrich.Parameters.AddWithValue("@pricingCategory",(object?)pricingCategory??DBNull.Value);
            enrich.Parameters.AddWithValue("@billable",(object?)metaBillable??DBNull.Value);
            enrich.Parameters.AddWithValue("@pricingModel",(object?)pricingModel??DBNull.Value);
            await enrich.ExecuteNonQueryAsync(token);return false;
        }
    }

    private async Task SetEventProcessingStatus(Guid tenantId, string eventKey, string status, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(
            "UPDATE integration.WhatsAppWebhookEvents SET ProcessingStatus=@status WHERE TenantId=@tenant AND EventKey=@key;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@key", eventKey);
        command.Parameters.AddWithValue("@status", status);
        await command.ExecuteNonQueryAsync(token);
    }

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? InteractiveAction(JsonElement message)
    {
        string? action = null;
        if (message.TryGetProperty("interactive", out var interactive) && interactive.ValueKind == JsonValueKind.Object)
        {
            if (interactive.TryGetProperty("button_reply", out var button) && button.ValueKind == JsonValueKind.Object) action = String(button, "id");
            else if (interactive.TryGetProperty("list_reply", out var list) && list.ValueKind == JsonValueKind.Object) action = String(list, "id");
        }
        else if (message.TryGetProperty("button", out var legacyButton) && legacyButton.ValueKind == JsonValueKind.Object)
            action = String(legacyButton, "payload");
        return action is not null && WhatsAppCommerceActionIds.Supported.Contains(action) ? action : null;
    }
    private static (string? CatalogId, IReadOnlyCollection<WhatsAppCommerceInboundProduct> Products) CommerceProducts(JsonElement message)
    {
        if (!message.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object)
            return (null, Array.Empty<WhatsAppCommerceInboundProduct>());
        var catalog = String(order, "catalog_id");
        if (string.IsNullOrWhiteSpace(catalog) || !order.TryGetProperty("product_items", out var items) || items.ValueKind != JsonValueKind.Array)
            return (null, Array.Empty<WhatsAppCommerceInboundProduct>());
        var products = new List<WhatsAppCommerceInboundProduct>();
        foreach (var item in items.EnumerateArray())
        {
            var external = String(item, "product_retailer_id");
            if (string.IsNullOrWhiteSpace(external) || !item.TryGetProperty("quantity", out var quantity)
                || quantity.ValueKind != JsonValueKind.Number || !quantity.TryGetDecimal(out var value) || value <= 0)
                return (null, Array.Empty<WhatsAppCommerceInboundProduct>());
            products.Add(new(external, value));
        }
        return products.Count == 0 ? (null, Array.Empty<WhatsAppCommerceInboundProduct>()) : (catalog, products);
    }
    private static DateTimeOffset Timestamp(JsonElement element) => element.TryGetProperty("timestamp", out var value)
        && value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var seconds)
        && seconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;

    internal static IReadOnlyCollection<WebhookEnvelope> ParseWebhook(ReadOnlyMemory<byte> body)
    {
        using var document=JsonDocument.Parse(body);var root=document.RootElement;var result=new List<WebhookEnvelope>();
        if(root.ValueKind!=JsonValueKind.Object)throw new JsonException("Webhook root must be an object.");
        if(!root.TryGetProperty("object",out var objectNode)||objectNode.GetString()!="whatsapp_business_account")return result;
        if(!root.TryGetProperty("entry",out var entries)||entries.ValueKind!=JsonValueKind.Array)return result;
        foreach(var entry in entries.EnumerateArray())
        {
            var wabaId=String(entry,"id");if(string.IsNullOrWhiteSpace(wabaId))throw new JsonException("Webhook entry has no WABA ID.");
            if(!entry.TryGetProperty("changes",out var changes)||changes.ValueKind!=JsonValueKind.Array)continue;
            foreach(var change in changes.EnumerateArray())
            {
                // A change without a value is malformed; leave it out of the
                // parsed envelope so the request is rejected unless another
                // well-formed change in the payload can be processed.
                if(change.ValueKind!=JsonValueKind.Object||!change.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.Object)continue;
                var changeType=String(change,"field")??"UNKNOWN";
                var phoneNumberId=value.TryGetProperty("metadata",out var metadata)?String(metadata,"phone_number_id"):null;
                var hasMessages=value.TryGetProperty("messages",out var messagesNode)
                    && messagesNode.ValueKind==JsonValueKind.Array && messagesNode.GetArrayLength()>0;
                var hasStatuses=value.TryGetProperty("statuses",out var statusesNode)
                    && statusesNode.ValueKind==JsonValueKind.Array && statusesNode.GetArrayLength()>0;
                if((hasMessages||hasStatuses)&&string.IsNullOrWhiteSpace(phoneNumberId))
                    throw new JsonException("Webhook message/status change has no Phone Number ID.");
                var events=new List<WebhookTransportEvent>();var profiles=new Dictionary<string,string>(StringComparer.Ordinal);
                if(value.TryGetProperty("contacts",out var contacts)&&contacts.ValueKind==JsonValueKind.Array)foreach(var contact in contacts.EnumerateArray())
                {var waId=String(contact,"wa_id");var name=contact.TryGetProperty("profile",out var profile)?String(profile,"name"):null;if(!string.IsNullOrWhiteSpace(waId)&&!string.IsNullOrWhiteSpace(name))profiles[waId]=name;}
                if(value.TryGetProperty("messages",out var messages)&&messages.ValueKind==JsonValueKind.Array)foreach(var message in messages.EnumerateArray())
                {
                    if(message.ValueKind!=JsonValueKind.Object)throw new JsonException("Webhook message must be an object.");
                    var id=String(message,"id");var from=String(message,"from");var messageType=String(message,"type");
                    if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(from)||string.IsNullOrWhiteSpace(messageType))
                        throw new JsonException("Webhook message is missing a required routing or identity field.");
                    var text=message.TryGetProperty("text",out var textNode)&&textNode.ValueKind==JsonValueKind.Object?String(textNode,"body"):null;
                    var action=InteractiveAction(message);var (catalogId,productItems)=CommerceProducts(message);
                    events.Add(new($"message:{id}",id,"MESSAGE_RECEIVED","INBOUND",from,messageType,null,Timestamp(message),text,profiles.TryGetValue(from,out var profileName)?profileName:null,null,null,null,action,catalogId,productItems));
                }
                if(value.TryGetProperty("statuses",out var statuses)&&statuses.ValueKind==JsonValueKind.Array)foreach(var status in statuses.EnumerateArray())
                {if(status.ValueKind!=JsonValueKind.Object)throw new JsonException("Webhook status must be an object.");var id=String(status,"id");var state=String(status,"status");if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(state))throw new JsonException("Webhook status is missing a required identity field.");string? category=null;string? pricingModel=null;bool? billable=null;if(status.TryGetProperty("pricing",out var pricing)&&pricing.ValueKind==JsonValueKind.Object){category=String(pricing,"category");pricingModel=String(pricing,"pricing_model");if(pricing.TryGetProperty("billable",out var billableNode)&&(billableNode.ValueKind==JsonValueKind.True||billableNode.ValueKind==JsonValueKind.False))billable=billableNode.GetBoolean();}events.Add(new($"status:{id}:{state}",id,"MESSAGE_STATUS","OUTBOUND",String(status,"recipient_id"),null,state,Timestamp(status),null,null,category,billable,pricingModel));}
                result.Add(new(wabaId,phoneNumberId,changeType,events));
            }
        }
        return result;
    }

    private async Task MarkWebhookVerified(Guid tenantId, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET LastWebhookVerifiedOn=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", c); q.Parameters.AddWithValue("@tenant", tenantId); await q.ExecuteNonQueryAsync(token); }
    private async Task MarkWebhookReceived(Guid tenantId, WebhookTransportEvent item, bool duplicate, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("UPDATE integration.WhatsAppConfigurations SET LastWebhookReceivedOn=SYSUTCDATETIME(),LastWebhookEventType=@type,LastWebhookMetaMessageId=@message,DuplicateWebhookCount=DuplicateWebhookCount+@duplicate,ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant;", c); q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@type", item.EventType); q.Parameters.AddWithValue("@message", item.MetaMessageId); q.Parameters.AddWithValue("@duplicate", duplicate ? 1 : 0); await q.ExecuteNonQueryAsync(token); }

    private async Task TryCaptureReferralMessage(Guid tenantId,string mobile,string text,CancellationToken token)
    {
        var match=ReferralCommand().Match(text);if(referrals is null||!match.Success||!await features.IsEnabledAsync(tenantId,WhatsBiz.Application.Common.Features.FeatureKeys.CustomerReferralRewards,token))return;
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1) CustomerId FROM sales.Customers WHERE TenantId=@tenant AND IsDeleted=0 AND RIGHT(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),10)=RIGHT(@mobile,10)",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@mobile",NonDigits().Replace(mobile,string.Empty));var value=await q.ExecuteScalarAsync(token);if(value is not Guid customer)return;
        try{await referrals.CaptureAsync(tenantId,new(match.Groups[1].Value,customer,"WHATSAPP"),"WHATSAPP",token);}catch(BusinessRuleException){/* The signed webhook is acknowledged; invalid/duplicate attribution is not retried. */}
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
    private async Task<ConfigRow?> ReadOne(string column, object value, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand($"SELECT TOP(1) c.TenantId,c.ProviderMode,c.MetaAppId,c.WhatsAppBusinessAccountId,c.PhoneNumberId,c.DisplayPhoneNumber,c.BusinessDisplayName,c.AccessTokenProtected,c.WebhookVerifyTokenProtected,c.AppSecretProtected,c.ApiVersion,c.TestRecipientNumber,c.IsEnabled,c.ConnectionStatus,c.LastValidatedOn,c.LastError,c.LastWebhookVerifiedOn,c.LastWebhookReceivedOn,c.LastWebhookEventType,c.LastWebhookMetaMessageId,c.DuplicateWebhookCount FROM integration.WhatsAppConfigurations c JOIN core.Tenants t ON t.TenantId=c.TenantId AND t.IsActive=1 WHERE c.{column}=@value;", c); q.Parameters.AddWithValue("@value", value); await using var r = await q.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? Map(r) : null; }
    private async Task<IReadOnlyCollection<ConfigRow>> ReadWebhookVerificationCandidates(CancellationToken token)
    { var rows = new List<ConfigRow>(); await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("SELECT c.TenantId,c.ProviderMode,c.MetaAppId,c.WhatsAppBusinessAccountId,c.PhoneNumberId,c.DisplayPhoneNumber,c.BusinessDisplayName,c.AccessTokenProtected,c.WebhookVerifyTokenProtected,c.AppSecretProtected,c.ApiVersion,c.TestRecipientNumber,c.IsEnabled,c.ConnectionStatus,c.LastValidatedOn,c.LastError,c.LastWebhookVerifiedOn,c.LastWebhookReceivedOn,c.LastWebhookEventType,c.LastWebhookMetaMessageId,c.DuplicateWebhookCount FROM integration.WhatsAppConfigurations c JOIN core.Tenants t ON t.TenantId=c.TenantId AND t.IsActive=1 WHERE c.IsEnabled=1 AND c.ProviderMode<>N'MOCK' AND c.WebhookVerifyTokenProtected IS NOT NULL;", c); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) rows.Add(Map(r)); return rows; }
    private async Task<PlatformRow?> ReadPlatform(CancellationToken token)
    {await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT MetaAppId,AppSecretProtected,WebhookVerifyTokenProtected,IsEnabled,ModifiedOn FROM integration.WhatsAppPlatformConfiguration WHERE PlatformConfigurationId=1;",c);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.IsDBNull(4)?null:r.GetDateTimeOffset(4)):null;}
    private static ConfigRow Map(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetBoolean(12), r.GetString(13), r.IsDBNull(14) ? null : r.GetDateTimeOffset(14), r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? null : r.GetDateTimeOffset(16), r.IsDBNull(17) ? null : r.GetDateTimeOffset(17), r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? null : r.GetString(19), r.GetInt64(20));
    private static WhatsAppConfigurationDto ToDto(ConfigRow x,PlatformRow? platform) {var shared=x.ProviderMode!=WhatsAppProviderModes.Mock&&platform?.IsEnabled==true;return new(x.ProviderMode,shared?platform!.MetaAppId:x.MetaAppId,x.WabaId,x.PhoneNumberId,x.DisplayPhoneNumber,x.BusinessDisplayName,x.ApiVersion,x.TestRecipientNumber,x.IsEnabled,x.ConnectionStatus,x.LastValidatedOn,x.LastError,x.AccessTokenProtected is not null,shared?platform!.WebhookVerifyTokenProtected is not null:x.WebhookVerifyTokenProtected is not null,shared?platform!.AppSecretProtected is not null:x.AppSecretProtected is not null,shared);}
    private static WhatsAppConfigurationDto Empty() => new(WhatsAppProviderModes.Mock, null, null, null, null, null, null, null, false, WhatsAppConnectionStatuses.NotConfigured, null, null, false, false, false);
    private string? ProtectReplacement(string? value, string? current, string label, bool optional) { if (!string.IsNullOrWhiteSpace(value)) return protector.Protect(value.Trim()); if (!string.IsNullOrWhiteSpace(current)) return current; if (optional) return null; throw new BusinessRuleException($"The {label} is required."); }
    private string? UnprotectOrNull(string? value) { if (value is null) return null; try { return protector.Unprotect(value); } catch (CryptographicException) { return null; } }
    private static string MaskIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= 4 ? new string('*', trimmed.Length) : $"***{trimmed[^4..]}";
    }
    private static string NormalizeDigits(string? value) => value is null ? string.Empty : NonDigits().Replace(value, string.Empty);
    internal static WhatsAppPhoneAssetDiagnostic MapPhoneAsset(WhatsAppProviderPhoneAsset asset, string? configuredPhoneNumberId, string? configuredDisplayNumber)
    {
        var configuredDisplay = NormalizeDigits(configuredDisplayNumber);
        var matchesDisplay = string.IsNullOrWhiteSpace(configuredDisplay) || string.IsNullOrWhiteSpace(asset.DisplayPhoneNumber)
            ? (bool?)null
            : string.Equals(configuredDisplay, NormalizeDigits(asset.DisplayPhoneNumber), StringComparison.Ordinal);
        return new(MaskIdentifier(asset.Id), MaskIdentifier(asset.DisplayPhoneNumber), asset.VerifiedName,
            asset.QualityRating, asset.CodeVerificationStatus, asset.PlatformType, asset.NameStatus,
            string.Equals(asset.Id, configuredPhoneNumberId, StringComparison.Ordinal), matchesDisplay);
    }
    internal static void ValidateInput(SaveWhatsAppConfigurationInput x, bool usesSharedPlatformCredentials)
    {
        if (!WhatsAppProviderModes.All.Contains(x.ProviderMode)) throw new BusinessRuleException("Provider mode must be MOCK, META_TEST, or LIVE.");
        if (x.ProviderMode.Equals(WhatsAppProviderModes.Mock, StringComparison.OrdinalIgnoreCase)) return;

        var isLive = x.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase);
        if (isLive)
        {
            if (!usesSharedPlatformCredentials) throw new BusinessRuleException("The shared KhataDhari Meta App configuration must be enabled before LIVE retailer connections can be saved.");
            if (!IsOptionalDigits(x.WhatsAppBusinessAccountId) || !IsOptionalDigits(x.PhoneNumberId)) throw new BusinessRuleException("WABA ID and phone number ID must contain only digits.");
            if (!string.IsNullOrWhiteSpace(x.ApiVersion) && !Version().IsMatch(x.ApiVersion.Trim())) throw new BusinessRuleException("API version must use Meta's vNN.N format.");
        }
        else
        {
            if ((!usesSharedPlatformCredentials && (string.IsNullOrWhiteSpace(x.MetaAppId) || !Digits().IsMatch(x.MetaAppId.Trim()))) || string.IsNullOrWhiteSpace(x.WhatsAppBusinessAccountId) || !Digits().IsMatch(x.WhatsAppBusinessAccountId.Trim()) || string.IsNullOrWhiteSpace(x.PhoneNumberId) || !Digits().IsMatch(x.PhoneNumberId.Trim())) throw new BusinessRuleException(usesSharedPlatformCredentials ? "WABA ID and phone number ID must contain only digits." : "Meta App ID, WABA ID, and phone number ID must contain only digits.");
            if (string.IsNullOrWhiteSpace(x.ApiVersion) || !Version().IsMatch(x.ApiVersion.Trim())) throw new BusinessRuleException("API version must use Meta's vNN.N format.");
        }
        if (!string.IsNullOrWhiteSpace(x.TestRecipientNumber) && !Recipient().IsMatch(NonDigits().Replace(x.TestRecipientNumber, string.Empty))) throw new BusinessRuleException("Test recipient must be a valid international WhatsApp number including country code.");
    }
    internal static string? PreferProvided(string? value, string? existing) => string.IsNullOrWhiteSpace(value) ? existing : value.Trim();
    internal static string? ExistingAccessTokenForMode(string providerMode, string? existingProviderMode, string? existingProtectedToken) =>
        providerMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(existingProviderMode, WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase) ? existingProtectedToken : null
            : existingProtectedToken;
    internal static bool IsExistingLiveConnectionUnchanged(string providerMode, bool enabled, string? existingProviderMode,
        bool existingEnabled, string? existingStatus, string? waba, string? existingWaba, string? phone, string? existingPhone,
        string? accessToken, string? existingProtectedToken) =>
        providerMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase) && enabled && existingEnabled
        && string.Equals(existingProviderMode, WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)
        && existingStatus == WhatsAppConnectionStatuses.Connected
        && string.Equals(waba, existingWaba, StringComparison.Ordinal)
        && string.Equals(phone, existingPhone, StringComparison.Ordinal)
        && string.Equals(accessToken, existingProtectedToken, StringComparison.Ordinal);
    internal static string ResolveSaveStatus(bool isLive, bool enabled, bool hasLiveConnection, bool remainsConnected) =>
        !enabled ? WhatsAppConnectionStatuses.Disabled
        : !isLive ? WhatsAppConnectionStatuses.Configured
        : remainsConnected ? WhatsAppConnectionStatuses.Connected
        : hasLiveConnection ? WhatsAppConnectionStatuses.Configured
        : WhatsAppConnectionStatuses.NotConfigured;
    private static bool IsOptionalDigits(string? value) => string.IsNullOrWhiteSpace(value) || Digits().IsMatch(value.Trim());
    private static bool FixedTimeEquals(string? a, string b) { if (a is null) return false; var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b); return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y); }
    internal static Guid? ResolveUniqueTenantToken(IEnumerable<(Guid TenantId, string? VerifyToken)> candidates, string verifyToken)
    {
        Guid? match = null;
        foreach (var candidate in candidates)
        {
            if (!FixedTimeEquals(candidate.VerifyToken, verifyToken)) continue;
            if (match is not null) return null;
            match = candidate.TenantId;
        }
        return match;
    }
    internal static bool ValidSignature(string? signature, ReadOnlySpan<byte> body, string secret) { if (signature is null || signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false; try { var supplied = Convert.FromHexString(signature[7..]); var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body); return CryptographicOperations.FixedTimeEquals(supplied, expected); } catch (FormatException) { return false; } }
    [GeneratedRegex("^[0-9]+$")] private static partial Regex Digits();
    [GeneratedRegex("[^0-9]")] private static partial Regex NonDigits();
    [GeneratedRegex("^[1-9][0-9]{7,14}$")] private static partial Regex Recipient();
    [GeneratedRegex("^v[0-9]{1,3}\\.[0-9]+$")] private static partial Regex Version();
    [GeneratedRegex("^\\s*REF\\s+([A-Z2-9]{6,20})\\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)] private static partial Regex ReferralCommand();
    private sealed record ConfigRow(Guid TenantId, string ProviderMode, string? MetaAppId, string? WabaId, string? PhoneNumberId, string? DisplayPhoneNumber, string? BusinessDisplayName, string? AccessTokenProtected, string? WebhookVerifyTokenProtected, string? AppSecretProtected, string? ApiVersion, string? TestRecipientNumber, bool IsEnabled, string ConnectionStatus, DateTimeOffset? LastValidatedOn, string? LastError, DateTimeOffset? LastWebhookVerifiedOn, DateTimeOffset? LastWebhookReceivedOn, string? LastWebhookEventType, string? LastWebhookMetaMessageId, long DuplicateWebhookCount);
    private sealed record PlatformRow(string MetaAppId,string AppSecretProtected,string WebhookVerifyTokenProtected,bool IsEnabled,DateTimeOffset? ModifiedOn);
    internal sealed record WebhookEnvelope(string WabaId,string? PhoneNumberId,string ChangeType,IReadOnlyCollection<WebhookTransportEvent> Events);
    internal sealed record WebhookTransportEvent(string EventKey, string MetaMessageId, string EventType,
        string Direction, string? ContactNumber, string? MessageType, string? Status, DateTimeOffset EventTimestamp, string? MessageText, string? ProfileName,
        string? PricingCategory = null, bool? MetaBillable = null, string? PricingModel = null,
        string? InteractiveActionId = null, string? CatalogId = null,
        IReadOnlyCollection<WhatsAppCommerceInboundProduct>? CommerceProducts = null)
    {
        public IReadOnlyCollection<WhatsAppCommerceInboundProduct> Products => CommerceProducts ?? Array.Empty<WhatsAppCommerceInboundProduct>();
    }
}

internal static partial class WhatsAppLogs
{
    [LoggerMessage(2101, LogLevel.Warning, "Meta WhatsApp connection validation failed for tenant {TenantId} with HTTP {StatusCode}.")]
    public static partial void MetaValidationFailed(ILogger logger, Guid tenantId, int statusCode);
    [LoggerMessage(2102, LogLevel.Information, "WhatsApp webhook received for tenant {TenantId}, masked phone number ID {MaskedPhoneNumberId}, fields {Fields}.")]
    public static partial void WebhookReceived(ILogger logger, Guid tenantId, string maskedPhoneNumberId, string fields);
    [LoggerMessage(2103, LogLevel.Information, "WhatsApp transport {ProviderMode} tenant {TenantId} event {EventType} message {MetaMessageId} direction {Direction} result {Result}.")]
    public static partial void TransportProcessed(ILogger logger, string providerMode, Guid tenantId, string eventType,
        string metaMessageId, string direction, string result);
    [LoggerMessage(2104, LogLevel.Information, "WhatsApp webhook verification request received; subscribe mode valid: {ModeValid}.")]
    public static partial void WebhookVerificationReceived(ILogger logger, bool modeValid);
    [LoggerMessage(2105, LogLevel.Information, "WhatsApp webhook verification configuration source {ConfigurationSource} resolved: {Resolved}.")]
    public static partial void WebhookVerificationConfiguration(ILogger logger, string configurationSource, bool resolved);
    [LoggerMessage(2106, LogLevel.Information, "WhatsApp webhook verification token match for source {ConfigurationSource}: {Matched}.")]
    public static partial void WebhookVerificationTokenResult(ILogger logger, string configurationSource, bool matched);
    [LoggerMessage(2107, LogLevel.Information, "WhatsApp webhook verification challenge returned for source {ConfigurationSource}.")]
    public static partial void WebhookVerificationChallengeReturned(ILogger logger, string configurationSource);
    [LoggerMessage(2108, LogLevel.Warning, "WhatsApp webhook verification credential for source {ConfigurationSource} could not be decrypted.")]
    public static partial void WebhookVerificationCredentialUnreadable(ILogger logger, string configurationSource);
    [LoggerMessage(2110, LogLevel.Information, "WhatsApp webhook POST received; X-Hub-Signature-256 header present: {SignaturePresent}.")]
    public static partial void WebhookPostReceived(ILogger logger, bool signaturePresent);
    [LoggerMessage(2111, LogLevel.Information, "WhatsApp webhook POST configuration source {ConfigurationSource} resolved: {Resolved}.")]
    public static partial void WebhookPostConfiguration(ILogger logger, string configurationSource, bool resolved);
    [LoggerMessage(2112, LogLevel.Information, "WhatsApp webhook POST signature check for source {ConfigurationSource}; App Secret decrypted: {AppSecretDecrypted}; signature valid: {SignatureValid}.")]
    public static partial void WebhookPostSignature(ILogger logger, string configurationSource, bool appSecretDecrypted, bool signatureValid);
    [LoggerMessage(2113, LogLevel.Information, "WhatsApp webhook POST accepted: {Accepted}; result: {Result}.")]
    public static partial void WebhookPostOutcome(ILogger logger, bool accepted, string result);
    [LoggerMessage(2114, LogLevel.Information, "WhatsApp webhook POST processing skipped for tenant {TenantId}; reason: {Reason}.")]
    public static partial void WebhookPostProcessingSkipped(ILogger logger, Guid tenantId, string reason);
    [LoggerMessage(2115, LogLevel.Information, "WhatsApp webhook POST acknowledged with HTTP {StatusCode} after {DurationMilliseconds} ms.")]
    public static partial void WebhookPostAcknowledged(ILogger logger, int statusCode, double durationMilliseconds);
    [LoggerMessage(2116, LogLevel.Information, "WhatsApp webhook envelope change {ChangeType}, masked phone number ID {MaskedPhoneNumberId}, transport events {EventCount}.")]
    public static partial void WebhookPostEnvelope(ILogger logger, string changeType, string maskedPhoneNumberId, int eventCount);
    [LoggerMessage(2117, LogLevel.Warning, "WhatsApp webhook configuration was not resolved for masked phone number ID {MaskedPhoneNumberId} and masked WABA ID {MaskedWabaId}; event was not tenant-routed.")]
    public static partial void WebhookPostConfigurationNotResolved(ILogger logger, string maskedPhoneNumberId, string maskedWabaId);
    [LoggerMessage(2118, LogLevel.Information, "WhatsApp webhook change {ChangeType} ignored; reason: {Reason}.")]
    public static partial void WebhookPostIgnored(ILogger logger, string changeType, string reason);
    [LoggerMessage(2119, LogLevel.Warning, "WhatsApp webhook payload rejected; parser error category: {ErrorCategory}.")]
    public static partial void WebhookPostPayloadRejected(ILogger logger, string errorCategory);
    [LoggerMessage(2120, LogLevel.Error, "WhatsApp webhook stage failed; reason: {Reason}; error category: {ErrorCategory}.")]
    public static partial void WebhookPostFailure(ILogger logger, string reason, string errorCategory);
}
