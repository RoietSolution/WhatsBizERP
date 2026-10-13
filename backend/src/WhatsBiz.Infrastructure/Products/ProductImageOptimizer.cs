using ImageMagick;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Products;

public sealed class ProductImageOptimizer : IProductImageOptimizer
{
    public const int MaxUploadBytes = ImageMagickSafety.MaxUploadBytes;
    public const int CatalogMaxDimension = 1200;
    public const int ThumbnailMaxDimension = 300;

    static ProductImageOptimizer()
    {
        ImageMagickSafety.EnsureInitialized();
    }

    public Task<OptimizedProductImage> OptimizeAsync(string fileName, string? suppliedContentType, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length == 0) throw new BusinessRuleException("The image is empty.");
        if (content.Length > MaxUploadBytes) throw new BusinessRuleException("The image cannot exceed 5 MB.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new MagickImageInfo(content);
            if (!IsAllowed(info.Format)) throw new BusinessRuleException("Only JPEG, PNG, and WebP images are supported.");
            using var source = new MagickImage(content);
            source.AutoOrient();
            using var catalog = Prepare(source, CatalogMaxDimension);
            using var thumbnail = Prepare(source, ThumbnailMaxDimension);
            return Task.FromResult(new OptimizedProductImage($"{Guid.NewGuid():N}.webp", "image/webp", Encode(catalog, cancellationToken), Encode(thumbnail, cancellationToken), (int)source.Width, (int)source.Height, (int)thumbnail.Width, (int)thumbnail.Height));
        }
        catch (BusinessRuleException) { throw; }
        catch (Exception exception) when (exception is MagickException or ArgumentException or InvalidOperationException)
        { throw new BusinessRuleException("The uploaded file is not a valid JPEG, PNG, or WebP image."); }
    }

    private static bool IsAllowed(MagickFormat format) => format is MagickFormat.Jpeg or MagickFormat.Png or MagickFormat.WebP;
    private static MagickImage Prepare(MagickImage source, int maxDimension)
    {
        if (source.Width <= maxDimension && source.Height <= maxDimension) return (MagickImage)source.Clone();
        var scale = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
        var resized = (MagickImage)source.Clone();
        resized.Resize((uint)Math.Max(1, (int)Math.Round(source.Width * scale)), (uint)Math.Max(1, (int)Math.Round(source.Height * scale)));
        return resized;
    }
    private static byte[] Encode(MagickImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        image.Quality = 82;
        return image.ToByteArray(MagickFormat.WebP);
    }
}
