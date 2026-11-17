using FluentAssertions;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontProductImageProjectionTests
{
    [Fact]
    public void ProjectsAllShopifyImagesInPositionOrderBeforeManagedImagesAndUsesPositionOneAsPrimary()
    {
        var productId = Guid.NewGuid();
        var external = Enumerable.Range(1, 25).Reverse().Select(position => new ExternalStorefrontProductImage(
            Guid.NewGuid(), $"https://cdn.shopify.com/products/{position}.jpg", position, position == 2)).ToArray();
        var managed = new[] { new ManagedStorefrontProductImage(Guid.NewGuid(), true) };

        var gallery = StorefrontProductImageProjection.Project("smoke-store", productId, external, managed);

        gallery.PrimaryUrl.Should().Be("https://cdn.shopify.com/products/1.jpg");
        gallery.Images.Should().HaveCount(26);
        gallery.Images.First().Url.Should().Be(gallery.PrimaryUrl);
        gallery.Images.First().IsPrimary.Should().BeTrue();
        gallery.Images.Take(25).Select(image => image.Url).Should().Equal(Enumerable.Range(1, 25).Select(position => $"https://cdn.shopify.com/products/{position}.jpg"));
        gallery.Images.Last().Url.Should().Contain($"/api/store/smoke-store/products/{productId}/image");
        gallery.Images.Last().IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void FiltersUnsafeExternalImagesAndFallsBackToManagedPrimary()
    {
        var managed = new[] { new ManagedStorefrontProductImage(Guid.NewGuid(), true) };
        var external = new[] { new ExternalStorefrontProductImage(Guid.NewGuid(), "https://127.0.0.1/internal.jpg", 1, true) };

        var gallery = StorefrontProductImageProjection.Project("smoke-store", Guid.NewGuid(), external, managed);

        gallery.PrimaryUrl.Should().Contain("/api/store/");
        gallery.Images.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void PreservesManagedImageDimensionsForResponsiveSrcsetGeneration()
    {
        var managedId = Guid.NewGuid();
        var gallery = StorefrontProductImageProjection.Project("smoke-store", Guid.NewGuid(), [],
            [new ManagedStorefrontProductImage(managedId, true, 1000, 1200, 250, 300)]);

        var primary = gallery.Images.Should().ContainSingle().Which;
        primary.Width.Should().Be(1000);
        primary.Height.Should().Be(1200);
        primary.ThumbnailWidth.Should().Be(250);
        primary.ThumbnailHeight.Should().Be(300);
    }
}
