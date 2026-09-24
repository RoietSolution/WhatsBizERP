namespace WhatsBiz.Application.Features.Payments;

public static class PaymentProviders
{
    public const string Razorpay = "RAZORPAY";
    public const string DirectUpi = "DIRECT_UPI";
    public const string Cod = "COD";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([Razorpay, DirectUpi, Cod], StringComparer.Ordinal);
}

public static class CommercePaymentStatuses
{
    public const string Pending = "PENDING";
    public const string PendingVerification = "PENDING_VERIFICATION";
    public const string Paid = "PAID";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Refunded = "REFUNDED";
    public const string CodPending = "COD_PENDING";
}

public sealed record PaymentProviderSetting(string Provider, bool IsEnabled, bool IsDefault,
    bool IsConfigured, string? MaskedKeyId, bool HasKeySecret, bool HasWebhookSecret,
    bool IsTestMode, string? UpiVpa, string? PayeeName);
public sealed record PaymentSettingsDto(bool OnlinePaymentEnabled, IReadOnlyCollection<PaymentProviderSetting> Providers);
public sealed record SaveRazorpayConfiguration(string KeyId, string? KeySecret, string? WebhookSecret,
    bool IsEnabled, bool IsDefault, bool IsTestMode);
public sealed record SaveDirectUpiConfiguration(string UpiVpa, string PayeeName, bool IsEnabled, bool IsDefault);
public sealed record SaveCodConfiguration(bool IsEnabled, bool IsDefault);
public sealed record SavePaymentOptions(bool OnlinePaymentEnabled);
public sealed record EnabledPaymentMethod(string Provider, string Label, bool IsDefault);
public sealed record CreatePaymentAttemptInput(Guid OrderId, string Provider);
public sealed record CommercePaymentDto(Guid PaymentId, Guid TenantId, string TenantName, Guid OrderId, string OrderNumber, string? CustomerName,
    string Provider, decimal Amount, string Currency, string Status, string? ProviderOrderId,
    string? ProviderPaymentId, string? PaymentLink, string? TransactionReference,
    DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, DateTimeOffset? VerifiedAt, string? VerifiedBy);
public sealed record PaymentAttemptResult(CommercePaymentDto Payment, string? PaymentAction);
public sealed record PaymentQuery(DateTimeOffset? DateFrom = null, DateTimeOffset? DateTo = null, string? Status = null, string? Provider = null, int PageNumber = 1, int PageSize = 50);
public sealed record PlatformPaymentQuery(Guid? TenantId = null, DateTimeOffset? DateFrom = null, DateTimeOffset? DateTo = null, string? Status = null, string? Provider = null, int PageNumber = 1, int PageSize = 50);
public sealed record PagedPaymentsDto(IReadOnlyCollection<CommercePaymentDto> Items, long TotalCount, int PageNumber, int PageSize);
public sealed record VerifyDirectUpiInput(string? Reference);

public sealed record PaymentGatewayConfiguration(string Provider, string? KeyId, string? KeySecret,
    string? WebhookSecret, bool IsTestMode, string? UpiVpa, string? PayeeName);
public sealed record GatewayCreateRequest(Guid PaymentId, Guid OrderId, string OrderNumber,
    decimal Amount, string Currency, string TransactionReference, string? CustomerName, string? CustomerMobile);
public sealed record GatewayCreateResult(string? ProviderOrderId, string? ProviderReference,
    string? PaymentLink, string? PaymentAction);
public sealed record GatewayStatusResult(string Status, string? ProviderPaymentId);
public sealed record GatewayWebhookResult(bool SignatureValid, string? EventId, string? EventType,
    string? ProviderOrderId, string? ProviderReference, string? ProviderPaymentId,
    decimal? Amount, string? Currency, bool IsPaid, bool IsFailed);

public interface IPaymentGateway
{
    string Provider { get; }
    Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration, GatewayCreateRequest request, CancellationToken token);
    Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration, string providerReference, CancellationToken token);
    GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration, ReadOnlyMemory<byte> rawBody,
        string signature, string? eventId);
    Task RefundPaymentAsync(PaymentGatewayConfiguration configuration, string providerPaymentId, decimal amount, string currency, CancellationToken token)
        => throw new NotSupportedException($"Refunds are not supported by {Provider}.");
}

public interface IPaymentGatewayResolver { IPaymentGateway Resolve(string provider); }

public interface ICommercePaymentService
{
    Task<PaymentSettingsDto> GetSettingsAsync(CancellationToken token);
    Task<PaymentSettingsDto> SaveRazorpayAsync(SaveRazorpayConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveDirectUpiAsync(SaveDirectUpiConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveCodAsync(SaveCodConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveOptionsAsync(SavePaymentOptions input, string actor, CancellationToken token);
    Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsAsync(CancellationToken token);
    Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsForTenantAsync(Guid trustedTenantId, CancellationToken token);
    Task<PaymentAttemptResult> CreateAttemptAsync(CreatePaymentAttemptInput input, string actor, CancellationToken token);
    Task<PaymentAttemptResult> CreateAttemptForTenantAsync(Guid trustedTenantId, CreatePaymentAttemptInput input, string actor, CancellationToken token);
    Task<CommercePaymentDto> GetPaymentAsync(Guid paymentId, CancellationToken token);
    Task<PagedPaymentsDto> ListAsync(PaymentQuery query, CancellationToken token);
    Task<CommercePaymentDto> GetPaymentForAdministrationAsync(Guid paymentId, CancellationToken token);
    Task<PagedPaymentsDto> ListForAdministrationAsync(PlatformPaymentQuery query, CancellationToken token);
    Task<PaymentSettingsDto> GetSettingsForTenantAsync(Guid trustedTenantId, CancellationToken token);
    Task<PaymentSettingsDto> SaveRazorpayForTenantAsync(Guid trustedTenantId, SaveRazorpayConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveDirectUpiForTenantAsync(Guid trustedTenantId, SaveDirectUpiConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveCodForTenantAsync(Guid trustedTenantId, SaveCodConfiguration input, string actor, CancellationToken token);
    Task<PaymentSettingsDto> SaveOptionsForTenantAsync(Guid trustedTenantId, SavePaymentOptions input, string actor, CancellationToken token);
    Task<CommercePaymentDto> VerifyDirectUpiAsync(Guid paymentId, VerifyDirectUpiInput input, Guid userId, string actor, CancellationToken token);
    Task ProcessRazorpayWebhookAsync(ReadOnlyMemory<byte> rawBody, string signature, string? eventId, CancellationToken token);
}
