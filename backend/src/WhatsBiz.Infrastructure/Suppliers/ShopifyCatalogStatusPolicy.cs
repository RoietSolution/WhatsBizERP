namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyCatalogStatusPolicy
{
    public static bool? Parse(string? status, out string? error)
    {
        error = null;
        switch (status?.Trim().ToLowerInvariant())
        {
            case "active": return true;
            case "inactive":
            case "draft":
            case "archived": return false;
            case null:
            case "":
                error = "Shopify Status is required (active, inactive, draft, or archived).";
                return null;
            default:
                error = $"Shopify Status '{status.Trim()}' is unsupported; use active, inactive, draft, or archived.";
                return null;
        }
    }

    public static bool ResolveForPersistence(bool? proposedStatus, bool isNewProduct, bool currentStatus, bool synchronizeExisting)
        => proposedStatus.HasValue && (isNewProduct || synchronizeExisting) ? proposedStatus.Value : currentStatus;
}
