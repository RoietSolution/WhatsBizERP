namespace WhatsBiz.Infrastructure.Storefront;

public sealed class PublicRoute
{
    public Guid RouteId { get; set; }
    public string OriginalSlug { get; set; } = "";
    public string NormalizedSlug { get; private set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Guid? CityId { get; set; }
    public string RouteStatus { get; set; } = "ACTIVE";
    public byte[] Version { get; set; } = [];
    public MarketplaceCity? City { get; set; }
}

public sealed class MarketplaceCity
{
    public Guid CityId { get; set; }
    public string CityName { get; set; } = "";
    public string StateName { get; set; } = "";
    public string MarketplaceName { get; set; } = "";
    public string? Description { get; set; }
    public string? BannerHeadline { get; set; }
    public string? BannerSubtitle { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? BannerMediaId { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? ExternalMarketingDomain { get; set; }
    public bool IsActive { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] Version { get; set; } = [];
    public PublicRoute? Route { get; set; }
}

public sealed class CityMarketplaceMedia
{
    public Guid MediaId { get; set; }
    public Guid CityId { get; set; }
    public string ResourceType { get; set; } = "";
    public string? ResourceKey { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "image/webp";
    public string ThumbnailContentType { get; set; } = "image/webp";
    public string StorageProvider { get; set; } = "DATABASE";
    public string? ObjectKey { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public byte[]? ImageData { get; set; }
    public byte[]? ThumbnailData { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class PartnerSeller
{
    public Guid PartnerSellerId { get; set; }
    public Guid OperatingTenantId { get; set; }
    public string SellerName { get; set; } = "";
    public string SellerType { get; set; } = "OTHER";
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public Guid? CityId { get; set; }
    public string? Locality { get; set; }
    public string? BusinessRegistrationReference { get; set; }
    public string? TaxRegistrationReference { get; set; }
    public string? FoodRegistrationReference { get; set; }
    public string VerificationStatus { get; set; } = "PENDING";
    public string SellerStatus { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public byte[] Version { get; set; } = [];
}

public sealed class CityRetailer
{
    public Guid CityId { get; set; }
    public Guid TenantId { get; set; }
    public string? Locality { get; set; }
    public string? ServiceArea { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class PartnerProductListing
{
    public Guid OperatingTenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PartnerSellerId { get; set; }
    public string ListingStatus { get; set; } = "PENDING_APPROVAL";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public byte[] Version { get; set; } = [];
}

public sealed record PublicCityProduct(Guid Id, Guid OperatingTenantId, string OperatingStoreKey, string OperatingStoreName,
    Guid CategoryId, string CategoryName, Guid BrandId, string BrandName, string Name, string Description,
    string? ImageUrl, decimal SellingPrice, decimal? CompareAtPrice, decimal GstPercentage, string GstPricingType,
    string Availability, bool CanOrderNow, string? AvailabilityMessage, bool IsPurchasable, string? NotPurchasableReason,
    Guid? PartnerSellerId, string? SellerName);

public static class PublicSlugPolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "ref", "share", "cart", "checkout", "products", "categories", "search", "offers", "orders", "addresses", "account", "auth", "feedback", "payment-return", "assets", "city", "stores", "api", "admin" };

    public static string Normalize(string slug)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        if (value.Length is < 1 or > 120 || value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) || value[0] == '-' || value[^1] == '-')
            throw new ArgumentException("Use a 1–120 character public slug containing letters, numbers and interior hyphens only.");
        if (Reserved.Contains(value)) throw new ArgumentException("That slug is reserved for an application route.");
        return value;
    }

    public static string NormalizeRouteSegment(string slug)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        if (value.Length is < 1 or > 120 || value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) || value[0] is '-' or '_' || value[^1] is '-' or '_')
            throw new ArgumentException("Use a 1–120 character route containing letters, numbers, hyphens or underscores, without a separator at either end.");
        if (Reserved.Contains(value)) throw new ArgumentException("That slug is reserved for an application route.");
        return value;
    }
}
