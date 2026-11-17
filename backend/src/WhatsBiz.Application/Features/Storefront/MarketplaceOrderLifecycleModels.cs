namespace WhatsBiz.Application.Features.Storefront;

public sealed record MarketplaceOrderLifecycleResult(
    Guid OrderId,
    string LifecycleStatus,
    string InvoiceStatus,
    bool IsReplay);

public interface IMarketplaceOrderLifecycleService
{
    Task<MarketplaceOrderLifecycleResult> CompleteAsync(
        Guid tenantId, Guid orderId, string idempotencyKey, string? actor, CancellationToken token);

    Task<MarketplaceOrderLifecycleResult> CancelAsync(
        Guid tenantId, Guid orderId, string idempotencyKey, string? actor, CancellationToken token);

    Task<int> ExpireDueBatchAsync(Guid tenantId, int batchSize, string? actor, CancellationToken token);

    Task<int> ExpireDueAcrossTenantsAsync(int batchSize, string? actor, CancellationToken token);
}
