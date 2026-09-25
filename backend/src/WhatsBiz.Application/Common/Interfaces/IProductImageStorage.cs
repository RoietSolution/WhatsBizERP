namespace WhatsBiz.Application.Common.Interfaces;

public static class ProductImageStorageProviders
{
    public const string Database = "DATABASE";
    public const string Local = "LOCAL";
    public const string S3 = "S3";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Database, Local, S3 };
}

public sealed record ProductImageStorageWriteRequest(Guid TenantId, Guid ProductId, Guid ImageId,
    byte[] CatalogContent, byte[] ThumbnailContent, string ContentType);
public sealed record StoredProductImage(string Provider, string? ObjectKey, string? ThumbnailObjectKey,
    long CatalogSizeBytes, long ThumbnailSizeBytes, string ContentHash);
public sealed record ProductImageStorageReadRequest(Guid TenantId, string Provider, string? ObjectKey,
    byte[] DatabaseContent, string ContentType);
public sealed record ProductImageStorageContent(byte[] Content, string ContentType);
public sealed record ProductImageStorageDeleteRequest(Guid TenantId, string Provider, string? ObjectKey, string? ThumbnailObjectKey);

public interface IProductImageStorage
{
    string ActiveProvider { get; }
    Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken);
    Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken);
}

public sealed record StorefrontMediaStorageWriteRequest(Guid TenantId, string ResourceType, Guid ResourceId,
    byte[] CatalogContent, byte[] ThumbnailContent, string ContentType);
public sealed record StoredStorefrontMedia(string Provider, string? ObjectKey, string? ThumbnailObjectKey,
    long CatalogSizeBytes, long ThumbnailSizeBytes, string ContentHash);
public sealed record StorefrontMediaStorageReadRequest(Guid TenantId, string Provider, string? ObjectKey,
    byte[] DatabaseContent, string ContentType);
public sealed record StorefrontMediaStorageDeleteRequest(Guid TenantId, string Provider, string? ObjectKey,
    string? ThumbnailObjectKey);

/// <summary>Uses the same configured database/local/S3 stores and tenant-key validation as product images.</summary>
public interface IStorefrontMediaStorage
{
    string ActiveProvider { get; }
    Task<StoredStorefrontMedia> StoreStorefrontAsync(StorefrontMediaStorageWriteRequest request, CancellationToken cancellationToken);
    Task<ProductImageStorageContent?> ReadStorefrontAsync(StorefrontMediaStorageReadRequest request, CancellationToken cancellationToken);
    Task DeleteStorefrontAsync(StorefrontMediaStorageDeleteRequest request, CancellationToken cancellationToken);
}
