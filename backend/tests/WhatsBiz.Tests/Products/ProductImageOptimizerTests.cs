using FluentAssertions;
using ImageMagick;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Infrastructure.Products;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Application.Features.Storefront;

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
        using var catalog = new MagickImage(result.CatalogData);
        catalog.Width.Should().Be(1200);
        catalog.Height.Should().Be(600);
    }

    [Fact]
    public async Task WebpIsAcceptedAndRemainsTransparentAfterOptimization()
    {
        using var source = new MagickImage(MagickColors.Transparent, 240, 120);
        source.Settings.BackgroundColor = MagickColors.Transparent;
        var result = await optimizer.OptimizeAsync("transparent.webp", "image/webp", source.ToByteArray(MagickFormat.WebP), CancellationToken.None);

        result.ContentType.Should().Be("image/webp");
        result.Width.Should().Be(240);
        result.Height.Should().Be(120);
        using var catalog = new MagickImage(result.CatalogData);
        catalog.Format.Should().Be(MagickFormat.WebP);
        catalog.HasAlpha.Should().BeTrue();
    }

    [Fact]
    public async Task UnsupportedCorruptAndOversizedFilesAreRejected()
    {
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.gif", "image/gif", [1, 2, 3], CancellationToken.None)).Should().ThrowAsync<BusinessRuleException>();
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.jpg", "image/jpeg", [1, 2, 3], CancellationToken.None)).Should().ThrowAsync<BusinessRuleException>();
        await FluentActions.Awaiting(() => optimizer.OptimizeAsync("x.jpg", "image/jpeg", new byte[5 * 1024 * 1024 + 1], CancellationToken.None)).Should().ThrowAsync<BusinessRuleException>();
    }

    [Theory]
    [InlineData(StorefrontAttachmentScanResult.Infected)]
    [InlineData(StorefrontAttachmentScanResult.Failed)]
    [InlineData(StorefrontAttachmentScanResult.Pending)]
    public void ProductUploadFailsClosedForNonCleanScannerResults(StorefrontAttachmentScanResult result)
    {
        FluentActions.Invoking(() => StorefrontProductImageScanGuard.RequireClean(result))
            .Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ProductUploadAllowsOnlyCleanScannerResult()
    {
        FluentActions.Invoking(() => StorefrontProductImageScanGuard.RequireClean(StorefrontAttachmentScanResult.Clean))
            .Should().NotThrow();
    }

    [Fact]
    public void ProductUploadScansBeforeStorageAndPersistsTheAuthenticatedTenant()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Application/Features/Products/Products/ProductHandlers.cs"));
        var source = File.ReadAllText(sourcePath);
        source.IndexOf("ScanOriginalAsync", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("storage.StoreAsync", StringComparison.Ordinal));
        source.Should().Contain("TenantId = tenantId");
    }

    private static byte[] CreateImage(int width, int height, bool jpeg)
    {
        using var image = new MagickImage(new MagickColor(30, 120, 80), (uint)width, (uint)height);
        image.Quality = 85;
        return image.ToByteArray(jpeg ? MagickFormat.Jpeg : MagickFormat.Png);
    }
}
