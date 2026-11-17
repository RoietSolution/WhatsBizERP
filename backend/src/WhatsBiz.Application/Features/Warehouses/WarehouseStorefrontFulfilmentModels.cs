namespace WhatsBiz.Application.Features.Warehouses;

public sealed record WarehouseStorefrontSellerAreaInput(
    string SellerKind,
    Guid? PartnerSellerId,
    bool IsEnabled,
    IReadOnlyCollection<string> Pincodes);

public sealed record WarehouseStorefrontFulfilmentInput(
    IReadOnlyCollection<WarehouseStorefrontSellerAreaInput> SellerAreas);

public sealed record WarehouseStorefrontSellerAreaDto(
    string SellerKind,
    Guid? PartnerSellerId,
    string SellerName,
    bool IsEnabled,
    IReadOnlyCollection<string> Pincodes);

public sealed record WarehouseStorefrontFulfilmentDto(
    Guid WarehouseId,
    bool WarehouseIsActive,
    bool StoreDeliveryEnabled,
    string DeliveryCoverageMode,
    bool IsMarketplaceEligible,
    IReadOnlyCollection<string> RetailerServiceablePincodes,
    IReadOnlyCollection<WarehouseStorefrontSellerAreaDto> SellerAreas);

public sealed record WarehouseFulfilmentEligibilityRequest(
    Guid TenantId,
    Guid WarehouseId,
    Guid ProductId,
    string SellerKind,
    Guid? PartnerSellerId,
    string Pincode,
    Guid? MarketplaceCityId = null);

public sealed record WarehouseFulfilmentEligibilityResult(
    bool IsEligible,
    string Code,
    string Message);

public static class WarehouseStorefrontEligibilityPolicy
{
    public static WarehouseFulfilmentEligibilityResult Evaluate(
        bool warehouseBelongsToTenant,
        bool warehouseIsActive,
        bool explicitlyConfigured,
        bool sellerRelationshipValid,
        bool storeDeliveryEnabled,
        bool retailerServicesPincode,
        bool warehouseServicesPincode)
    {
        if (!warehouseBelongsToTenant)
            return new(false, "WAREHOUSE_NOT_FOUND", "The warehouse is not available for this retailer.");
        if (!warehouseIsActive)
            return new(false, "WAREHOUSE_INACTIVE", "This warehouse is inactive.");
        if (!sellerRelationshipValid)
            return new(false, "SELLER_INELIGIBLE", "The product seller is not eligible for this warehouse.");
        if (!storeDeliveryEnabled)
            return new(false, "STORE_DELIVERY_DISABLED", "Store delivery is currently unavailable.");
        if (!retailerServicesPincode)
            return new(false, "OUTSIDE_RETAILER_SERVICE_AREA", "The retailer does not deliver to this pincode.");
        if (!explicitlyConfigured || !warehouseServicesPincode)
            return new(false, "WAREHOUSE_SERVICE_AREA_NOT_CONFIGURED", "This warehouse is not enabled for the delivery area.");
        return new(true, "ELIGIBLE", "The warehouse can fulfil this storefront order.");
    }
}

public interface IWarehouseStorefrontFulfilmentService
{
    Task<WarehouseStorefrontFulfilmentDto> GetAsync(Guid warehouseId, CancellationToken token);
    Task<WarehouseStorefrontFulfilmentDto> SaveAsync(Guid warehouseId, WarehouseStorefrontFulfilmentInput input, CancellationToken token);
    Task<WarehouseFulfilmentEligibilityResult> CheckAsync(WarehouseFulfilmentEligibilityRequest request, CancellationToken token);
}
