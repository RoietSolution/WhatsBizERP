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
