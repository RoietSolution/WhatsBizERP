namespace WhatsBiz.Application.Features.WhatsApp;

public static class WhatsAppMessageCategories
{
    public const string Marketing = "MARKETING";
    public const string Utility = "UTILITY";
    public const string Authentication = "AUTHENTICATION";
    public const string Service = "SERVICE";
    public const string Unknown = "UNKNOWN";
    public static readonly string[] All = [Marketing, Utility, Authentication, Service, Unknown];

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        Marketing => Marketing,
        Utility => Utility,
        Authentication => Authentication,
        Service => Service,
        _ => Unknown
    };
}

public sealed record WhatsAppUsageAcceptedMessage(Guid TenantId, string ProviderMode, string PhoneNumberId,
    string MetaMessageId, string RecipientNumber, string? TemplateName, string? MessageCategory,
    DateTimeOffset SentAt);

public sealed record WhatsAppUsageStatus(Guid TenantId, string ProviderMode, string PhoneNumberId,
    string MetaMessageId, string? RecipientNumber, string Status, DateTimeOffset EventAt,
    string? MessageCategory, bool? MetaBillable, string? PricingModel);

public sealed record WhatsAppUsageCurrencyAmount(string Currency, decimal EstimatedMetaCost);
public sealed record WhatsAppUsageCategorySummary(string Category, int DeliveredMessages,
    decimal? EstimatedMetaCost, string? Currency, bool RateConfigured,
    IReadOnlyCollection<WhatsAppUsageCurrencyAmount> CurrencyBreakdown);
public sealed record WhatsAppSubscriptionSummary(string? PlanName, bool WhatsAppCommerceEntitled);
public sealed record WhatsAppUsageSummary(string Period, string? Currency,
    IReadOnlyCollection<WhatsAppUsageCategorySummary> Categories,
    decimal? EstimatedMetaCharges, bool RateConfigurationComplete,
    IReadOnlyCollection<WhatsAppUsageCurrencyAmount> CurrencyBreakdown,
    WhatsAppSubscriptionSummary KhataDhariSubscription);

public interface IWhatsAppUsageBillingService
{
    Task RecordAcceptedAsync(WhatsAppUsageAcceptedMessage message, CancellationToken token);
    Task ApplyStatusAsync(WhatsAppUsageStatus status, CancellationToken token);
    Task<WhatsAppUsageSummary> GetSummaryAsync(Guid tenantId, int year, int month, CancellationToken token);
}
