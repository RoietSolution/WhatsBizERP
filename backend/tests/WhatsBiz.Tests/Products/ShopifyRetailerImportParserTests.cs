using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Infrastructure.Suppliers;
using Xunit;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyRetailerImportParserTests
{
    [Fact]
    public void LegacyExportGroupsImageRowsAndMapsVariantPriceAndMrp()
    {
        const string csv = "Handle,Title,Body (HTML),Type,Vendor,Variant SKU,Variant Barcode,Variant Price,Variant Compare At Price,Image Src,Image Position,Image Alt Text,Status,Published\n" +
            "silk-saree,Silk Saree,<p>Silk</p>,Sarees,Vendor A,SKU-1,BC-1,359,400,https://cdn.shopify.com/a.jpg,2,Border,active,true\n" +
            "silk-saree,,,,,,,,,https://cdn.shopify.com/b.jpg,1,Full view,active,true\n";

        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv").Should().ContainSingle().Subject;

        row.Handle.Should().Be("silk-saree"); row.Sku.Should().Be("SKU-1"); row.SellingPrice.Should().Be(359); row.Mrp.Should().Be(400);
        row.Images.Select(x => x.Position).Should().Equal(1, 2);
        row.Images.OrderBy(x => x.Position).First().AltText.Should().Be("Full view");
        row.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitMappingsKeepSellingPriceAndMrpDefaultsAndIgnorePublicationAndInventory()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Variant Compare At Price,Variant Inventory Qty,Status,Published,Image Src,Image Alt Text\n" +
            "mapped-product,Product,SKU-9,25.50,40,999,archived,false,https://cdn.shopify.com/item.jpg,Front view\n";
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["handle"] = "Handle", ["title"] = "Title", ["sku"] = "Variant SKU",
            ["sellingPrice"] = "Variant Price", ["mrp"] = "Variant Compare At Price",
            ["image"] = "Image Src", ["imageAlt"] = "Image Alt Text",
            ["status"] = "", ["productCost"] = "", ["gstPercentage"] = "", ["weight"] = "", ["weightUnit"] = ""
        };

        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv", mappings).Should().ContainSingle().Subject;

        row.SellingPrice.Should().Be(25.50m);
        row.Mrp.Should().Be(40m);
        row.ProductCost.Should().BeNull();
        row.Status.Should().BeNull();
        row.Images.Should().ContainSingle().Which.AltText.Should().Be("Front view");
        row.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitOptionalMappingCanSelectCostWithoutUsingSellingPrice()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Cost per item\nproduct,Product,SKU,25,10\n";
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["handle"] = "Handle", ["title"] = "Title", ["sku"] = "Variant SKU",
            ["sellingPrice"] = "Variant Price", ["productCost"] = "Cost per item",
            ["mrp"] = "", ["description"] = "", ["category"] = "", ["brand"] = "", ["barcode"] = "",
            ["image"] = "", ["imagePosition"] = "", ["imageAlt"] = "", ["status"] = "",
            ["gstPercentage"] = "", ["weight"] = "", ["weightUnit"] = ""
        };

        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv", mappings).Should().ContainSingle().Subject;
        row.SellingPrice.Should().Be(25m);
        row.ProductCost.Should().Be(10m);
    }

    [Fact]
    public void ExplicitCustomHeaderMappingsKeepSupplierCostsSeparateFromRetailPrices()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Variant Compare At Price2,Wholesale Price,Packaging Charges,Shipping Cost2\nproduct,Product,SKU,25,40,11.25,2,3.50\n";
        var mappings = Mappings(("mrp", "Variant Compare At Price2"), ("productCost", "Wholesale Price"),
            ("packagingCost", "Packaging Charges"), ("shippingCost", "Shipping Cost2"));

        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv", mappings).Should().ContainSingle().Subject;

        row.SellingPrice.Should().Be(25m);
        row.Mrp.Should().Be(40m);
        row.ProductCost.Should().Be(11.25m);
        row.PackagingCost.Should().Be(2m);
        row.ShippingCost.Should().Be(3.50m);
        row.ProductCostSourceColumn.Should().Be("Wholesale Price");
        row.PackagingCostSourceColumn.Should().Be("Packaging Charges");
        row.ShippingCostSourceColumn.Should().Be("Shipping Cost2");
    }

    [Fact]
    public void DefaultSupplierCostMappingsDoNotGuessCostPerItemAndMissingOptionalHeadersAreIgnored()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Cost per item\nproduct,Product,SKU,25,10\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings()).Should().ContainSingle().Subject;
        row.ProductCost.Should().BeNull();
        row.PackagingCost.Should().BeNull();
        row.ShippingCost.Should().BeNull();
        row.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MissingMappedCostColumnIsReportedWithoutConvertingValueToZero()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price\nproduct,Product,SKU,25\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            Mappings(("productCost", "Wholesale Price"))).Single();

        row.ProductCost.Should().BeNull();
        row.ProductCostSourceColumn.Should().BeNull();
        row.Warnings.Should().Contain(x => x.Contains("Wholesale Price") && x.Contains("was not found"));
    }

    [Fact]
    public void EvaluatesNumericExcelCostFormulasAndReadsCostsOnlyFromPrimaryProductRow()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Products");
            var headers = new[] { "Handle", "Title", "Variant SKU", "Variant Price", "Product Cost Calc", "Pack Formula", "Ship Estimate", "Image Src" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "formula-product"; sheet.Cell(2, 2).Value = "Formula Product";
            sheet.Cell(2, 3).Value = "SKU-F"; sheet.Cell(2, 4).Value = 100;
            sheet.Cell(2, 5).FormulaA1 = "=20+5"; sheet.Cell(2, 6).FormulaA1 = "=2*3"; sheet.Cell(2, 7).FormulaA1 = "=1.25+0.75";
            sheet.Cell(2, 8).Value = "https://cdn.shopify.com/primary.jpg";
            sheet.Cell(3, 8).Value = "https://cdn.shopify.com/detail.jpg";
            sheet.Cell(3, 5).FormulaA1 = "=999"; // An image continuation row must not replace primary-row costs.
            workbook.SaveAs(stream);
        }
        var mappings = Mappings(("productCost", "Product Cost Calc"), ("packagingCost", "Pack Formula"), ("shippingCost", "Ship Estimate"));

        var row = ShopifyRetailerImportParser.Parse(stream.ToArray(), "shopify.xlsx", mappings).Single();

        row.ProductCost.Should().Be(25m);
        row.PackagingCost.Should().Be(6m);
        row.ShippingCost.Should().Be(2m);
        row.Images.Should().HaveCount(2);
        row.Warnings.Should().BeEmpty();
        ShopifyRetailerProductImportController.EstimateSupplierTotal(row.ProductCost, row.PackagingCost, row.ShippingCost).Should().Be(33m);
    }

    [Fact]
    public void ExactExcelMappedPricingUsesImportedAmountsWithoutAdjustments()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Products");
            var headers = new[] { "Handle", "Title", "Variant SKU", "Variant Price", "Packaging Cost", "Shipping Cost", "Supplier Product Cost", "ERP Selling Price", "Variant Compare At Price", "Product Category", "Type", "Image Src" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "mapped-pricing"; sheet.Cell(2, 2).Value = "Mapped Pricing"; sheet.Cell(2, 3).Value = "SKU-459";
            sheet.Cell(2, 4).Value = 238; sheet.Cell(2, 5).FormulaA1 = "=10*3"; sheet.Cell(2, 6).FormulaA1 = "=120/2";
            sheet.Cell(2, 7).FormulaA1 = "=D2+E2+F2"; sheet.Cell(2, 8).FormulaA1 = "=ROUND(G2*1.4,0)"; sheet.Cell(2, 9).Value = 714;
            sheet.Cell(2, 10).Value = "Shipping Supplies"; sheet.Cell(2, 11).Value = "Wrong Type";
            sheet.Cell(2, 12).Value = "https://cdn.shopify.com/primary.jpg";
            sheet.Cell(3, 7).FormulaA1 = "=999"; // Continuation rows cannot replace primary-row pricing.
            sheet.Cell(3, 12).Value = "https://cdn.shopify.com/detail.jpg";
            workbook.SaveAs(stream);
        }
        var mappings = Mappings(
            ("sellingPrice", "ERP Selling Price"), ("mrp", "Variant Compare At Price"),
            ("productCost", "Supplier Product Cost"), ("packagingCost", "Packaging Cost"),
            ("shippingCost", "Shipping Cost"), ("category", "Product Category"));

        // Mirror the ERP's JSON.stringify(FormData) request: property names are
        // lower camelCase, including the enclosing fieldMappings property.
        var selectionsJson = JsonSerializer.Serialize(new { fieldMappings = mappings });
        ShopifyRetailerProductImportController.TrySelections(selectionsJson, out var selections, out var selectionError)
            .Should().BeTrue(selectionError);
        selections.FieldMappings.Should().BeEquivalentTo(mappings);

        var row = ShopifyRetailerImportParser.Parse(stream.ToArray(), "guturgo.xlsx", selections.FieldMappings).Single();

        row.SellingPrice.Should().Be(459m);
        row.Mrp.Should().Be(714m);
        row.ProductCost.Should().Be(328m);
        row.PackagingCost.Should().Be(30m);
        row.ShippingCost.Should().Be(60m);
        row.Type.Should().Be("Shipping Supplies");
        row.Images.Should().HaveCount(2);
        row.Errors.Should().BeEmpty();
        row.Warnings.Should().BeEmpty();
        var total = ShopifyRetailerProductImportController.EstimateSupplierTotal(row.ProductCost, row.PackagingCost, row.ShippingCost, "INCLUSIVE");
        total.Should().Be(328m);
        var profit = ShopifyRetailerProductImportController.EstimateProfitLoss(row.SellingPrice, total);
        profit.Should().Be(131m);
        ShopifyRetailerProductImportController.EstimateMarginPercentage(row.SellingPrice, profit).Should().Be(28.54m);
        ShopifyRetailerProductImportController.SupplierPricingWarnings(row.SellingPrice, row.ProductCost, row.PackagingCost, row.ShippingCost, "INCLUSIVE").Should().BeEmpty();

        var preview = ShopifyRetailerProductImportController.ProjectPricingPreview(row, 459m, 714m, 328m, 30m, 60m, "INCLUSIVE");
        preview.ImportedSellingPrice.Should().Be(459m);
        preview.EffectiveSellingPrice.Should().Be(459m);
        preview.ImportedMrp.Should().Be(714m);
        preview.ImportedSupplierProductCost.Should().Be(328m);
        preview.ImportedTotalSupplierCost.Should().Be(328m);
        preview.ImportedProfitLoss.Should().Be(131m);
        preview.ImportedMarginPercentage.Should().Be(28.54m);
        preview.ImportedPricingWarnings.Should().BeEmpty();
        preview.EffectivePricingWarnings.Should().BeEmpty();

        ShopifyRetailerProductImportController.EstimateSupplierTotal(row.ProductCost, row.PackagingCost, row.ShippingCost, "EXCLUSIVE").Should().Be(418m);
        var unchangedExistingPrice = ShopifyRetailerProductImportController.ProjectPricingPreview(row, 459m, 714m, 328m, 30m, 60m, "INCLUSIVE");
        unchangedExistingPrice.ImportedSellingPrice.Should().Be(459m);
        unchangedExistingPrice.EffectiveSellingPrice.Should().Be(459m);
        unchangedExistingPrice.ImportedPricingWarnings.Should().BeEmpty();
        unchangedExistingPrice.EffectivePricingWarnings.Should().BeEmpty();
    }

    [Fact]
    public void ActualEasyFurnitureLifterRowsKeepPrimaryPricingAndAllEightImageOnlyRows()
    {
        const string handle = "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set";
        const string title = "Easy Furniture Lifter Tool Set with Lever and 4 Wheeled Sliders";
        const string csv = "Handle,Title,Variant SKU,Variant Barcode,ERP Selling Price,Variant Compare At Price,Variant Price,Supplier Product Cost,Packaging Cost,Shipping Cost,Product Category,Image Src,Image Position,Image Alt Text,Option1 Name,Option1 Value\n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,Easy Furniture Lifter Tool Set with Lever and 4 Wheeled Sliders,1619_easy_furniture_lifter,,815,1566,522,582,20,40,Hand & Power Tools,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-1.jpg?v=1786699472,1,,Set,1 Set\n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/sku_9bf43863-5d33-471d-9f81-d2c7b8520367.jpg?v=1786699472,2,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-3.jpg?v=1786699472,3,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-4.jpg?v=1786699472,4,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-7.jpg?v=1786699472,5,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-6.jpg?v=1786699472,6,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-2.jpg?v=1786699472,7,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-5.jpg?v=1786699472,8,, , \n" +
            "easy-furniture-lifter-tool-set-with-lever-and-4-wheeled-sliders-1-set,,,,126,,,90,30,60,,https://cdn.shopify.com/s/files/1/0782/3284/6646/files/Moving-Tool-8.jpg?v=1786699472,9,, , \n";
        var mappings = Mappings(("sellingPrice", "ERP Selling Price"), ("mrp", "Variant Compare At Price"),
            ("productCost", "Variant Price"), ("packagingCost", "Packaging Cost"),
            ("shippingCost", "Shipping Cost"), ("category", "Product Category"));

        var product = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "guturgo-easy-furniture-lifter.csv", mappings)
            .Should().ContainSingle().Subject;

        product.Handle.Should().Be(handle);
        product.Title.Should().Be(title);
        product.Sku.Should().Be("1619_easy_furniture_lifter");
        product.ProductCost.Should().Be(522m);
        product.PackagingCost.Should().Be(20m);
        product.ShippingCost.Should().Be(40m);
        product.SellingPrice.Should().Be(815m);
        product.Mrp.Should().Be(1566m);
        product.Type.Should().Be("Hand & Power Tools");
        product.Images.Should().HaveCount(9);
        product.Images.Select(x => x.Position).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        product.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GenuineSameHandleVariantRowsWithDifferentSkusAndPricesRemainBlocking()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Option1 Name,Option1 Value,Image Src\n" +
            "two-variants,Product,SKU-SMALL,10,Size,Small,https://cdn.shopify.com/small.jpg\n" +
            "two-variants,Product,SKU-LARGE,20,Size,Large,https://cdn.shopify.com/large.jpg\n";

        var product = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "variants.csv").Should().ContainSingle().Subject;

        product.Errors.Should().Contain(x => x.Contains("multiple variant SKUs", StringComparison.OrdinalIgnoreCase));
        product.Errors.Should().Contain(x => x.Contains("multiple variant Variant Price values", StringComparison.OrdinalIgnoreCase));
        product.Images.Should().HaveCount(2);
    }

    [Fact]
    public void UnreadableExcelCostFormulaWarnsAndRemainsMissing()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Products");
            var headers = new[] { "Handle", "Title", "Variant SKU", "Variant Price", "Supplier Product Cost" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "formula-error"; sheet.Cell(2, 2).Value = "Formula Error";
            sheet.Cell(2, 3).Value = "SKU-ERR"; sheet.Cell(2, 4).Value = 100; sheet.Cell(2, 5).FormulaA1 = "=1/0";
            workbook.SaveAs(stream);
        }

        var row = ShopifyRetailerImportParser.Parse(stream.ToArray(), "shopify.xlsx").Single();

        row.ProductCost.Should().BeNull();
        row.Errors.Should().BeEmpty();
        row.Warnings.Should().Contain(x => x.Contains("Supplier product cost formula could not be evaluated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplicitProductCategoryMappingDoesNotFallBackToTypeColumn()
    {
        const string csv = "Handle,Title,Type,Product Category,Variant SKU,Variant Price\nproduct,Product,Wrong Type,Kitchen & Dining,SKU,25\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            Mappings(("category", "Product Category"))).Single();

        row.Type.Should().Be("Kitchen & Dining");
        row.CategorySourceColumn.Should().Be("Product Category");
    }

    [Fact]
    public void DefaultCategoryDoesNotSilentlyUseTypeWhenProductCategoryIsMissing()
    {
        const string csv = "Handle,Title,Type,Variant SKU,Variant Price\nproduct,Product,Wrong Type,SKU,25\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings()).Single();

        row.Type.Should().BeNull();
        row.CategorySourceColumn.Should().BeNull();
    }

    [Fact]
    public void ExplicitZeroIsDistinctFromBlankForMappedCosts()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Supplier Product Cost,Packaging Cost,Shipping Cost\nzero,Zero,SKU-Z,25,0,0,0\nblank,Blank,SKU-B,25,,,\n";
        var mappings = ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings();
        var rows = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv", mappings);
        var zero = rows.Single(x => x.Handle == "zero");
        var blank = rows.Single(x => x.Handle == "blank");

        zero.ProductCost.Should().Be(0m);
        zero.PackagingCost.Should().Be(0m);
        zero.ShippingCost.Should().Be(0m);
        blank.ProductCost.Should().BeNull();
        blank.PackagingCost.Should().BeNull();
        blank.ShippingCost.Should().BeNull();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("₹12")]
    [InlineData("1.12345")]
    [InlineData("100000000000000")]
    public void InvalidSupplierCostAmountsAreRejected(string amount)
    {
        var csv = $"Handle,Title,Variant SKU,Variant Price,Packaging Cost\nproduct,Product,SKU,25,{amount}\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings()).Should().ContainSingle().Subject;
        row.Errors.Should().Contain(x => x.Contains("Packaging cost", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeSellingPriceMrpAndSupplierCostRemainBlockingValidationErrors()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Variant Compare At Price,Supplier Product Cost\nproduct,Product,SKU,-1,-2,-3\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings()).Single();

        row.Errors.Should().Contain(x => x.Contains("Variant Price"));
        row.Errors.Should().Contain(x => x.Contains("Variant Compare At Price"));
        row.Errors.Should().Contain(x => x.Contains("Supplier product cost"));
    }

    [Fact]
    public void DuplicateSelectedHeadersAreRejectedAsAmbiguous()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Packaging Cost,Packaging Cost\nproduct,Product,SKU,25,2,3\n";
        Action parse = () => ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv",
            ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings());
        parse.Should().Throw<InvalidDataException>().WithMessage("*duplicate matching headers*");
    }

    [Fact]
    public void HeaderDetectionReturnsActualCustomHeadersIncludingDuplicates()
    {
        const string csv = "Handle,Variant Price,Shipping Cost2,Shipping Cost2\n";
        ShopifyRetailerImportParser.ReadHeaders(Encoding.UTF8.GetBytes(csv), "shopify.csv")
            .Should().Equal("Handle", "Variant Price", "Shipping Cost2", "Shipping Cost2");
    }

    [Fact]
    public void DefaultPriceMappingContinuesToAcceptCurrentShopifyAliases()
    {
        const string csv = "Handle,Title,SKU,Price,Compare-at price\nproduct,Product,SKU,25,40\n";
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["handle"] = "Handle", ["title"] = "Title", ["sku"] = "Variant SKU",
            ["sellingPrice"] = "Variant Price", ["mrp"] = "Variant Compare At Price",
            ["description"] = "", ["category"] = "", ["brand"] = "", ["barcode"] = "",
            ["image"] = "", ["imagePosition"] = "", ["imageAlt"] = "", ["productCost"] = "",
            ["gstPercentage"] = "", ["weight"] = "", ["weightUnit"] = ""
        };

        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv", mappings).Should().ContainSingle().Subject;

        row.SellingPrice.Should().Be(25m);
        row.Mrp.Should().Be(40m);
    }

    [Fact]
    public void BlankHandleImageContinuationAttachesOnlyToPreviousProduct()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Image Src,Image Position\nhandle-a,Product A,SKU-A,20,https://cdn.shopify.com/a.jpg,1\n,,, ,https://cdn.shopify.com/a-detail.jpg,2\nhandle-b,Product B,SKU-B,30,https://cdn.shopify.com/b.jpg,1\n";
        var rows = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv");
        rows.Should().HaveCount(2);
        rows.Single(x => x.Handle == "handle-a").Images.Should().HaveCount(2);
        rows.Single(x => x.Handle == "handle-b").Images.Should().ContainSingle();
    }

    [Fact]
    public void DuplicateSkusAcrossHandlesRemainSeparateProducts()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price\none,One,REUSED,10\ntwo,Two,REUSED,20\n";
        var rows = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv");
        rows.Should().HaveCount(2);
        rows.Select(x => x.Handle).Should().Equal("one", "two");
        rows.Select(x => x.Sku).Should().OnlyContain(x => x == "REUSED");
    }

    [Fact]
    public void SupplierSkuIsPreservedExactlyAndImageOnlyRowsRemainGroupedByHandle()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price,Image Src,Image Position\nfirst,First,  SHARED-1  ,10,https://cdn.shopify.com/first.jpg,1\n,,, ,https://cdn.shopify.com/first-detail.jpg,2\nsecond,Second,  SHARED-1  ,11,https://cdn.shopify.com/second.jpg,1\n";

        var rows = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv");

        rows.Should().HaveCount(2);
        rows.Select(x => x.Sku).Should().OnlyContain(x => x == "  SHARED-1  ");
        rows.Single(x => x.Handle == "first").Images.Should().HaveCount(2);
        rows.Single(x => x.Handle == "second").Images.Should().ContainSingle();
        ShopifyImportIdentityRules.FindSharedSupplierSkus(rows.Select(x => (x.Handle, x.Sku)))
            .Should().Contain("SHARED-1");
        ShopifyImportIdentityRules.SourceIdentityKey("first", rows.First().Sku)
            .Should().Be(ShopifyImportIdentityRules.SourceIdentityKey(" FIRST ", "SHARED-1"));
    }

    [Fact]
    public void SameHandleAndSupplierSkuRetainTheSameImportIdentityOnReimport()
    {
        const string firstImport = "Handle,Title,Variant SKU,Variant Price\nhandle-a,Old title,SKU-1,10\n";
        const string reimport = "Handle,Title,Variant SKU,Variant Price\nHANDLE-A,Updated title, SKU-1 ,11\n";
        var first = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(firstImport), "first.csv").Single();
        var updated = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(reimport), "updated.csv").Single();

        ShopifyImportIdentityRules.SourceIdentityKey(first.Handle, first.Sku)
            .Should().Be(ShopifyImportIdentityRules.SourceIdentityKey(updated.Handle, updated.Sku));
        updated.Sku.Should().Be(" SKU-1 ");
        updated.Title.Should().Be("Updated title");
    }

    [Fact]
    public void CurrentShopifyHeaderAliasesAreRecognized()
    {
        const string csv = "Product handle,Product title,Description,Category,Brand,SKU,GTIN,Price,Compare-at price,Image URL,Image position,Alt Text,Product status\nnew-handle,Modern Product,Description,Home,Brand,SKU-NEW,123,49.5,60,https://cdn.shopify.com/new.jpg,1,front,draft\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv").Should().ContainSingle().Subject;
        row.Handle.Should().Be("new-handle"); row.Title.Should().Be("Modern Product"); row.SellingPrice.Should().Be(49.5m); row.Mrp.Should().Be(60m); row.Barcode.Should().Be("123");
    }

    [Fact]
    public void MultipleVariantSkusUnderOneHandleRequireReview()
    {
        const string csv = "Handle,Title,Variant SKU,Variant Price\nproduct,Product,SKU-1,10\nproduct,Product,SKU-2,10\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv").Should().ContainSingle().Subject;
        row.Errors.Should().Contain(x => x.Contains("multiple variant SKUs", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-price")]
    public void InvalidPricesAreRejected(string price)
    {
        var csv = $"Handle,Title,Variant SKU,Variant Price\nproduct,Product,SKU-1,{price}\n";
        var row = ShopifyRetailerImportParser.Parse(Encoding.UTF8.GetBytes(csv), "shopify.csv").Should().ContainSingle().Subject;
        row.Errors.Should().Contain(x => x.Contains("Variant Price", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> Mappings(params (string Key, string Value)[] overrides)
    {
        var mappings = ShopifyRetailerProductImportController.ImportSelections.DefaultFieldMappings();
        foreach (var (key, value) in overrides) mappings[key] = value;
        return mappings;
    }
}
