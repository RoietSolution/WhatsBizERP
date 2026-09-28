namespace WhatsBiz.Application.Features.Payments;

public sealed record StorefrontRefundDto(Guid RefundId, Guid InvoiceId, string Provider,
    decimal RefundAmount, string RefundStatus, string? ProviderRefundId,
    string? SettlementMode, string? SettlementReference, DateTimeOffset? RefundedAt);

public sealed record ConfirmManualRefundInput(string SettlementMode, string ExternalReference,
    DateTimeOffset SettledAt);

public interface ICommerceRefundService
{
    Task<StorefrontRefundDto> PrepareFullCancellationAsync(Guid tenantId, Guid invoiceId,
        Guid actorId, CancellationToken token);
    Task<StorefrontRefundDto> StartRazorpayAsync(Guid tenantId, Guid refundId,
        CancellationToken token);
    Task<StorefrontRefundDto> ReconcileRazorpayAsync(Guid tenantId, Guid refundId,
        CancellationToken token);
    Task<StorefrontRefundDto> ConfirmManualAsync(Guid tenantId, Guid refundId,
        ConfirmManualRefundInput input, string actor, CancellationToken token);
    Task<StorefrontRefundDto> GetAsync(Guid tenantId, Guid refundId, CancellationToken token);
    Task ProcessRazorpayRefundWebhookAsync(ReadOnlyMemory<byte> rawBody,
        string signature, CancellationToken token);
}
