namespace WhatsBiz.Application.Features.WhatsApp;

public static class WhatsAppConnectionStatuses
{
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string Configured = "CONFIGURED";
    public const string Connected = "CONNECTED";
    public const string Error = "ERROR";
    public const string Disabled = "DISABLED";
}

public static class WhatsAppProviderModes
{
    public const string Mock = "MOCK";
    public const string MetaTest = "META_TEST";
    public const string Live = "LIVE";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Mock, MetaTest, Live };
}

public sealed record WhatsAppConfigurationDto(string ProviderMode, string? MetaAppId, string? WhatsAppBusinessAccountId, string? PhoneNumberId,
    string? DisplayPhoneNumber, string? BusinessDisplayName, string? ApiVersion, string? TestRecipientNumber, bool IsEnabled,
    string ConnectionStatus, DateTimeOffset? LastValidatedDate, string? LastError,
    bool HasAccessToken, bool HasWebhookVerifyToken, bool HasAppSecret, bool UsesSharedPlatformCredentials = false);

public sealed record SaveWhatsAppConfigurationInput(string ProviderMode, string? MetaAppId, string? WhatsAppBusinessAccountId, string? PhoneNumberId,
    string ApiVersion, string? TestRecipientNumber, bool IsEnabled, string? AccessToken, string? WebhookVerifyToken, string? AppSecret);

public sealed record WhatsAppConnectionResult(bool Succeeded, string ConnectionStatus,
    string? DisplayPhoneNumber, string? BusinessDisplayName, DateTimeOffset ValidatedAt, string? Message);
public sealed record SendWhatsAppTestMessageInput(string RecipientNumber, string? Message);
public sealed record WhatsAppTestMessageResult(bool Succeeded, string? MetaMessageId, DateTimeOffset AttemptedAt, string? Message);
public sealed record WhatsAppMetaTestDiagnosticsDto(string WebhookPath, string? WebhookCallbackUrl,
    DateTimeOffset? LastWebhookVerifiedOn, DateTimeOffset? LastWebhookReceivedOn, string? LastInboundEventType,
    string? LastMetaMessageId, bool TenantResolutionSucceeded, long DuplicateWebhookCount,
    DateTimeOffset? LastTestMessageOn, string? LastTestMessageId);
public sealed record WhatsAppPlatformConfigurationDto(string? MetaAppId, bool IsEnabled,
    bool HasAppSecret, bool HasWebhookVerifyToken, DateTimeOffset? ModifiedOn);
public sealed record SaveWhatsAppPlatformConfigurationInput(string MetaAppId, bool IsEnabled,
    string? AppSecret, string? WebhookVerifyToken);
public sealed record RetailerWhatsAppConnectionDto(Guid TenantId, string TenantKey, string TenantName,
    bool TenantIsActive, string? ProviderMode, string? WabaId, string? PhoneNumberId,
    string? DisplayPhoneNumber, string? BusinessDisplayName, bool ConfigurationIsEnabled,
    string ConnectionStatus, DateTimeOffset? LastValidatedOn, bool UsesSharedPlatformCredentials);

public interface IWhatsAppService
{
    Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token);
    Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token);
    Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token);
    Task<WhatsAppTestMessageResult> SendTestMessageAsync(Guid tenantId, SendWhatsAppTestMessageInput input, CancellationToken token);
    Task<WhatsAppMetaTestDiagnosticsDto> GetDiagnosticsAsync(Guid tenantId, CancellationToken token);
    Task<WhatsAppPlatformConfigurationDto> GetPlatformConfigurationAsync(CancellationToken token);
    Task<WhatsAppPlatformConfigurationDto> SavePlatformConfigurationAsync(SaveWhatsAppPlatformConfigurationInput input, string? actor, CancellationToken token);
    Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> GetRetailerConnectionsAsync(CancellationToken token);
    Task<string?> VerifyWebhookAsync(string mode, string verifyToken, string challenge, CancellationToken token);
    Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token);
}
