using System.Security.Cryptography;
using System.Text;

namespace WhatsBiz.Api.Controllers;

/// <summary>Rules shared by the retailer product and inventory import endpoints.</summary>
public static class ShopifyImportIdentityRules
{
    public const string SharedSupplierSkuWarning = "Supplier SKU is shared by distinct Shopify Handles; each Handle + SKU is imported separately.";

    public static string NormalizeSku(string? sku) => (sku ?? string.Empty).Trim().ToUpperInvariant();

    public static string SourceIdentityKey(string handle, string? sku) =>
        $"{handle.Trim().ToUpperInvariant()}\u001f{NormalizeSku(sku)}";

    public static HashSet<string> FindSharedSupplierSkus(IEnumerable<(string Handle, string? Sku)> rows) => rows
        .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
        .GroupBy(x => NormalizeSku(x.Sku), StringComparer.Ordinal)
        .Where(group => group.Select(x => x.Handle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        .Select(group => group.Key)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Returns a deterministic ERP-only code for a source identity whose supplier SKU cannot be used as ProductCode.</summary>
    public static string CreateInternalProductCode(Guid tenantId, string sourceStore, string handle, string supplierSku)
    {
        var identity = string.Join('\u001f', tenantId.ToString("N"), sourceStore.Trim().ToUpperInvariant(), handle.Trim().ToUpperInvariant(), NormalizeSku(supplierSku));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return "SHP-" + digest[..46]; // Products.ProductCode is limited to 50 characters.
    }
}

public sealed record ShopifyInventorySourceCandidate(string Handle, string VariantSku, Guid ProductId);
public sealed record ShopifyInventoryProductCandidate(Guid ProductId, string ProductCode);
public sealed record ShopifyInventoryProductMatch(string Status, Guid? ProductId)
{
    public bool IsResolved => Status == "MATCHED" && ProductId.HasValue;
}

/// <summary>
/// Resolves against a caller's already tenant- and Source Store-scoped candidates.
/// Exact Handle + SKU identity wins; SKU-only matching is accepted only for one product.
/// </summary>
public static class ShopifyInventoryProductMatcher
{
    public static ShopifyInventoryProductMatch Resolve(string? handle, string? sku,
        IEnumerable<ShopifyInventorySourceCandidate> sourceCandidates,
        IEnumerable<ShopifyInventoryProductCandidate> productCandidates)
    {
        var sources = sourceCandidates.ToArray();
        var products = productCandidates.ToArray();
        var liveProductIds = products.Select(x => x.ProductId).ToHashSet();

        if (!string.IsNullOrWhiteSpace(handle) && !string.IsNullOrWhiteSpace(sku))
        {
            var exact = sources.Where(x => Same(x.Handle, handle) && Same(x.VariantSku, sku)).Select(x => x.ProductId).Distinct().ToArray();
            if (exact.Length > 1) return new("AMBIGUOUS", null);
            if (exact.Length == 1) return liveProductIds.Contains(exact[0])
                ? new("MATCHED", exact[0])
                : new("UNAVAILABLE", null);
        }
        else if (!string.IsNullOrWhiteSpace(handle) && string.IsNullOrWhiteSpace(sku))
        {
            var byHandle = sources.Where(x => Same(x.Handle, handle)).Select(x => x.ProductId).Distinct().ToArray();
            if (byHandle.Length > 1) return new("AMBIGUOUS", null);
            if (byHandle.Length == 1) return liveProductIds.Contains(byHandle[0])
                ? new("MATCHED", byHandle[0])
                : new("UNAVAILABLE", null);
        }

        if (string.IsNullOrWhiteSpace(sku)) return new("NOT_FOUND", null);
        var skuMatches = sources.Where(x => Same(x.VariantSku, sku)).Select(x => x.ProductId)
            .Concat(products.Where(x => Same(x.ProductCode, sku)).Select(x => x.ProductId))
            .Distinct().ToArray();
        if (skuMatches.Length > 1) return new("AMBIGUOUS", null);
        if (skuMatches.Length == 0) return new("NOT_FOUND", null);
        return liveProductIds.Contains(skuMatches[0])
            ? new("MATCHED", skuMatches[0])
            : new("UNAVAILABLE", null);
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
