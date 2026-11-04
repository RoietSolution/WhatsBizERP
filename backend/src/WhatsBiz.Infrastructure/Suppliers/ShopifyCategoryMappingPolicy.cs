namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyCategoryMappingPolicy
{
    public static string? FromTags(string? tags)
    {
        var category = ShopifyCatalogImportParser.Normalize(tags);
        return category.Length == 0 ? null : category;
    }

    public static IReadOnlyCollection<string> ParseTagsAsCategory(string? tags)
        => FromTags(tags) is { } category ? [category] : [];

    public static bool Matches(string? tags, string? erpCategoryName)
    {
        var category = FromTags(tags);
        return category is not null
            && string.Equals(category, ShopifyCatalogImportParser.Normalize(erpCategoryName), StringComparison.OrdinalIgnoreCase);
    }
}
