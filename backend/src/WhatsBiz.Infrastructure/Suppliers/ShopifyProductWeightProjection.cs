using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyProductWeightProjection
{
    public const string WeightUnitSpecificationName = "Physical Weight Unit";

    public static (decimal Value, string Unit) ToProductWeight(decimal grams, string unit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(grams);
        var normalizedUnit = unit.Trim().ToLowerInvariant();
        var value = normalizedUnit switch
        {
            "g" => grams,
            "kg" => grams / 1000m,
            "lb" => grams / 453.59237m,
            "oz" => grams / 28.349523125m,
            _ => throw new ArgumentException("Weight unit must be g, kg, oz, or lb.", nameof(unit))
        };

        return (decimal.Round(value, 4, MidpointRounding.AwayFromZero), normalizedUnit);
    }

    public static ProductSpecification CreateUnitSpecification(Guid tenantId, Guid productId, string unit, string? username) => new()
    {
        TenantId = tenantId,
        ProductId = productId,
        Name = WeightUnitSpecificationName,
        Value = unit,
        DisplayOrder = 0,
        CreatedBy = username
    };
}
