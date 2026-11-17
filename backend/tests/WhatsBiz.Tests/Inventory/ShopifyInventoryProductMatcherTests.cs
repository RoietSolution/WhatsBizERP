using FluentAssertions;
using WhatsBiz.Api.Controllers;

namespace WhatsBiz.Tests.Inventory;

public sealed class ShopifyInventoryProductMatcherTests
{
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void ExactHandleAndSkuWinsWhenSupplierSkuIsShared()
    {
        var result = ShopifyInventoryProductMatcher.Resolve("handle-b", "SHARED",
            [new("handle-a", "SHARED", ProductA), new("handle-b", "SHARED", ProductB)],
            [new(ProductA, "SHARED"), new(ProductB, "SHP-INTERNAL-B")]);

        result.Should().Be(new ShopifyInventoryProductMatch("MATCHED", ProductB));
    }

    [Fact]
    public void SkuOnlyMatchRejectsMultipleShopifyProductsWithSharedSku()
    {
        var result = ShopifyInventoryProductMatcher.Resolve(null, "SHARED",
            [new("handle-a", "SHARED", ProductA), new("handle-b", "SHARED", ProductB)],
            [new(ProductA, "SHP-INTERNAL-A"), new(ProductB, "SHP-INTERNAL-B")]);

        result.Status.Should().Be("AMBIGUOUS");
        result.ProductId.Should().BeNull();
    }

    [Fact]
    public void UniqueSkuOnlyMatchRemainsSupported()
    {
        var result = ShopifyInventoryProductMatcher.Resolve(null, "SKU-UNIQUE",
            [new("handle-a", "SKU-UNIQUE", ProductA)],
            [new(ProductA, "INTERNAL-A")]);

        result.Should().Be(new ShopifyInventoryProductMatch("MATCHED", ProductA));
    }

    [Fact]
    public void ExactIdentityDoesNotFallThroughWhenItsProductIsUnavailable()
    {
        var result = ShopifyInventoryProductMatcher.Resolve("handle-a", "SKU-A",
            [new("handle-a", "SKU-A", ProductA), new("handle-b", "SKU-A", ProductB)],
            [new(ProductB, "INTERNAL-B")]);

        result.Status.Should().Be("UNAVAILABLE");
    }

    [Fact]
    public void InternalProductCodesAreUniquePerTenantSourceHandleAndSkuAndFitErpLimit()
    {
        var a = ShopifyImportIdentityRules.CreateInternalProductCode(ProductA, "shop", "handle-a", "SHARED");
        var b = ShopifyImportIdentityRules.CreateInternalProductCode(ProductA, "shop", "handle-b", "SHARED");
        var otherTenant = ShopifyImportIdentityRules.CreateInternalProductCode(ProductB, "shop", "handle-a", "SHARED");

        a.Should().HaveLength(50);
        a.Should().NotBe(b).And.NotBe(otherTenant);
        ShopifyImportIdentityRules.CreateInternalProductCode(ProductA, "shop", "handle-a", " SHARED ").Should().Be(a);
    }

    [Fact]
    public void ExistingAndIncomingHandlesIdentifyASharedSupplierSku()
    {
        ShopifyImportIdentityRules.FindSharedSupplierSkus(
            [("handle-a", (string?)"SHARED"), ("handle-b", "SHARED")])
            .Should().ContainSingle().Which.Should().Be("SHARED");
    }
}
