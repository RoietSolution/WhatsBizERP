using System.Security.Cryptography;

namespace WhatsBiz.Application.Features.Referrals;

public static class ReferralStatuses
{
    public const string Pending = "PENDING"; public const string Qualified = "QUALIFIED";
    public const string Rewarded = "REWARDED"; public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED"; public const string Reversed = "REVERSED";
}

public static class ReferralQualificationTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "CUSTOMER_REGISTERED", "FIRST_ORDER_PLACED", "FIRST_PAID_ORDER", "FIRST_COMPLETED_ORDER", "FIRST_COMPLETED_ORDER_MIN_AMOUNT", "MANUAL_APPROVAL" };
}

public static class ReferralCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string Create(int length = 8)
    {
        if (length is < 6 or > 20) throw new ArgumentOutOfRangeException(nameof(length));
        Span<byte> bytes = stackalloc byte[length]; Span<char> result = stackalloc char[length];
        for (var i = 0; i < length;) { RandomNumberGenerator.Fill(bytes); for (var j = 0; j < bytes.Length && i < length; j++) if (bytes[j] < 224) result[i++] = Alphabet[bytes[j] % Alphabet.Length]; }
        return new string(result);
    }
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}

public sealed record ReferralConfigurationDto(bool IsEnabled, string QualificationType, decimal MinimumQualifyingAmount,
    int ReferrerRewardCoins, int ReferredRewardCoins, int CoinValidityDays, int MaximumRewardedReferralsPerCustomerMonth,
    int MaximumCoinsPerCustomerMonth, bool ReverseOnRefund, int RedemptionCoins, decimal RedemptionValue,
    int MinimumRedemptionCoins, decimal MaximumOrderPercentage, bool AllowWithCoupons, bool AllowDiscountedProducts, bool AllowTax, bool AllowDelivery);
public sealed record ReferralConfigurationInput(bool IsEnabled, string QualificationType, decimal MinimumQualifyingAmount,
    int ReferrerRewardCoins, int ReferredRewardCoins, int CoinValidityDays, int MaximumRewardedReferralsPerCustomerMonth,
    int MaximumCoinsPerCustomerMonth, bool ReverseOnRefund, int RedemptionCoins, decimal RedemptionValue,
    int MinimumRedemptionCoins, decimal MaximumOrderPercentage, bool AllowWithCoupons, bool AllowDiscountedProducts, bool AllowTax, bool AllowDelivery);
public sealed record ReferralCodeDto(Guid ReferralCodeId, Guid CustomerId, string Code, string ReferralUrl, bool IsActive, DateTimeOffset CreatedAt);
public sealed record ReferralDto(Guid ReferralId, Guid ReferrerCustomerId, Guid ReferredCustomerId, string Status,
    string QualificationType, Guid? QualifyingOrderId, DateTimeOffset? QualifiedAt, DateTimeOffset? RewardedAt,
    DateTimeOffset? ReversedAt, string? RejectionReason, DateTimeOffset CreatedAt, string ReferredCustomerDisplay);
public sealed record ReferralCaptureInput(string Code, Guid ReferredCustomerId, string Source = "WEB");
public sealed record ReferralResolutionDto(string Code, string TenantKey, string RetailerName, bool IsEnabled);
public sealed record ReferralMetricsDto(int TotalReferrals, int PendingReferrals, int SuccessfulReferrals, int ReversedReferrals,
    int CoinsIssued, int CoinsRedeemed, int OutstandingCoins, decimal ConversionPercentage, decimal ReferralRevenue,
    IReadOnlyCollection<TopReferrerDto> TopReferrers);
public sealed record TopReferrerDto(Guid CustomerId, string CustomerName, int SuccessfulReferrals, int CoinsEarned);
public sealed record RewardAdjustmentInput(Guid CustomerId, int Coins, string Reason);

public interface ICustomerReferralService
{
    Task<ReferralConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token);
    Task<ReferralConfigurationDto> SaveConfigurationAsync(Guid tenantId, ReferralConfigurationInput input, string? actor, CancellationToken token);
    Task<ReferralCodeDto> GetOrCreateCodeAsync(Guid tenantId, Guid customerId, string? actor, CancellationToken token);
    Task SetCodeActiveAsync(Guid tenantId, Guid customerId, bool active, string? actor, CancellationToken token);
    Task<ReferralResolutionDto?> ResolveCodeAsync(string code, CancellationToken token);
    Task<ReferralDto> CaptureAsync(Guid tenantId, ReferralCaptureInput input, string? actor, CancellationToken token);
    Task ApproveAsync(Guid tenantId, Guid referralId, string? actor, CancellationToken token);
    Task EvaluateOrderAsync(Guid tenantId, Guid orderId, string status, string? actor, CancellationToken token);
    Task ReverseOrderAsync(Guid tenantId, Guid orderId, string reason, string? actor, CancellationToken token);
    Task<IReadOnlyCollection<ReferralDto>> GetHistoryAsync(Guid tenantId, Guid? customerId, int take, CancellationToken token);
    Task<ReferralMetricsDto> GetMetricsAsync(Guid tenantId, CancellationToken token);
    Task AdjustAsync(Guid tenantId, RewardAdjustmentInput input, string? actor, CancellationToken token);
    Task<int> ExpireAsync(Guid? tenantId, int batchSize, string? actor, CancellationToken token);
}
