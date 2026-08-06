using FluentAssertions;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductSpreadsheetServiceTests
{
    [Fact]
    public void TemplateCanBeReadAsImportWorkbook()
    {
        var service = new ProductSpreadsheetService();
        var rows = service.Read(service.CreateTemplate());
        rows.Should().ContainSingle();
        rows.Single().ProductCode.Should().Be("PRD-001");
        rows.Single().SellingPrice.Should().Be(120);
    }
}
