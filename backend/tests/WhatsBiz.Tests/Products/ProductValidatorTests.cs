using FluentAssertions;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductValidatorTests
{
    [Fact]
    public async Task ValidProductPassesValidation()
    {
        var result = await new ProductInputValidator().ValidateAsync(CreateInput());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MissingRequiredReferencesFailValidation()
    {
        var input = CreateInput() with { ProductName = string.Empty, CategoryId = Guid.Empty, BrandId = Guid.Empty, UnitId = Guid.Empty };
        var result = await new ProductInputValidator().ValidateAsync(input);
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.ProductName));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.CategoryId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.BrandId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.UnitId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task GstOutsideAllowedRangeFailsValidation(decimal gst)
    {
        var result = await new ProductInputValidator().ValidateAsync(CreateInput() with { GSTPercentage = gst });
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.GSTPercentage));
    }

    [Fact]
    public async Task SellingPriceBelowPurchasePriceFailsValidation()
    {
        var result = await new ProductInputValidator().ValidateAsync(CreateInput() with { PurchasePrice = 100, SellingPrice = 99 });
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ProductInput.SellingPrice));
    }

    private static ProductInput CreateInput() => new("PRD-001", "890000000001", "Product", null, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1001", null, 18, 100, 120, 125, 0, 100, 10, null, null, null, null, false, false, true);
}
