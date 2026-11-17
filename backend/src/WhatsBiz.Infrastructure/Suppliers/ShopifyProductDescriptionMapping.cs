using WhatsBiz.Domain.Products;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyProductDescriptionMapping
{
    public static void ApplyToNewProduct(Product product, string? bodyHtml)
    {
        product.LongDescription = ProductDescriptionSanitizer.Sanitize(bodyHtml, 100000);
    }

    public static void ApplyToExistingProduct(Product product, string? bodyHtml, bool supplierOwnsDescription)
    {
        if (supplierOwnsDescription)
            product.LongDescription = ProductDescriptionSanitizer.Sanitize(bodyHtml, 100000);
    }
}
