using ImageMagick;

namespace WhatsBiz.Infrastructure.Products;

internal static class ImageMagickSafety
{
    internal const int MaxUploadBytes = 5 * 1024 * 1024;
    internal const int MaxDecodedDimension = 10_000;
    internal const long MaxDecodedPixels = 40_000_000;

    static ImageMagickSafety()
    {
        ResourceLimits.Width = MaxDecodedDimension;
        ResourceLimits.Height = MaxDecodedDimension;
        ResourceLimits.Area = MaxDecodedPixels;
    }

    internal static void EnsureInitialized() { }
}
