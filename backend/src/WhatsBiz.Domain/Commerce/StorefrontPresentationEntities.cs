namespace WhatsBiz.Domain.Commerce;

public sealed class StorefrontConfiguration
{
    public Guid TenantId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? AllCategoryMediaId { get; set; }
    public string? Tagline { get; set; }
    public string? AccentColor { get; set; }
    public string? DeliveryMessage { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public bool DeliveryEnabled { get; set; }
    public decimal StandardDeliveryCharge { get; set; }
    public bool FreeDeliveryEnabled { get; set; }
    public bool DeliveryChargeTaxEnabled { get; set; }
    public bool DeliveryChargeIncomePostingEnabled { get; set; }
    public bool ShowProductRatings { get; set; } = true;
    public bool ShowProductReviews { get; set; } = true;
    public int DefaultReturnWindowDays { get; set; } = 7;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StorefrontMedia
{
    public Guid MediaId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ThumbnailContentType { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = string.Empty;
    public string? ObjectKey { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public byte[]? ImageData { get; set; }
    public byte[]? ThumbnailData { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class StorefrontBanner
{
    public Guid BannerId { get; set; }
    public Guid TenantId { get; set; }
    public string Slot { get; set; } = string.Empty;
    public Guid? MediaId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? TargetUrl { get; set; }
    public Guid? PromotionId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StorefrontCategoryImage
{
    public Guid TenantId { get; set; }
    public Guid ProductCategoryId { get; set; }
    public Guid MediaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class StorefrontBannerSlots
{
    public const string Primary = "PRIMARY";
    public const string Secondary = "SECONDARY";
    public static readonly string[] All = [Primary, Secondary];
}

public sealed class StorefrontProductReview
{
    public Guid ReviewId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CustomerId { get; set; }
    public int Rating { get; set; }
    public string ReviewText { get; set; } = string.Empty;
    public string Status { get; set; } = "PUBLISHED";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class StorefrontServiceablePincode
{
    public Guid TenantId { get; set; }
    public string Pincode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StorefrontPromotion
{
    public Guid PromotionId { get; set; }
    public Guid TenantId { get; set; }
    public string OfferName { get; set; } = string.Empty;
    public string OfferType { get; set; } = string.Empty;
    public decimal MinimumPurchaseAmount { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public int? UsageLimitPerCustomer { get; set; }
    public string? ShortDescription { get; set; }
    public string? DetailedDescription { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? PromoCode { get; set; }
    public string? CtaLabel { get; set; }
    public string? EligibleItemsDescription { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
