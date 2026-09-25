namespace WhatsBiz.Application.Features.Storefront;

public sealed record StorefrontBannerDto(string Slot, string ImageUrl, string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder);
public sealed record StorefrontPaymentMethodDto(string Code, string Label, string Description, bool IsDefault, bool UsesHostedPaymentPage);
public sealed record StorefrontStoreDto(string StoreKey, string Name, string Tagline, string? LogoUrl, string AccentColor,
    string DeliveryMessage, IReadOnlyCollection<StorefrontBannerDto> Banners, IReadOnlyCollection<StorefrontPaymentMethodDto> PaymentMethods);
public sealed record StorefrontCategoryDto(Guid Id, string Name, string? ImageUrl);
public sealed record StorefrontProductDto(Guid Id, Guid CategoryId, string Name, string Description, string? ImageUrl, decimal SellingPrice, decimal? CompareAtPrice, string Availability, string? UnitLabel);
public sealed record StorefrontImage(string ContentType, byte[] Content);
public sealed record StorefrontCheckoutItem(Guid ProductId, decimal Quantity);
public sealed record StorefrontCheckoutInput(string CustomerName, string Mobile, string? Email,
    string DeliveryAddress, IReadOnlyCollection<StorefrontCheckoutItem> Items, string PaymentProvider = "RAZORPAY");
public sealed record StorefrontCheckoutResult(Guid OrderId, string OrderNumber, decimal Amount,
    string Currency, Guid PaymentId, string? CheckoutUrl, string PaymentProvider, string PaymentStatus, string? CustomerMessage);

public interface IStorefrontService
{
    Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontProductDto>?> GetProductsAsync(string storeKey, CancellationToken token);
    Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token);
}

public sealed record StorefrontBannerAdminDto(string Slot, bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder, string? ImageUrl);
public sealed record StorefrontAdminDto(string StoreName, string? LogoUrl, IReadOnlyCollection<StorefrontBannerAdminDto> Banners);
public sealed record UpdateStorefrontBannerInput(bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder);

public interface IStorefrontAdministrationService
{
    Task<StorefrontAdminDto> GetAsync(CancellationToken token);
    Task UpdateBannerAsync(string slot, UpdateStorefrontBannerInput input, CancellationToken token);
    Task<StorefrontImage?> GetImageAsync(string resource, Guid? categoryId, CancellationToken token);
    Task UploadImageAsync(string resource, Guid? categoryId, string fileName, Stream content, CancellationToken token);
    Task RemoveImageAsync(string resource, Guid? categoryId, CancellationToken token);
}

public interface IStorefrontCheckoutService
{
    Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, CancellationToken token);
}
