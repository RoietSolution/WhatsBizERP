using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public sealed record ShopifyBrandMapping(Brand? Brand, string? NormalizedVendor, string Status, string? Message)
{
    public bool IsValid => Status is "EXISTING" or "DEFAULT" or "WILL_CREATE";
}

public static class ShopifyBrandMappingPolicy
{
    public static ShopifyBrandMapping Resolve(string? vendor, IReadOnlyCollection<Brand> tenantBrands, Brand? defaultBrand = null, bool defaultBrandConfigured = false)
    {
        var normalized = ShopifyCatalogImportParser.Normalize(vendor);
        if (normalized.Length == 0)
        {
            if (defaultBrand is not null)
                return defaultBrand.IsActive && !defaultBrand.IsDeleted
                    ? new(defaultBrand, null, "DEFAULT", $"Default brand matched: {defaultBrand.BrandName}")
                    : new(null, null, "INACTIVE", "The configured default brand is inactive; choose an active tenant brand.");

            return defaultBrandConfigured
                ? new(null, null, "INACTIVE", "The configured default brand is inactive or unavailable to this tenant.")
                : new(null, null, "MISSING", "Shopify Vendor is required unless an active tenant default brand is configured.");
        }

        var invalid = Validate(normalized);
        if (invalid is not null) return new(null, normalized, "INVALID", invalid);

        var matches = tenantBrands
            .Where(x => !x.IsDeleted && string.Equals(ShopifyCatalogImportParser.Normalize(x.BrandName), normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
            return new(null, normalized, "AMBIGUOUS", $"More than one tenant brand matches Vendor '{normalized}'. Resolve duplicate brands before importing.");
        if (matches.Length == 1)
            return matches[0].IsActive
                ? new(matches[0], normalized, "EXISTING", "Existing brand matched")
                : new(null, normalized, "INACTIVE", $"Brand '{normalized}' exists for this tenant but is inactive.");

        return new(null, normalized, "WILL_CREATE", $"Brand will be created: {normalized}");
    }

    public static string? Validate(string normalizedVendor)
    {
        if (normalizedVendor.Length > 200) return "Shopify Vendor must be 200 characters or fewer.";
        if (normalizedVendor.Any(char.IsControl)) return "Shopify Vendor contains unsupported control characters.";
        return null;
    }
}
