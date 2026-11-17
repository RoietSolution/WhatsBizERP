using System.Text;
using FluentAssertions;
using WhatsBiz.Infrastructure.Suppliers;
using Xunit;

namespace WhatsBiz.Tests.Inventory;

public sealed class ShopifyInventoryImportParserTests
{
    [Fact]
    public void ReadsAbsoluteOnHandAndIgnoresReferenceColumns()
    {
        const string csv = "Handle,SKU,Location,On hand (current),Available (not editable),Committed (not editable),Unavailable (not editable),Incoming (not editable),HS Code,COO,Bin name\nshirt,SKU-M,Shop Location,43,100,2,1,7,610910,IN,BOX-A\n";
        var row = ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv), "daily.csv").Should().ContainSingle().Subject;
        row.OnHand.Should().Be(43); row.Location.Should().Be("Shop Location"); row.HsnCode.Should().Be("610910"); row.CountryOfOrigin.Should().Be("IN"); row.BinName.Should().Be("BOX-A");
    }

    [Fact]
    public void DoesNotAcceptAvailableQuantityWithoutOnHandColumn()
    {
        const string csv = "SKU,Location,Available (not editable)\nSKU-M,Shop Location,43\n";
        var act = () => ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv), "daily.csv");
        act.Should().Throw<InvalidDataException>().WithMessage("*On hand (current)*");
    }

    [Fact]
    public void AcceptsCurrentShopifyInventoryHeaderAliases()
    {
        const string csv = "Product handle,Variant SKU,Shop Location,On hand current\nproduct,SKU-1,Main Shop,5\n";
        var row = ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv), "snapshot.csv").Should().ContainSingle().Subject;
        row.Handle.Should().Be("product"); row.Sku.Should().Be("SKU-1"); row.Location.Should().Be("Main Shop"); row.OnHand.Should().Be(5);
    }

    [Fact]
    public void PreservesSupplierSkuTextForIdentityMatching()
    {
        const string csv = "Handle,SKU,Location,On hand (current)\nproduct,  SKU-1  ,Main Shop,5\n";
        ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv), "snapshot.csv")
            .Should().ContainSingle().Which.Sku.Should().Be("  SKU-1  ");
    }

    [Fact]
    public void AcceptsDailySnapshotAboveFiveThousandRowsWithinConfiguredBound()
    {
        var csv = new StringBuilder("Handle,SKU,Location,On hand (current)\n");
        for (var i = 0; i < 5_001; i++) csv.Append("handle-").Append(i).Append(",SKU-").Append(i).AppendLine(",Shop Location,0");
        ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv.ToString()), "daily.csv").Should().HaveCount(5_001);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("unknown")]
    public void RejectsInvalidAbsoluteQuantities(string quantity)
    {
        var csv = $"Handle,Location,On hand (current)\nshirt,Shop Location,{quantity}\n";
        var row = ShopifyInventoryImportParser.Parse(Encoding.UTF8.GetBytes(csv), "daily.csv").Should().ContainSingle().Subject;
        row.Errors.Should().Contain(x => x.StartsWith("On hand (current) must be a non-negative quantity", StringComparison.Ordinal));
    }
}
