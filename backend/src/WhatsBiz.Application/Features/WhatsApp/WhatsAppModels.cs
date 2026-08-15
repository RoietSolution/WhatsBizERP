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

public sealed record WhatsAppConfigurationDto(string ProviderMode, string? WhatsAppBusinessAccountId, string? PhoneNumberId,
    string? DisplayPhoneNumber, string? BusinessDisplayName, string? ApiVersion, bool IsEnabled,
    string ConnectionStatus, DateTimeOffset? LastValidatedDate, string? LastError,
    bool HasAccessToken, bool HasWebhookVerifyToken, bool HasAppSecret);

public sealed record SaveWhatsAppConfigurationInput(string ProviderMode, string? WhatsAppBusinessAccountId, string? PhoneNumberId,
    string ApiVersion, bool IsEnabled, string? AccessToken, string? WebhookVerifyToken, string? AppSecret);

public sealed record WhatsAppConnectionResult(bool Succeeded, string ConnectionStatus,
    string? DisplayPhoneNumber, string? BusinessDisplayName, DateTimeOffset ValidatedAt, string? Message);

public interface IWhatsAppService
{
    Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token);
    Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token);
    Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token);
    Task<string?> VerifyWebhookAsync(string mode, string verifyToken, string challenge, CancellationToken token);
    Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token);
}
