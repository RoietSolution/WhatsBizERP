using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Domain.Products;
using Xunit;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyRetailerImportSnapshotTests
{
    [Fact]
    public void MappingSnapshotIsStableAndChangesWhenFieldSelectionChanges()
    {
        var first = Selections();
        var same = Selections();
        var changed = Selections();
        changed.FieldMappings!["sellingPrice"] = "Retail Price";

        var firstHash = ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", first);

        ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", same).Should().Be(firstHash);
        ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", changed).Should().NotBe(firstHash);
        ShopifyRetailerProductImportController.SnapshotHash("Store B", "file-hash", first).Should().NotBe(firstHash);
        ShopifyRetailerProductImportController.SnapshotHash("Store A", "other-file", first).Should().NotBe(firstHash);

        var costUpdates = Selections() with { UpdatePackagingCost = true };
        ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", costUpdates).Should().NotBe(firstHash);
        var customCostHeader = Selections();
        customCostHeader.FieldMappings!["shippingCost"] = "Shipping Cost2";
        ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", customCostHeader).Should().NotBe(firstHash);
        var inclusiveCosts = Selections() with { SupplierCostTreatment = "INCLUSIVE" };
        ShopifyRetailerProductImportController.SnapshotHash("Store A", "file-hash", inclusiveCosts).Should().NotBe(firstHash);
    }

    [Fact]
    public void CommitRequiresTheMatchingPreviewSnapshot()
    {
        ShopifyRetailerProductImportController.MatchesPreviewSnapshot(null, "snapshot").Should().BeFalse();
        ShopifyRetailerProductImportController.MatchesPreviewSnapshot("", "snapshot").Should().BeFalse();
        ShopifyRetailerProductImportController.MatchesPreviewSnapshot("stale", "snapshot").Should().BeFalse();
        ShopifyRetailerProductImportController.MatchesPreviewSnapshot("SNAPSHOT", "snapshot").Should().BeTrue();
    }

    [Fact]
    public void DefaultMappingsKeepV92RetailPricesAndUseDedicatedSupplierCostColumns()
    {
        var mappings = ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings();
        mappings["sellingPrice"].Should().Be("Variant Price");
        mappings["mrp"].Should().Be("Variant Compare At Price");
        mappings["productCost"].Should().Be("Supplier Product Cost");
        mappings["packagingCost"].Should().Be("Packaging Cost");
        mappings["shippingCost"].Should().Be("Shipping Cost");
    }

    [Fact]
    public void SupplierCostEstimateRequiresAllThreeDistinctAmounts()
    {
        ShopifyRetailerProductImportController.EstimateSupplierTotal(100m, 5m, 8m).Should().Be(113m);
        ShopifyRetailerProductImportController.EstimateSupplierTotal(100m, null, 8m).Should().BeNull();
        ShopifyRetailerProductImportController.EstimateSupplierTotal(100m, 0m, 8m).Should().Be(108m);
        ShopifyRetailerProductImportController.EstimateSupplierTotal(328m, 30m, 60m, "INCLUSIVE").Should().Be(328m);
        ShopifyRetailerProductImportController.EstimateSupplierTotal(328m, 30m, 60m, "EXCLUSIVE").Should().Be(418m);
    }

    [Fact]
    public void SupplierCostsAboveSellingPriceAreWarningsAndProfitMarginAreCalculated()
    {
        ShopifyRetailerProductImportController.SupplierPricingWarnings(100m, 110m, 5m, 3m)
            .Should().ContainSingle("Total supplier cost exceeds the ERP Selling Price.");
        ShopifyRetailerProductImportController.SupplierPricingWarnings(100m, 90m, 8m, 5m)
            .Should().ContainSingle().Which.Should().Be("Total supplier cost exceeds the ERP Selling Price.");
        ShopifyRetailerProductImportController.SupplierPricingWarnings(100m, 90m, null, 5m).Should().BeEmpty();

        var total = ShopifyRetailerProductImportController.EstimateSupplierTotal(110m, 5m, 3m);
        var profitLoss = ShopifyRetailerProductImportController.EstimateProfitLoss(100m, total);
        profitLoss.Should().Be(-18m);
        ShopifyRetailerProductImportController.EstimateMarginPercentage(100m, profitLoss).Should().Be(-18m);
        ShopifyRetailerProductImportController.EstimateProfitLoss(100m, null).Should().BeNull();
        ShopifyRetailerProductImportController.EstimateMarginPercentage(0m, 10m).Should().BeNull();
    }

    [Fact]
    public void GuturGoDefaultsToInclusiveSupplierCostAndNeverUsesVariantPriceForPricing()
    {
        var valid = Selections() with { SupplierCostTreatment = null };
        ShopifyRetailerProductImportController.TryResolveSupplierCostTreatment("GuturGo", valid, out var guturGo, out var error)
            .Should().BeTrue(error);
        guturGo.SupplierCostTreatment.Should().Be("INCLUSIVE");
        guturGo.FieldMappings!["sellingPrice"].Should().Be("ERP Selling Price");
        guturGo.FieldMappings["productCost"].Should().Be("Supplier Product Cost");
        ShopifyRetailerProductImportController.TryResolveSupplierCostTreatment("guturgo.myshopify.com", valid, out var domain, out error)
            .Should().BeTrue(error);
        domain.SupplierCostTreatment.Should().Be("INCLUSIVE");

        ShopifyRetailerProductImportController.TryResolveSupplierCostTreatment("Fashion Supplier", valid, out var other, out error)
            .Should().BeTrue(error);
        other.SupplierCostTreatment.Should().Be("EXCLUSIVE");

        var legacyMapping = Selections();
        legacyMapping.FieldMappings!["productCost"] = "Variant Price";
        ShopifyRetailerProductImportController.TryResolveSupplierCostTreatment("GuturGo", legacyMapping, out var corrected, out error)
            .Should().BeTrue(error);
        corrected.FieldMappings!["productCost"].Should().Be("Supplier Product Cost");
    }

    [Fact]
    public void InclusivePurchasePriceDoesNotDoubleCountSeparateExpenseBreakdown()
    {
        ShopifyRetailerProductImportController.CombinedShippingPackagingCostForTreatment("INCLUSIVE").Should().Be(0m);
        ShopifyRetailerProductImportController.CombinedShippingPackagingCostForTreatment("EXCLUSIVE").Should().BeNull();
        new Product { PurchasePrice = 328m, PackagingCost = 30m, ShippingCost = 60m,
            CombinedShippingPackagingCost = ShopifyRetailerProductImportController.CombinedShippingPackagingCostForTreatment("INCLUSIVE") }
            .EstimatedAcquisitionCost.Should().Be(328m);
        new Product { PurchasePrice = 328m, PackagingCost = 30m, ShippingCost = 60m,
            CombinedShippingPackagingCost = ShopifyRetailerProductImportController.CombinedShippingPackagingCostForTreatment("EXCLUSIVE") }
            .EstimatedAcquisitionCost.Should().Be(418m);
        ShopifyRetailerProductImportController.SupplierPricingWarnings(459m, 328m, 30m, 60m, "INCLUSIVE").Should().BeEmpty();
    }

    [Fact]
    public void SupplierCostUpdatesRequireOptInAndBlankValuesPreserveExistingCosts()
    {
        ShopifyRetailerProductImportController.SelectOptionalCost(12m, 8m, false, false).Should().Be(8m);
        ShopifyRetailerProductImportController.SelectOptionalCost(12m, 8m, false, true).Should().Be(12m);
        ShopifyRetailerProductImportController.SelectOptionalCost(0m, 8m, false, true).Should().Be(0m);
        ShopifyRetailerProductImportController.SelectOptionalCost(null, 8m, false, true).Should().Be(8m);
        ShopifyRetailerProductImportController.SelectOptionalCost(12m, null, true, false).Should().Be(12m);
    }

    private static ShopifyRetailerProductImportController.ImportSelections Selections() => new(
        CategoryByType: new Dictionary<string, Guid>(StringComparer.Ordinal),
        BrandByVendor: new Dictionary<string, Guid>(StringComparer.Ordinal),
        FieldMappings: ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings());
}
