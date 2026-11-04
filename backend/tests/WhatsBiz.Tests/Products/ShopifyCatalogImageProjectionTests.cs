using FluentAssertions;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyCatalogImageProjectionTests
{
    [Fact]
    public void ProjectsShopifyPositionIntoGalleryOrderAndPositionOneAsPrimary()
    {
        var tenant = Guid.NewGuid();
        var product = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        var images = new[]
        {
            new ShopifyCatalogImage("https://cdn.example.com/third.jpg", 3, false),
            new ShopifyCatalogImage("https://cdn.example.com/primary.jpg", 1, false),
            new ShopifyCatalogImage("https://cdn.example.com/primary.jpg", 2, false),
            new ShopifyCatalogImage("http://unsafe.example.test/image.jpg", 0, true)
        };

        var projected = ShopifyCatalogImageProjection.ToExternalImages(tenant, product, supplier, images).ToArray();

        projected.Should().HaveCount(2);
        projected.Select(image => image.DisplayOrder).Should().Equal(1, 3);
        projected.Single(image => image.IsPrimary).ImageUrl.Should().Be("https://cdn.example.com/primary.jpg");
        projected.Should().OnlyContain(image => image.TenantId == tenant && image.ProductId == product && image.SupplierId == supplier);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    public void ProjectsEveryDistinctExternalImageWithoutManagedUploadLimit(int count)
    {
        var images = Enumerable.Range(1, count)
            .Select(position => new ShopifyCatalogImage($"https://cdn.shopify.com/product/{position}.jpg", position, position == 1))
            .ToArray();

        var projected = ShopifyCatalogImageProjection.ToExternalImages(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), images);

        projected.Should().HaveCount(count);
        projected.Select(image => image.DisplayOrder).Should().Equal(Enumerable.Range(1, count));
        projected.Single(image => image.IsPrimary).DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void KeepsPositionCaseSensitivePathsAndDeterministicallyChoosesPrimaryOnDuplicatePositions()
    {
        var images = new[]
        {
            new ShopifyCatalogImage("https://cdn.shopify.com/product/A.jpg", 2, false),
            new ShopifyCatalogImage("https://cdn.shopify.com/product/a.jpg", 2, false),
            new ShopifyCatalogImage("https://cdn.shopify.com/product/a.jpg", 3, false)
        };

        var projected = ShopifyCatalogImageProjection.ToExternalImages(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), images).ToArray();

        projected.Should().HaveCount(2);
        projected.Select(image => image.ImageUrl).Should().Equal("https://cdn.shopify.com/product/A.jpg", "https://cdn.shopify.com/product/a.jpg");
        projected.Single(image => image.IsPrimary).ImageUrl.Should().Be("https://cdn.shopify.com/product/A.jpg");
    }

    [Fact]
    public void EmptyImageRefreshDoesNotReplaceExistingSupplierGallery()
    {
        ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(true, []).Should().BeFalse();
        ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(false, [new("https://cdn.shopify.com/product/1.jpg", 1, true)]).Should().BeFalse();
        ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(true, [new("https://cdn.shopify.com/product/1.jpg", 1, true)]).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://localhost/image.jpg")]
    [InlineData("https://127.0.0.1/image.jpg")]
    [InlineData("https://10.0.0.4/image.jpg")]
    [InlineData("https://192.168.1.2/image.jpg")]
    [InlineData("https://cdn.shopify.com:8443/image.jpg")]
    [InlineData("https://user:pass@cdn.shopify.com/image.jpg")]
    public void RejectsLocalOrUnsafeExternalImageAddresses(string url)
    {
        ShopifyCatalogImportParser.SafeHttps(url).Should().BeNull();
    }
}
