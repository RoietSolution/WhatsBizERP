using System.Text;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Api.Controllers;

public sealed record ShopifyCategoryOption(Guid ProductCategoryId, string CategoryName, string CategoryCode,
    Guid? ParentCategoryId, string DisplayName);

public sealed record ShopifyCategoryMatch(string Status, Guid? ProductCategoryId,
    IReadOnlyCollection<ShopifyCategoryOption> Candidates);

/// <summary>Unambiguous automatic category binding for retailer-scoped active categories.</summary>
public static class ShopifyCategoryBinding
{
    public static IReadOnlyCollection<ShopifyCategoryOption> Options(IReadOnlyCollection<ProductCategory> categories)
    {
        var byId = categories.ToDictionary(x => x.ProductCategoryId);
        return categories.Select(category => new ShopifyCategoryOption(category.ProductCategoryId, category.CategoryName,
            category.CategoryCode, category.ParentCategoryId, BuildDisplayPath(category, byId))).ToArray();
    }

    public static ShopifyCategoryMatch Match(string? value, IReadOnlyCollection<ProductCategory> categories)
    {
        if (string.IsNullOrWhiteSpace(value)) return new("UNMATCHED", null, []);
        var normalized = Normalize(value);
        var pathKey = NormalizePath(value);
        var hasPath = HasHierarchySeparator(value);
        var matches = Options(categories).Where(option => Normalize(option.CategoryName) == normalized ||
                Normalize(option.CategoryCode) == normalized ||
                hasPath && NormalizePath(option.DisplayName) == pathKey)
            .GroupBy(x => x.ProductCategoryId).Select(x => x.First()).ToArray();
        return matches.Length switch
        {
            0 => new("UNMATCHED", null, matches),
            1 => new("MATCHED", matches[0].ProductCategoryId, matches),
            _ => new("AMBIGUOUS", null, matches)
        };
    }

    public static string Normalize(string? value) => string.Concat((value ?? string.Empty).Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string BuildDisplayPath(ProductCategory category, Dictionary<Guid, ProductCategory> byId)
    {
        var chain = new List<string>();
        var current = category;
        var visited = new HashSet<Guid>();
        while (visited.Add(current.ProductCategoryId))
        {
            chain.Add(current.CategoryName);
            if (!current.ParentCategoryId.HasValue || !byId.TryGetValue(current.ParentCategoryId.Value, out var parent)) break;
            current = parent;
        }
        chain.Reverse();
        return string.Join(" > ", chain);
    }

    private static bool HasHierarchySeparator(string value) => value.Any(c => c is '>' or '/' or '|' or '\\');

    private static string NormalizePath(string value) => string.Join(" > ", value
        .Split(['>', '/', '|', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(Normalize));
}
