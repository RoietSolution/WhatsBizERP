using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyCatalogPricingPolicy
{
    public const string GstPricingType = "INCLUSIVE";

    public static decimal? ResolveGstPercentage(ShopifyCatalogRow row, decimal? supplierDefault)
        => row.GstPercentage ?? supplierDefault;

    public static string GstSource(ShopifyCatalogRow row, decimal? supplierDefault)
        => row.GstPercentage.HasValue ? "Workbook" : supplierDefault.HasValue ? "Default" : "Missing";

    public static decimal? CalculateErpCostPrice(ShopifyCatalogRow row) => row.ErpCostPrice;

    public static void ApplyRetailerPricing(Product product, ShopifyCatalogRow row, decimal gstPercentage)
    {
        product.SellingPrice = row.SellingPrice!.Value;
        product.MRP = row.Mrp!.Value;
        product.GSTPercentage = gstPercentage;
        product.GstPricingType = GstPricingType;
    }

    public static IReadOnlyCollection<string> Validate(ShopifyCatalogRow row)
    {
        var errors = new List<string>();
        if (row.ProductCost is null) errors.Add("Cost per item is required.");
        if (row.CombinedShippingPackagingCost is null) errors.Add("Variant Price is required for combined shipping and packaging cost.");
        if (row.SellingPrice is null) errors.Add("ERP Selling Price is required from the retailer-editable ERP Selling Price column.");
        if (row.Mrp is null) errors.Add("ERP MRP is required from the retailer-editable ERP MRP column.");
        if (row.SellingPrice is < 0) errors.Add("ERP Selling Price must be non-negative.");
        if (row.Mrp is < 0) errors.Add("ERP MRP must be non-negative.");
        if (row.SellingPrice.HasValue && row.ProductCost.HasValue && row.SellingPrice.Value < row.ProductCost.Value)
            errors.Add("ERP Selling Price cannot be below supplier product cost.");
        if (row.Mrp.HasValue && row.SellingPrice.HasValue && row.Mrp.Value < row.SellingPrice.Value)
            errors.Add("ERP MRP cannot be below ERP Selling Price.");
        return errors;
    }
}
