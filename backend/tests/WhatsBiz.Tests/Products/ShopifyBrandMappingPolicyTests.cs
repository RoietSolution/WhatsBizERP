using FluentAssertions;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyBrandMappingPolicyTests
{
    [Fact]
    public void MissingVendorBrandIsMarkedForCreationWithoutMutatingTenantBrands()
    {
        var brands = new List<Brand>();

        var result = ShopifyBrandMappingPolicy.Resolve("  North   Star  ", brands);

        result.Status.Should().Be("WILL_CREATE");
        result.NormalizedVendor.Should().Be("North Star");
        result.Message.Should().Be("Brand will be created: North Star");
        brands.Should().BeEmpty("preview must not create master data");
    }

    [Fact]
    public void ExistingTenantBrandIsReusedCaseInsensitively()
    {
        var brand = ActiveBrand("Acme Audio");

        var result = ShopifyBrandMappingPolicy.Resolve("  ACME   AUDIO ", [brand]);

        result.Status.Should().Be("EXISTING");
        result.Brand.Should().BeSameAs(brand);
        result.Message.Should().Be("Existing brand matched");
    }

    [Fact]
    public void TwoProductsSharingVendorResolveToTheSameBrandAfterFirstCreation()
    {
        var brands = new List<Brand>();
        var first = ShopifyBrandMappingPolicy.Resolve("Acme", brands);
        var created = ActiveBrand(first.NormalizedVendor!);
        brands.Add(created);

        var second = ShopifyBrandMappingPolicy.Resolve(" ACME ", brands);

        first.Status.Should().Be("WILL_CREATE");
        second.Status.Should().Be("EXISTING");
        second.Brand.Should().BeSameAs(created);
    }

    [Fact]
    public void ReimportFindsThePreviouslyCreatedTenantBrandInsteadOfCreatingAnother()
    {
        var existing = ActiveBrand("North Star");

        var repeatedImport = ShopifyBrandMappingPolicy.Resolve("North Star", [existing]);

        repeatedImport.Status.Should().Be("EXISTING");
        repeatedImport.Brand!.BrandId.Should().Be(existing.BrandId);
    }

    [Fact]
    public void InaccessibleOtherTenantBrandIsNotReused()
    {
        var otherTenantPrivateBrand = ActiveBrand("Private Supplier Brand");
        IReadOnlyCollection<Brand> importingTenantBrands = [];

        var result = ShopifyBrandMappingPolicy.Resolve(otherTenantPrivateBrand.BrandName, importingTenantBrands);

        result.Status.Should().Be("WILL_CREATE");
        result.Brand.Should().BeNull();
    }

    [Theory]
    [InlineData("", "MISSING")]
    [InlineData("   ", "MISSING")]
    [InlineData("Invalid\u0000Vendor", "INVALID")]
    [InlineData("A name longer than the brand limit", "WILL_CREATE")]
    public void VendorValuesProduceClearResolutionStates(string vendor, string expected)
    {
        ShopifyBrandMappingPolicy.Resolve(vendor, []).Status.Should().Be(expected);
    }

    [Fact]
    public void InactiveTenantBrandIsRejectedInsteadOfReactivatedOrDuplicated()
    {
        var brand = ActiveBrand("Inactive Vendor");
        brand.IsActive = false;

        var result = ShopifyBrandMappingPolicy.Resolve("Inactive Vendor", [brand]);

        result.Status.Should().Be("INACTIVE");
        result.Message.Should().Contain("exists for this tenant but is inactive");
        result.Brand.Should().BeNull();
    }

    [Fact]
    public void VendorLongerThanBrandSchemaLimitIsRejected()
    {
        var result = ShopifyBrandMappingPolicy.Resolve(new string('V', 201), []);

        result.Status.Should().Be("INVALID");
        result.Message.Should().Be("Shopify Vendor must be 200 characters or fewer.");
    }

    [Fact]
    public void MultipleTenantMatchesAreRejectedAsAmbiguous()
    {
        var result = ShopifyBrandMappingPolicy.Resolve("Acme", [ActiveBrand("ACME"), ActiveBrand(" Acme ")]);

        result.Status.Should().Be("AMBIGUOUS");
        result.Message.Should().Contain("duplicate brands");
    }

    [Fact]
    public void DefaultBrandIsUsedOnlyWhenVendorIsBlankAndItIsActive()
    {
        var defaultBrand = ActiveBrand("Default brand");

        ShopifyBrandMappingPolicy.Resolve("  ", [defaultBrand], defaultBrand)
            .Should().Match<ShopifyBrandMapping>(x => x.Status == "DEFAULT" && x.Brand == defaultBrand);

        defaultBrand.IsActive = false;
        ShopifyBrandMappingPolicy.Resolve("  ", [defaultBrand], defaultBrand).Status.Should().Be("INACTIVE");
    }

    private static Brand ActiveBrand(string name) => new()
    {
        BrandCode = "BR-" + Guid.NewGuid().ToString("N"),
        BrandName = name,
        IsActive = true
    };
}
