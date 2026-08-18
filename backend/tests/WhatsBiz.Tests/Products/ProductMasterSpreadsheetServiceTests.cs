using FluentAssertions;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductMasterSpreadsheetServiceTests
{
    private readonly ProductMasterSpreadsheetService service = new();

    [Fact]
    public void CategoryTemplateCanBeRead()
    {
        var row = service.ReadCategories(service.CategoryTemplate()).Single();
        row.Code.Should().Be("CAT-001");
        row.Name.Should().Be("Sample Category");
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public void BrandTemplateCanBeRead()
    {
        var row = service.ReadBrands(service.BrandTemplate()).Single();
        row.Code.Should().Be("BRD-001");
        row.Name.Should().Be("Sample Brand");
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UnitTemplateCanBeRead()
    {
        var row = service.ReadUnits(service.UnitTemplate()).Single();
        row.Code.Should().Be("PCS");
        row.ShortName.Should().Be("Pc");
        row.DecimalPlaces.Should().Be(0);
    }
}
