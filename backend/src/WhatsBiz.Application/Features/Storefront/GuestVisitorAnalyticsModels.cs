namespace WhatsBiz.Application.Features.Storefront;

public sealed record GuestAnalyticsStatus(bool Available, string NoticeVersion);
public sealed record GuestAnalyticsConsentInput(Guid VisitorId, Guid SessionId, bool Granted,
    string NoticeVersion, string ViewportCategory, string? TrafficSource = null,
    string? CampaignSource = null, string? CampaignMedium = null, string? CampaignName = null);
public sealed record GuestAnalyticsEventInput(Guid EventId, string EventType, Guid? ProductId = null,
    Guid? CategoryId = null, string? SearchTerm = null, int? ResultCount = null, int? EngagedSeconds = null);
public sealed record GuestAnalyticsBatchInput(Guid SessionId, IReadOnlyList<GuestAnalyticsEventInput> Events);
public sealed record GuestAnalyticsRetentionInput(int RetentionDays, bool PolicyReviewed);
public sealed record GuestAnalyticsSummary(long Visitors, long Sessions, long EngagedSeconds,
    long PageViews, long ProductViews, long CategoryViews, long Searches, long AddToCart, long CheckoutStarted,
    long? CompletedOrders, IReadOnlyDictionary<string,long> Devices,
    IReadOnlyDictionary<string,long> Browsers, IReadOnlyDictionary<string,long> OperatingSystems,
    IReadOnlyDictionary<string,long> TrafficSources,
    IReadOnlyList<GuestAnalyticsProduct> MostViewedProducts,
    GuestAnalyticsOwnerAvailability? Availability = null);
public sealed record GuestAnalyticsOwnerAvailability(int ActiveRetailers, int FeatureEnabledRetailers,
    int ReviewedPolicyRetailers, int EligibleRetailers, bool CollectionEnabled, bool RetentionCleanupEnabled,
    bool RetentionWorkerHealthy, bool? SelectedRetailerActive, bool? SelectedFeatureEnabled,
    bool? SelectedRetentionPolicyReviewed);
public sealed record GuestAnalyticsProduct(Guid ProductId, string ProductName, long Views);
public sealed record GuestAnalyticsTenantOption(Guid TenantId, string StoreName);

public interface IGuestVisitorAnalyticsService
{
    Task<GuestAnalyticsStatus> StatusAsync(string storeKey, CancellationToken token);
    Task SetConsentAsync(string storeKey, GuestAnalyticsConsentInput input, string? userAgent, CancellationToken token);
    Task RecordAsync(string storeKey, GuestAnalyticsBatchInput input, CancellationToken token);
    Task WithdrawAsync(string storeKey, Guid visitorId, Guid sessionId, CancellationToken token);
    Task<GuestAnalyticsSummary> RetailerReportAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token);
    Task<GuestAnalyticsSummary> OwnerReportAsync(Guid? tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token);
    Task<IReadOnlyList<GuestAnalyticsTenantOption>> OwnerFiltersAsync(CancellationToken token);
    Task SetRetentionPolicyAsync(Guid tenantId, GuestAnalyticsRetentionInput input, CancellationToken token);
}
