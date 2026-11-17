using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Exceptions;

namespace WhatsBiz.Application.Features.Storefront;

public sealed record MarketplaceOrderAllocationInput(
    POSPostRequest Invoice,
    string DeliveryAddressSnapshot,
    string Pincode,
    Guid? MarketplaceCityId,
    DateTimeOffset ReservationExpiresAt,
    string CatalogueItemsJson,
    decimal ExpectedPayableAmount,
    string? CheckoutRequestFingerprint = null);

public sealed record MarketplaceAllocatedItem(Guid ProductId, decimal Quantity);

public sealed record MarketplaceFulfilmentAllocationGroup(
    Guid FulfilmentGroupId,
    short GroupNumber,
    Guid WarehouseId,
    string SellerKind,
    Guid? PartnerSellerId,
    IReadOnlyCollection<MarketplaceAllocatedItem> Items);

public sealed record MarketplaceOrderAllocationResult(
    Guid OrderId,
    string OrderNumber,
    decimal GrandTotal,
    string Status,
    IReadOnlyCollection<MarketplaceFulfilmentAllocationGroup> FulfilmentGroups);

public interface IMarketplaceOrderAllocationService
{
    Task<MarketplaceAllocationEligibilityResult> CheckAvailabilityAsync(
        Guid tenantId, IReadOnlyCollection<StorefrontCheckoutItem> items, string pincode, CancellationToken token);

    Task<MarketplaceOrderAllocationResult> CreateHeldOrderAsync(
        Guid tenantId,
        string checkoutIdempotencyKey,
        MarketplaceOrderAllocationInput input,
        CancellationToken token);
}

public sealed record MarketplaceAllocationEligibilityResult(bool IsEligible, string? ReasonCode = null,
    string? CustomerMessage = null, bool MayArriveSeparately = false);

public sealed record MarketplaceStockCandidate(
    Guid WarehouseId,
    Guid? ZoneId,
    Guid? BinId,
    string? BatchNo,
    string? SerialNo,
    decimal AvailableQuantity);

public sealed record MarketplaceStockAllocation(
    Guid WarehouseId,
    Guid? ZoneId,
    Guid? BinId,
    string? BatchNo,
    string? SerialNo,
    decimal Quantity);

public static class MarketplaceWarehouseAllocationPolicy
{
    public static IReadOnlyCollection<MarketplaceStockAllocation> Allocate(
        decimal requestedQuantity, IEnumerable<MarketplaceStockCandidate> eligibleStock)
    {
        if (requestedQuantity <= 0) throw new BusinessRuleException("The requested item quantity must be greater than zero.");
        var allocations = new List<MarketplaceStockAllocation>();
        var remaining = requestedQuantity;
        foreach (var stock in eligibleStock
                     .Where(x => x.AvailableQuantity > 0)
                     .OrderBy(x => x.WarehouseId)
                     .ThenBy(x => x.ZoneId)
                     .ThenBy(x => x.BinId)
                     .ThenBy(x => x.BatchNo, StringComparer.Ordinal)
                     .ThenBy(x => x.SerialNo, StringComparer.Ordinal))
        {
            var quantity = Math.Min(remaining, stock.AvailableQuantity);
            if (quantity <= 0) continue;
            allocations.Add(new(stock.WarehouseId, stock.ZoneId, stock.BinId, stock.BatchNo, stock.SerialNo, quantity));
            remaining -= quantity;
            if (remaining == 0) break;
        }
        if (remaining > 0)
            throw new BusinessRuleException("Insufficient eligible stock. Refresh your cart and try again.");
        return allocations;
    }
}
