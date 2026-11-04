using FluentAssertions;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class PublicMarketplacePolicyTests
{
    [Theory]
    [InlineData(" AoneSer ", "aoneser")]
    [InlineData("varanasi", "varanasi")]
    [InlineData("fresh-food-2", "fresh-food-2")]
    public void PublicSlugsNormalizeCaseAndWhitespace(string input, string expected)
        => PublicSlugPolicy.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("cart")]
    [InlineData("products")]
    [InlineData("ref")]
    [InlineData("api")]
    [InlineData("/kanpur")]
    [InlineData("https://shop.khatadhari.com/mirganj")]
    [InlineData("-city")]
    [InlineData("city-")]
    public void PublicSlugsRejectReservedOrInvalidRoutes(string input)
        => FluentActions.Invoking(() => PublicSlugPolicy.Normalize(input)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("ARJN_GAR", "arjn_gar")]
    [InlineData("QA_DEFAULT", "qa_default")]
    [InlineData("RADHA_MED", "radha_med")]
    [InlineData("TENANT_4", "tenant_4")]
    public void RetailerRouteAliasesPreserveUnderscoresAndNormalizeCase(string input, string expected)
        => PublicSlugPolicy.NormalizeRouteSegment(input).Should().Be(expected);

    [Theory]
    [InlineData("cart")]
    [InlineData("_city")]
    [InlineData("city_")]
    [InlineData("/kanpur")]
    public void RetailerRouteAliasesRejectReservedOrInvalidSegments(string input)
        => FluentActions.Invoking(() => PublicSlugPolicy.NormalizeRouteSegment(input)).Should().Throw<ArgumentException>();

    [Fact]
    public void CitySlugPolicyRemainsStrictWhenRetailerAliasesAllowUnderscores()
        => FluentActions.Invoking(() => PublicSlugPolicy.Normalize("new_city")).Should().Throw<ArgumentException>();

    [Fact]
    public void CityCategoryMediaMigrationAddsOnlyKeyedCityDisplayMediaAndKeepsV75ValidationCompatible()
    {
        var root = FindRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V76-CityCategoryDisplayMedia.sql"));
        migration.Should().Contain("commerce.CityMarketplaceMedia")
            .And.Contain("ResourceKey nvarchar(160) NULL")
            .And.Contain("ResourceType='CATEGORY' AND ResourceKey IS NOT NULL")
            .And.Contain("UX_CityMarketplaceMedia_CityResourceKey")
            .And.Contain("UQ_CityMarketplaceMedia_CityResource")
            .And.Contain("BEGIN TRANSACTION").And.Contain("COMMIT TRANSACTION");
        var addColumnIndex = migration.IndexOf("ADD ResourceKey nvarchar(160) NULL", StringComparison.Ordinal);
        var batchSeparatorIndex = migration.IndexOf("\nGO", addColumnIndex, StringComparison.Ordinal);
        var dependentSqlIndex = migration.IndexOf("DROP CONSTRAINT CK_CityMarketplaceMedia_Resource", StringComparison.Ordinal);
        addColumnIndex.Should().BeGreaterThanOrEqualTo(0);
        batchSeparatorIndex.Should().BeGreaterThan(addColumnIndex);
        dependentSqlIndex.Should().BeGreaterThan(batchSeparatorIndex);
        migration.Should().NotContain("UPDATE commerce.Cities").And.NotContain("UPDATE core.Tenants");

        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V76-CityCategoryDisplayMedia.validate.sql"));
        validator.Should().Contain("UX_CityMarketplaceMedia_CityResourceKey")
            .And.Contain("UQ_CityMarketplaceMedia_CityResource")
            .And.Contain("is_not_trusted=0")
            .And.Contain("ResourceType='CATEGORY'");
    }

    [Fact]
    public void CityBannerCopyMigrationAddsOnlyOptionalBoundedCopyFields()
    {
        var root = FindRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V77-CityMarketplaceBannerCopy.sql"));
        migration.Should().Contain("commerce.Cities")
            .And.Contain("BannerHeadline nvarchar(120) NULL")
            .And.Contain("BannerSubtitle nvarchar(240) NULL")
            .And.Contain("COL_LENGTH")
            .And.Contain("BEGIN TRANSACTION").And.Contain("COMMIT TRANSACTION")
            .And.NotContain("UPDATE commerce.Cities");
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V77-CityMarketplaceBannerCopy.validate.sql"));
        validator.Should().Contain("BannerHeadline").And.Contain("c.max_length=240")
            .And.Contain("BannerSubtitle").And.Contain("c.max_length=480")
            .And.Contain("is_nullable=1");

        var service = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/PublicMarketplaceService.cs"));
        service.Should().Contain("c.BannerHeadline").And.Contain("c.BannerSubtitle")
            .And.Contain("input.BannerHeadline").And.Contain("input.BannerSubtitle");
    }

    [Fact]
    public void CityCategoriesAreDerivedFromEligibleTenantScopedProductsAndAggregateCountsByName()
    {
        var root = FindRoot();
        var service = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/PublicMarketplaceService.cs"));
        service.Should().Contain("var facetQuery=query")
            .And.Contain("CategoryName=EF.Functions.Collate(x.CategoryName.Trim(),\"Latin1_General_100_CI_AS\")")
            .And.Contain("ProductCount=group.Count()")
            .And.Contain("GroupBy(x=>x.Key,StringComparer.Ordinal)")
            .And.NotContain("Take(500)");
        service.Should().Contain("mapping.IsActive").And.Contain("directory.OptedIn && directory.OwnerApproved")
            .And.Contain("FeatureKeys.CityMarketplace").And.Contain("IsProductPubliclyApproved")
            .And.Contain("state?.CanOrderNow").And.Contain("balance.QuantityOnHand>balance.QuantityReserved");
        service.Should().NotContain("CityCategoryMaster").And.NotContain("CityCategoryProducts");
    }

    [Fact]
    public void V74MigrationPreservesRetailerKeysAndFailsOnCollisionsInsteadOfRenaming()
    {
        var root = FindRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V74-CityMarketplaceAndPartnerSellers.sql"));
        migration.Should().Contain("Existing retailer store keys collide")
            .And.Contain("conflicts with a reserved application route")
            .And.Contain("SELECT t.TenantKey,'RETAILER'")
            .And.Contain("UNIQUE INDEX UX_PublicRouteRegistry_NormalizedSlug")
            .And.Contain("REFERENCES core.Tenants(TenantId)")
            .And.Contain("EntityType='CITY' AND OriginalSlug NOT LIKE '%[^A-Za-z0-9-]%'")
            .And.Contain("EntityType='RETAILER' AND OriginalSlug NOT LIKE '%[^A-Za-z0-9_-]%'")
            .And.Contain("BEGIN TRANSACTION").And.Contain("COMMIT TRANSACTION");
        migration.Should().NotContain("UPDATE core.Tenants SET TenantKey").And.NotContain("REFERENCES master.Tenants");
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V74-CityMarketplaceAndPartnerSellers.validate.sql"));
        validator.Should().Contain("referenced_object_id=OBJECT_ID(N'core.Tenants')").And.Contain("canonical core.Tenants").And.Contain("strict letters, digits and interior-hyphens").And.Contain("CK_Cities_Name").And.NotContain("CK_Cities_NameState");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "database/migrations/V74-CityMarketplaceAndPartnerSellers.sql"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
