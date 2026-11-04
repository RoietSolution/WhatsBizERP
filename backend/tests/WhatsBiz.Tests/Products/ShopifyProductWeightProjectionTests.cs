using FluentAssertions;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyProductWeightProjectionTests
{
    [Theory]
    [InlineData(327, "g", 327)]
    [InlineData(327, "kg", 0.327)]
    [InlineData(327, "oz", 11.5346)]
    [InlineData(327, "lb", 0.7209)]
    public void ConvertsShopifyGramsToProductWeightWithoutUsingSellingUom(decimal grams, string unit, decimal expected)
    {
        var projected = ShopifyProductWeightProjection.ToProductWeight(grams, unit);

        projected.Value.Should().Be(expected);
        projected.Unit.Should().Be(unit);
    }

    [Fact]
    public void CreatesWeightUnitAsAProductSpecification()
    {
        var tenant = Guid.NewGuid();
        var product = Guid.NewGuid();
        var specification = ShopifyProductWeightProjection.CreateUnitSpecification(tenant, product, "g", "shopify-import");

        specification.TenantId.Should().Be(tenant);
        specification.ProductId.Should().Be(product);
        specification.Name.Should().Be("Physical Weight Unit");
        specification.Value.Should().Be("g");
    }
}
