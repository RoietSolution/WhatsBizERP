using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyCatalogImportPricingTests
{
    [Fact]
    public void MapsSupplierCostShippingRetailerPricesVendorAndCompareAtIndependently()
    {
        const string csv = "Title,Variant SKU,Cost per item,Variant Price,ERP Selling Price,ERP MRP,Variant Compare At Price,Vendor\n" +
            "Item,SKU-1,210,35,400,500,900,Brand supplied by retailer";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "retailer.csv").Single();

        row.ProductCost.Should().Be(210m);
        row.CombinedShippingPackagingCost.Should().Be(35m);
        ShopifyCatalogPricingPolicy.CalculateErpCostPrice(row).Should().Be(245m);
        row.SellingPrice.Should().Be(400m);
        row.Mrp.Should().Be(500m);
        row.CompareAtPrice.Should().Be(900m);
        row.BrandName.Should().Be("Brand supplied by retailer");
        ShopifyCatalogPricingPolicy.GstPricingType.Should().Be("INCLUSIVE");
        ShopifyCatalogPricingPolicy.Validate(row).Should().BeEmpty();

        var product = new Product { PurchasePrice = row.ProductCost!.Value, CombinedShippingPackagingCost = row.CombinedShippingPackagingCost!.Value };
        ShopifyCatalogPricingPolicy.ApplyRetailerPricing(product, row, 5m);
        product.EstimatedAcquisitionCost.Should().Be(245m);
        product.SellingPrice.Should().Be(400m);
        product.MRP.Should().Be(500m);
        product.GSTPercentage.Should().Be(5m);
        product.GstPricingType.Should().Be("INCLUSIVE");
    }

    [Fact]
    public void MissingRetailerPricesAreExplicitAndCompareAtDoesNotFillMrp()
    {
        const string csv = "Title,Cost per item,Variant Price,Variant Compare At Price\nItem,100,25,300";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "retailer.csv").Single();

        row.SellingPrice.Should().BeNull();
        row.Mrp.Should().BeNull();
        row.CompareAtPrice.Should().Be(300m);
        row.MissingFields.Should().Contain("ERP Selling Price").And.Contain("ERP MRP");
        ShopifyCatalogPricingPolicy.Validate(row).Should().Contain(error => error.Contains("ERP Selling Price"))
            .And.Contain(error => error.Contains("ERP MRP"));
    }

    [Fact]
    public void ExplicitGstWinsAndZeroIsRetainedAsAValidWorkbookValue()
    {
        const string csv = "Title,Tags,GST,Status\nProduct,Kitchen,0,active";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv", new Dictionary<string, string> { ["gst"] = "GST" }).Single();

        ShopifyCatalogPricingPolicy.ResolveGstPercentage(row, 5m).Should().Be(0m);
        ShopifyCatalogPricingPolicy.GstSource(row, 5m).Should().Be("Workbook");
    }

    [Fact]
    public void ConfiguredZeroGstDefaultIsResolvedAndReportedWithoutTreatingZeroAsMissing()
    {
        const string csv = "Title,Tags,Status\nProduct,Kitchen,active";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        var previewGst = ShopifyCatalogPricingPolicy.ResolveGstPercentage(row, 0m);
        previewGst.Should().Be(0m);
        ShopifyCatalogPricingPolicy.GstSource(row, 0m).Should().Be("Default");
        var product = new Product();
        ShopifyCatalogPricingPolicy.ApplyRetailerPricing(product, row with { SellingPrice = 10m, Mrp = 10m }, previewGst!.Value);
        product.GSTPercentage.Should().Be(0m);
        product.GstPricingType.Should().Be("INCLUSIVE");
    }

    [Fact]
    public void MissingGstWithoutConfiguredDefaultRemainsMissing()
    {
        const string csv = "Title,Tags,Status\nProduct,Kitchen,active";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        ShopifyCatalogPricingPolicy.ResolveGstPercentage(row, null).Should().BeNull();
        ShopifyCatalogPricingPolicy.GstSource(row, null).Should().Be("Missing");
    }

    [Fact]
    public void ShopifyTemplateRetainsAllSourceColumnsAndAddsEditableRetailerPrices()
    {
        var expected = new[]
        {
            "Handle", "Title", "Body (HTML)", "Vendor", "Standardized Product Type", "Custom Product Type", "Tags", "Published",
            "Option1 Name", "Option1 Value", "Option2 Name", "Option2 Value", "Option3 Name", "Option3 Value", "Variant SKU",
            "Variant Grams", "Variant Inventory Tracker", "Variant Inventory Qty", "Variant Inventory Policy", "Variant Fulfillment Service",
            "Variant Price", "Variant Compare At Price", "Variant Requires Shipping", "Variant Taxable", "Variant Barcode", "Image Src",
            "Image Position", "Image Alt Text", "Gift Card", "SEO Title", "SEO Description", "Google Shopping / Google Product Category",
            "Google Shopping / Gender", "Google Shopping / Age Group", "Google Shopping / MPN", "Google Shopping / AdWords Grouping",
            "Google Shopping / AdWords Labels", "Google Shopping / Condition", "Google Shopping / Custom Product", "Google Shopping / Custom Label 0",
            "Google Shopping / Custom Label 1", "Google Shopping / Custom Label 2", "Google Shopping / Custom Label 3", "Google Shopping / Custom Label 4",
            "Variant Image", "Variant Weight Unit", "Variant Tax Code", "Cost per item", "Status", "ERP Selling Price", "ERP MRP"
        };
        ShopifyCatalogImportTemplate.Headers.Should().Equal(expected);

        using var workbook = new XLWorkbook(new MemoryStream(ShopifyCatalogImportTemplate.Create()));
        var uploadedHeaders = workbook.Worksheet(1).Row(1).Cells(1, expected.Length).Select(cell => cell.GetString());
        uploadedHeaders.Should().Equal(expected);
    }
}
