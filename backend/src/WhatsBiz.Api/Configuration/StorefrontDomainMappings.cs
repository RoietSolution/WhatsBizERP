namespace WhatsBiz.Api.Configuration;

/// <summary>Exact, operator-verified public storefront origins mapped to retailer keys.</summary>
public static class StorefrontDomainMappings
{
    public static string? ResolveStoreKey(IConfiguration configuration, string? origin)
    {
        var normalized = NormalizeOrigin(origin);
        if (normalized is null) return null;

        foreach (var mapping in configuration.GetSection("StorefrontDomains:Mappings").GetChildren())
        {
            if (!bool.TryParse(mapping["Verified"], out var verified) || !verified) continue;
            var mappedOrigin = NormalizeOrigin(mapping["Origin"]);
            var storeKey = mapping["StoreKey"]?.Trim();
            if (mappedOrigin is not null && string.Equals(normalized, mappedOrigin, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(storeKey)) return storeKey;
        }
        return null;
    }

    public static string? NormalizeOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return null;
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
