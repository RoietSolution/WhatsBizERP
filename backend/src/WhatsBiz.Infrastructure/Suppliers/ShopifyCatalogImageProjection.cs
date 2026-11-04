using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyCatalogImageProjection
{
    public static IReadOnlyCollection<ProductExternalImage> ToExternalImages(
        Guid tenantId,
        Guid productId,
        Guid supplierId,
        IReadOnlyCollection<ShopifyCatalogImage> images)
    {
        var ordered = images
            .Where(image => ShopifyCatalogImportParser.SafeHttps(image.Url) is not null)
            .GroupBy(image => ShopifyCatalogImportParser.SafeHttps(image.Url)!, StringComparer.Ordinal)
            .Select(group => group.OrderBy(image => image.Position).First())
            .OrderBy(image => image.Position)
            .ThenBy(image => ShopifyCatalogImportParser.SafeHttps(image.Url), StringComparer.Ordinal)
            .ToArray();
        var primary = ordered.FirstOrDefault(image => image.Position == 1) ?? ordered.FirstOrDefault();
        if (primary is null) return [];

        return ordered.Select(image => new ProductExternalImage
        {
            TenantId = tenantId,
            ProductId = productId,
            SupplierId = supplierId,
            ImageUrl = ShopifyCatalogImportParser.SafeHttps(image.Url)!,
            DisplayOrder = Math.Max(0, image.Position),
            IsPrimary = string.Equals(ShopifyCatalogImportParser.SafeHttps(image.Url), ShopifyCatalogImportParser.SafeHttps(primary.Url), StringComparison.Ordinal)
        }).ToArray();
    }

    public static bool ShouldReplaceSupplierImages(bool imagesAreSupplierOwned, IReadOnlyCollection<ShopifyCatalogImage> incoming) =>
        imagesAreSupplierOwned && incoming.Any(image => ShopifyCatalogImportParser.SafeHttps(image.Url) is not null);
}
