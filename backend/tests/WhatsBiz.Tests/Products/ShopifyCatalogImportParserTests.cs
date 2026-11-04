using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyCatalogImportParserTests
{
    [Fact]
    public void CsvUsesShopifyDefaultsAndTreatsTheCompleteTagsCellAsOneCategory()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Custom Product Type,Body (HTML),Cost per item,Variant Price,Variant Inventory Qty,Image Src,Status\n\"1234 - Linen Saree\",SKU-1,linen-saree,\"Women, Clothing\",Saree,\"<p>Soft &amp; comfortable</p>\",250,40,8,https://cdn.example.com/a.jpg,active";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();
        row.Title.Should().Be("Linen Saree");
        row.Tags.Should().Be("Women, Clothing");
        row.TagOptions.Should().ContainSingle().Which.Should().Be("Women, Clothing");
        row.MissingFields.Should().NotContain(x => x.Contains("Choose a primary category"));
        row.ProductCost.Should().Be(250);
        row.CombinedShippingPackagingCost.Should().Be(40, "Shopify Variant Price is combined shipping and packaging cost");
        row.SellingPrice.Should().BeNull("selling price must come from the retailer-editable column");
        row.Mrp.Should().BeNull();
        row.MissingFields.Should().Contain("ERP Selling Price").And.Contain("ERP MRP");
        row.ErpCostPrice.Should().Be(290m);
        row.SupplierAvailableQuantity.Should().Be(8);
        row.ImageUrl.Should().Be("https://cdn.example.com/a.jpg");
        row.DescriptionText.Should().Contain("Soft & comfortable");
    }

    [Theory]
    [InlineData("Kitchen Tools, Kitchen Accessories", "Kitchen Tools, Kitchen Accessories")]
    [InlineData("  Mobile   Accessories,   Mobile Headphones  ", "Mobile Accessories, Mobile Headphones")]
    [InlineData(" Computer   Accessories ", "Computer Accessories")]
    [InlineData(" Personal Care ", "Personal Care")]
    public void CompleteTagsCellMapsToOneWhitespaceNormalizedCategory(string tags, string expected)
    {
        ShopifyCategoryMappingPolicy.FromTags(tags).Should().Be(expected);
        ShopifyCategoryMappingPolicy.ParseTagsAsCategory(tags).Should().Equal(expected);
    }

    [Fact]
    public void CompleteTagsValueMatchesOnlyTheSameTenantCategoryName()
    {
        ShopifyCategoryMappingPolicy.Matches("Mobile Accessories, Mobile Headphones", "  Mobile  Accessories, Mobile Headphones ").Should().BeTrue();
        ShopifyCategoryMappingPolicy.Matches("Mobile Accessories, Mobile Headphones", "Mobile Headphones").Should().BeFalse();
    }

    [Fact]
    public void MissingShopifyTagsReportsMissingErpCategoryMapping()
    {
        const string csv = "Title,Variant SKU,Tags,Status\nProduct,SKU-1,,active";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        ShopifyCategoryMappingPolicy.FromTags(row.Tags).Should().BeNull();
        row.MissingFields.Should().Contain("Shopify Tags (ERP category)");
    }

    [Fact]
    public void ParserPreservesOriginalTagsTextWhileNormalizingTheCategoryName()
    {
        const string originalTags = "  Mobile   Accessories,   Mobile Headphones  ";
        var csv = $"Title,Tags,Status\nProduct,\"{originalTags}\",active";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.Tags.Should().Be(originalTags);
        row.TagOptions.Should().ContainSingle().Which.Should().Be("Mobile Accessories, Mobile Headphones");
    }

    [Fact]
    public void ExplicitMappingOverridesSupplierHeadersAndUnsafeImagesAreRejected()
    {
        const string csv = "Name,Stock,Picture,Status\nProduct,3,http://unsafe.test/image.png,active";
        var mappings = new Dictionary<string,string> { ["title"]="Name", ["quantity"]="Stock", ["image"]="Picture" };
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv", mappings).Single();
        row.Title.Should().Be("Product");
        row.SupplierAvailableQuantity.Should().Be(3);
        row.ImageUrl.Should().BeNull();
        row.Errors.Should().ContainSingle().Which.Should().Contain("HTTPS");
    }

    [Theory]
    [InlineData("  women   clothing ", "women clothing")]
    [InlineData("001- Product Name", "Product Name")]
    public void NormalizesNamesAndStripsLeadingNumericSupplierCode(string input, string expected)
    {
        (input.StartsWith('0') ? ShopifyCatalogImportParser.StripLeadingSupplierCode(input) : ShopifyCatalogImportParser.Normalize(input)).Should().Be(expected);
    }

    [Fact]
    public void CsvBomAndBlankOptionalMappingsAreHandled()
    {
        const string csv = "\uFEFFTitle,Tags,Variant SKU\nItem,Clothing,SKU-2";
        var mappings = new Dictionary<string,string> { ["title"] = "Title", ["tags"] = "Tags", ["sku"] = "Variant SKU", ["brand"] = "" };
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv", mappings).Single();
        row.Title.Should().Be("Item");
        row.BrandName.Should().BeNull();
    }

    [Fact]
    public void UnsupportedFileExtensionIsRejected()
    {
        var act = () => ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes("Title\nItem"), "catalog.xls");
        act.Should().Throw<InvalidDataException>().WithMessage("Only Shopify CSV and XLSX files are supported.");
    }

    [Fact]
    public void AdditionalImagesForSameShopifyHandleAreKeptAsExternalUrls()
    {
        const string csv = "Title,Handle,Tags,Image Src\nItem,item,Kitchen,https://cdn.example.com/one.jpg\n,item,,https://cdn.example.com/two.jpg";
        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();
        row.ImageUrl.Should().Be("https://cdn.example.com/one.jpg");
        row.AdditionalImageUrls.Should().ContainSingle().Which.Should().Be("https://cdn.example.com/two.jpg");
    }

    [Fact]
    public void XlsxMapsShopifyFieldsSanitizesDescriptionAndGroupsHandleImages()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");
        var headers = new[] { "Title", "Variant SKU", "Handle", "Tags", "Custom Product Type", "Body (HTML)", "Cost per item", "Variant Price", "Variant Inventory Qty", "Image Src", "ERP Selling Price", "ERP MRP" };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        var values = new object?[] { "123456 - Blue Shirt", "SKU-42", "blue-shirt", "Clothing", "Shirts", "<p>Soft <strong>cotton</strong></p><script>alert(1)</script>", 125m, 20m, 7, "https://cdn.example.com/main.jpg", 250m, 300m };
        for (var column = 0; column < values.Length; column++) sheet.Cell(2, column + 1).Value = XLCellValue.FromObject(values[column], System.Globalization.CultureInfo.InvariantCulture);
        sheet.Cell(3, 3).Value = "blue-shirt";
        sheet.Cell(3, 10).Value = "https://cdn.example.com/alternate.jpg";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var row = ShopifyCatalogImportParser.Parse(stream.ToArray(), "catalog.xlsx").Single();
        row.Title.Should().Be("Blue Shirt");
        row.TagOptions.Should().ContainSingle().Which.Should().Be("Clothing");
        row.ProductType.Should().Be("Shirts");
        row.ProductCost.Should().Be(125m);
        row.CombinedShippingPackagingCost.Should().Be(20m);
        row.ErpCostPrice.Should().Be(145m);
        row.SellingPrice.Should().Be(250m);
        row.Mrp.Should().Be(300m);
        row.SupplierAvailableQuantity.Should().Be(7m);
        row.DescriptionHtml.Should().Contain("<strong>cotton</strong>").And.NotContain("script");
        row.DescriptionText.Should().Contain("cotton");
        row.ImageUrl.Should().Be("https://cdn.example.com/main.jpg");
        row.AdditionalImageUrls.Should().ContainSingle().Which.Should().Be("https://cdn.example.com/alternate.jpg");
    }

    [Fact]
    public void ImageOnlyContinuationDescriptionDoesNotReplaceProductDescription()
    {
        const string csv = "Title,Variant SKU,Handle,Body (HTML),Image Src\n" +
            "Product,SKU-1,product,<p>Original <strong>description</strong></p>,https://cdn.example.com/main.jpg\n" +
            ",,product,<p>Continuation description</p>,https://cdn.example.com/extra.jpg";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.DescriptionHtml.Should().Be("<p>Original <strong>description</strong></p>");
        row.DescriptionText.Should().Contain("Original").And.NotContain("Continuation");
        row.Images.Should().HaveCount(2);
    }

    [Fact]
    public void GroupsVariantAndImageOnlyRowsByHandleOrdersImagesAndKeepsWeightSeparateFromSellingUnit()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Image Src,Image Position,Variant Grams,Variant Weight Unit,ERP Unit\n" +
            "Shirt,SKU-1,shirt,Clothing,https://cdn.example.com/back.jpg,2,250,g,PCS\n" +
            ",,shirt, ,https://cdn.example.com/front.jpg,1,,,\n" +
            "Shirt Blue,SKU-2,shirt,Clothing,https://cdn.example.com/front.jpg,1,0.25,kg,PCS\n" +
            ",,shirt, ,https://cdn.example.com/extra.jpg,3,,,";

        var rows = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv", new Dictionary<string, string> { ["unit"] = "ERP Unit" }).ToArray();

        rows.Should().HaveCount(2, "image-only continuation rows must not become products");
        rows[0].SupplierSku.Should().Be("SKU-1");
        rows[1].SupplierSku.Should().Be("SKU-2");
        foreach (var row in rows)
        {
            row.Images.Select(image => image.Position).Should().Equal(1, 2, 3);
            row.Images.Select(image => image.Url).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(row.Images.Count);
            row.ImageUrl.Should().Be("https://cdn.example.com/front.jpg");
            row.Images.Single(image => image.IsPrimary).Position.Should().Be(1);
        }
        rows[0].WeightGrams.Should().Be(250m);
        rows[0].WeightUnit.Should().Be("g");
        rows[1].WeightGrams.Should().Be(0.25m);
        rows[1].WeightUnit.Should().Be("kg");
        rows[0].UnitName.Should().Be("PCS", "Shopify weight unit must not replace the explicitly mapped ERP selling UOM");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    public void PreservesAllShopifyImagesForProductAndImageOnlyContinuationRows(int imageCount)
    {
        var csv = new StringBuilder("Title,Variant SKU,Handle,Image Src,Image Position\n")
            .AppendLine($"Product,SKU-IMG,image-product,https://cdn.shopify.com/p/1.jpg,1");
        for (var position = 2; position <= imageCount; position++)
            csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $",,image-product,https://cdn.shopify.com/p/{position}.jpg,{position}");

        var rows = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv.ToString()), "images.csv");

        rows.Should().ContainSingle();
        var product = rows.Single();
        product.Images.Should().HaveCount(imageCount);
        product.Images.Select(image => image.Position).Should().Equal(Enumerable.Range(1, imageCount));
        product.Images.Single(image => image.IsPrimary).Url.Should().Be("https://cdn.shopify.com/p/1.jpg");
        product.PrimaryImageFallback.Should().BeFalse();
    }

    [Fact]
    public void DeduplicatesExactUrlsAndUsesLowestPositionWithStableTieBreakAsPrimaryFallback()
    {
        const string csv = "Title,Variant SKU,Handle,Image Src,Image Position\n" +
            "Product,SKU-IMG,image-product,https://cdn.shopify.com/p/z.jpg,4\n" +
            ",,image-product,https://cdn.shopify.com/p/a.jpg,4\n" +
            ",,image-product,https://cdn.shopify.com/p/a.jpg,5\n";

        var product = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "images.csv").Single();

        product.Images.Should().HaveCount(2);
        product.Images.Select(image => image.Url).Should().Equal("https://cdn.shopify.com/p/a.jpg", "https://cdn.shopify.com/p/z.jpg");
        product.Images.Single(image => image.IsPrimary).Position.Should().Be(4);
        product.PrimaryImageFallback.Should().BeTrue();
    }

    [Fact]
    public void RepeatedSkuRowsMergeWithoutErasingFieldsThatRemainMissing()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Image Src,Image Position\n" +
            "Product,SKU-1,product,,https://cdn.example.com/a.jpg,1\n" +
            "Product,SKU-1,product,,https://cdn.example.com/b.jpg,2";
        var rows = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv");

        rows.Should().ContainSingle();
        rows.Single().MissingFields.Should().Contain("Shopify Tags (ERP category)");
        rows.Single().Images.Should().HaveCount(2);
    }

    [Fact]
    public void KeepsShopifyCompareAtSeparateFromRetailerSellingPriceAndErpMrp()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Cost per item,Variant Price,ERP Selling Price,ERP MRP,Variant Compare At Price,Vendor\n" +
            "Product,SKU-1,product,Clothing,100,25,150,175,200,Example Vendor";

        var defaultMapped = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();
        defaultMapped.ProductCost.Should().Be(100m);
        defaultMapped.CombinedShippingPackagingCost.Should().Be(25m);
        defaultMapped.ErpCostPrice.Should().Be(125m);
        defaultMapped.SellingPrice.Should().Be(150m);
        defaultMapped.CompareAtPrice.Should().Be(200m);
        defaultMapped.Mrp.Should().Be(175m, "ERP MRP comes from the retailer-editable column");
        defaultMapped.BrandName.Should().Be("Example Vendor");

        var explicitMrp = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv",
            new Dictionary<string, string> { ["mrp"] = "ERP MRP" }).Single();
        explicitMrp.Mrp.Should().Be(175m);
    }

    [Fact]
    public void RepairsLegacySavedVariantPriceCostMappingWithoutChangingInventoryOrUomMappings()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Cost per item,Variant Price,Variant Inventory Qty,ERP Selling Price,ERP MRP,Variant Compare At Price\nProduct,SKU-1,product,Clothing,100,25,4,150,175,999";
        var oldProfileMappings = new Dictionary<string, string>
        {
            ["combinedCost"] = "Variant Price",
            ["sellingPrice"] = "Variant Price",
            ["mrp"] = "Variant Compare At Price",
            ["unit"] = "ERP Unit"
        };

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv", oldProfileMappings).Single();

        row.ProductCost.Should().Be(100m);
        row.CombinedShippingPackagingCost.Should().Be(25m);
        row.SellingPrice.Should().Be(150m);
        row.Mrp.Should().Be(175m);
        row.CompareAtPrice.Should().Be(999m);
        row.ErpCostPrice.Should().Be(125m);
        row.SupplierAvailableQuantity.Should().Be(4m);
        row.UnitName.Should().BeNull("the ERP UOM column isn't in this source file and must not be inferred");
    }

    [Fact]
    public void UsesShopifysDocumentedKgWeightDisplayDefaultWhenUnitColumnIsBlank()
    {
        const string csv = "Title,Variant SKU,Handle,Variant Grams,Variant Weight Unit\nProduct,SKU-1,product,327,";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.WeightGrams.Should().Be(327m);
        row.WeightUnit.Should().Be("kg");
        row.WeightUnitWasDefaulted.Should().BeTrue();
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("inactive", false)]
    [InlineData("draft", false)]
    [InlineData("archived", false)]
    public void ShopifyStatusMapsToExpectedErpActiveState(string status, bool expected)
    {
        var csv = $"Title,Variant SKU,Handle,Tags,Status\nProduct,SKU-1,product,Kitchen,{status}";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.ShopifyStatus.Should().Be(status);
        row.ProposedIsActive.Should().Be(expected);
        row.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("paused")]
    public void BlankOrUnknownShopifyStatusBlocksImport(string status)
    {
        var csv = $"Title,Variant SKU,Handle,Tags,Status\nProduct,SKU-1,product,Kitchen,{status}";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.ProposedIsActive.Should().BeNull();
        row.Errors.Should().ContainSingle(error => error.Contains("Shopify Status"));
    }

    [Fact]
    public void ExistingProductStatusChangesOnlyWhenSupplierStatusSynchronizationIsEnabled()
    {
        ShopifyCatalogStatusPolicy.ResolveForPersistence(false, false, true, false).Should().BeTrue();
        ShopifyCatalogStatusPolicy.ResolveForPersistence(false, false, true, true).Should().BeFalse();
        ShopifyCatalogStatusPolicy.ResolveForPersistence(true, true, false, false).Should().BeTrue();
    }

    [Fact]
    public void ConflictingVariantStatusValuesBlockGroupedProduct()
    {
        const string csv = "Title,Variant SKU,Handle,Tags,Status\n" +
            "Product,SKU-1,product,Kitchen,active\n" +
            "Product,SKU-1,product,Kitchen,draft";

        var row = ShopifyCatalogImportParser.Parse(Encoding.UTF8.GetBytes(csv), "catalog.csv").Single();

        row.Errors.Should().Contain(error => error.Contains("conflicting Shopify Status"));
    }
}
