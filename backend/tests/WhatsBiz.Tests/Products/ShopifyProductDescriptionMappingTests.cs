using FluentAssertions;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyProductDescriptionMappingTests
{
    [Fact]
    public void NewShopifyProductStoresBodyHtmlOnlyAsLongDescription()
    {
        var product = new Product();

        ShopifyProductDescriptionMapping.ApplyToNewProduct(product, "<h2>Details</h2><p>Made for daily use.</p>");

        product.ShortDescription.Should().BeNull();
        product.LongDescription.Should().Be("<h2>Details</h2><p>Made for daily use.</p>");
    }

    [Fact]
    public void SupplierRefreshPreservesRetailerShortDescription()
    {
        var product = new Product { ShortDescription = "Retailer-authored summary", LongDescription = "Previous supplier content" };

        ShopifyProductDescriptionMapping.ApplyToExistingProduct(product, "<p>Updated supplier content</p>", supplierOwnsDescription: true);

        product.ShortDescription.Should().Be("Retailer-authored summary");
        product.LongDescription.Should().Be("<p>Updated supplier content</p>");
    }

    [Fact]
    public void SupplierRefreshPreservesBothDescriptionsWhenRetailerOwnsThem()
    {
        var product = new Product { ShortDescription = "Retailer summary", LongDescription = "Retailer detailed description" };

        ShopifyProductDescriptionMapping.ApplyToExistingProduct(product, "<p>Supplier content</p>", supplierOwnsDescription: false);

        product.ShortDescription.Should().Be("Retailer summary");
        product.LongDescription.Should().Be("Retailer detailed description");
    }
}
