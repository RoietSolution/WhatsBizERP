namespace WhatsBiz.Domain.Commerce;

public sealed class StorefrontConfiguration
{
    public Guid TenantId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public string? Tagline { get; set; }
    public string? AccentColor { get; set; }
    public string? DeliveryMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StorefrontMedia
{
    public Guid MediaId { get; set; }
    public Guid TenantId { get; set; }
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
