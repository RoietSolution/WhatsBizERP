namespace WhatsBiz.Application.Features.Products.Products;

public sealed record ProductPricingPreviewResult(decimal GstAmount, decimal CustomerTotal, string GstPricingType);

public static class ProductPricingPolicy
{
    public static ProductPricingPreviewResult Calculate(decimal sellingPrice, decimal gstPercentage, string pricingType)
    {
        if (sellingPrice < 0 || gstPercentage is < 0 or > 100 || pricingType is not ("INCLUSIVE" or "EXCLUSIVE"))
            throw new ArgumentException("Product pricing values are invalid.");

        var tax = pricingType == "INCLUSIVE"
            ? sellingPrice * gstPercentage / (100m + gstPercentage)
            : sellingPrice * gstPercentage / 100m;
        var total = pricingType == "INCLUSIVE" ? sellingPrice : sellingPrice + tax;
        return new(decimal.Round(tax, 2, MidpointRounding.AwayFromZero), decimal.Round(total, 2, MidpointRounding.AwayFromZero), pricingType);
    }
}
