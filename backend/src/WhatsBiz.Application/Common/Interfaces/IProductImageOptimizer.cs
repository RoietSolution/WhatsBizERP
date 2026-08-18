namespace WhatsBiz.Application.Common.Interfaces;

public sealed record OptimizedProductImage(
    string FileName,
    string ContentType,
    byte[] CatalogData,
    byte[] ThumbnailData,
    int Width,
    int Height,
    int ThumbnailWidth,
    int ThumbnailHeight);

public interface IProductImageOptimizer
{
    Task<OptimizedProductImage> OptimizeAsync(string fileName, string? suppliedContentType, byte[] content, CancellationToken cancellationToken);
}
