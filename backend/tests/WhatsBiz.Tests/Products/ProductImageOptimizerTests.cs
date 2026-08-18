using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductImageOptimizerTests
{
    private readonly ProductImageOptimizer optimizer = new();

    [Fact]
    public async Task JpegGeneratesWebpCatalogAndThumbnailWithoutUpscaling()
    {
        var bytes = CreateImage(640, 320, jpeg: true);
        var result = await optimizer.OptimizeAsync("phone.jpg", "image/jpeg", bytes, CancellationToken.None);
        result.ContentType.Should().Be("image/webp");
        result.Width.Should().Be(640);
        result.Height.Should().Be(320);
        result.ThumbnailWidth.Should().Be(300);
        result.ThumbnailHeight.Should().Be(150);
        result.CatalogData.Should().NotBeEmpty();
        result.ThumbnailData.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PngIsAcceptedAndLargeImagePreservesAspectRatio()
    {
        var result = await optimizer.OptimizeAsync("photo.png", "image/png", CreateImage(2400, 1200, jpeg: false), CancellationToken.None);
        using var catalog = Image.Load(result.CatalogData);
        catalog.Width.Should().Be(1200);
        catalog.Height.Should().Be(600);
    }

    [Fact]
    public async Task UnsupportedCorruptAndOversizedFilesAreRejected()
    {
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.gif", "image/gif", [1, 2, 3], CancellationToken.None)).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.jpg", "image/jpeg", [1, 2, 3], CancellationToken.None)).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.jpg", "image/jpeg", new byte[5 * 1024 * 1024 + 1], CancellationToken.None)).Should().ThrowAsync<Exception>();
    }

    private static byte[] CreateImage(int width, int height, bool jpeg)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 120, 80));
        using var stream = new MemoryStream();
        if (jpeg) image.SaveAsJpeg(stream, new JpegEncoder { Quality = 85 });
        else image.SaveAsPng(stream, new PngEncoder());
        return stream.ToArray();
    }
}
