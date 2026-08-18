using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Products;

public sealed class ProductImageOptimizer : IProductImageOptimizer
{
    public const int MaxUploadBytes = 5 * 1024 * 1024;
    public const int CatalogMaxDimension = 1200;
    public const int ThumbnailMaxDimension = 300;

    public async Task<OptimizedProductImage> OptimizeAsync(string fileName, string? suppliedContentType, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length == 0) throw new BusinessRuleException("The image is empty.");
        if (content.Length > MaxUploadBytes) throw new BusinessRuleException("The image cannot exceed 5 MB.");
        try
        {
            await using var input = new MemoryStream(content, writable: false);
            var format = await Image.DetectFormatAsync(input, cancellationToken);
            if (format is null || !IsAllowed(format)) throw new BusinessRuleException("Only JPEG, PNG, and WebP images are supported.");
            input.Position = 0;
            using var source = await Image.LoadAsync(input, cancellationToken);
            using var catalog = Prepare(source, CatalogMaxDimension);
            using var thumbnail = Prepare(source, ThumbnailMaxDimension);
            return new($"{Guid.NewGuid():N}.webp", "image/webp", await Encode(catalog, cancellationToken), await Encode(thumbnail, cancellationToken), source.Width, source.Height, thumbnail.Width, thumbnail.Height);
        }
        catch (BusinessRuleException) { throw; }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or ArgumentException)
        { throw new BusinessRuleException("The uploaded file is not a valid JPEG, PNG, or WebP image."); }
    }

    private static bool IsAllowed(IImageFormat format) => format.Name is "JPEG" or "PNG" or "Webp" or "WEBP";
    private static Image Prepare(Image source, int maxDimension)
    {
        if (source.Width <= maxDimension && source.Height <= maxDimension) return source.Clone(_ => { });
        var scale = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
        return source.Clone(context => context.Resize(new ResizeOptions { Size = new Size(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale))), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
    }
    private static async Task<byte[]> Encode(Image image, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = 82, Method = WebpEncodingMethod.BestQuality }, cancellationToken);
        return output.ToArray();
    }
}
