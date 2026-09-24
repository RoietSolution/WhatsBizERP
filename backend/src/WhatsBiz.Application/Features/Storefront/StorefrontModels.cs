namespace WhatsBiz.Application.Features.Storefront;

public sealed record StorefrontStoreDto(string StoreKey, string Name, string Tagline, string? LogoUrl, string AccentColor, string DeliveryMessage);
public sealed record StorefrontCategoryDto(Guid Id, string Name);
public sealed record StorefrontProductDto(Guid Id, Guid CategoryId, string Name, string Description, string? ImageUrl, decimal SellingPrice, decimal? CompareAtPrice, string Availability, string UnitLabel);
public sealed record StorefrontImage(string ContentType, byte[] Content);
public sealed record StorefrontCheckoutItem(Guid ProductId, decimal Quantity);
public sealed record StorefrontCheckoutInput(string CustomerName, string Mobile, string? Email,
    string DeliveryAddress, IReadOnlyCollection<StorefrontCheckoutItem> Items);
public sealed record StorefrontCheckoutResult(Guid OrderId, string OrderNumber, decimal Amount,
    string Currency, Guid PaymentId, string CheckoutUrl);

public interface IStorefrontService
{
    Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontProductDto>?> GetProductsAsync(string storeKey, CancellationToken token);
    Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, CancellationToken token);
}

public interface IStorefrontCheckoutService
{
    Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, CancellationToken token);
}
