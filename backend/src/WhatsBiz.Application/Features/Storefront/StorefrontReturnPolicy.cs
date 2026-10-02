namespace WhatsBiz.Application.Features.Storefront;

public sealed record ResolvedStorefrontReturnPolicy(bool IsReturnable, int? EligibleDays, DateTimeOffset? Deadline);

public static class StorefrontReturnPolicy
{
    public static ResolvedStorefrontReturnPolicy Resolve(string mode, int? customDays, int defaultDays, DateTimeOffset? saleOrDeliveryDate = null)
    {
        var normalized = mode?.Trim().ToUpperInvariant();
        if (normalized == "NON_RETURNABLE") return new(false, null, null);
        var days = normalized == "CUSTOM" ? customDays : defaultDays;
        if (days is null or < 0) return new(false, null, null);
        return new(true, days, saleOrDeliveryDate?.AddDays(days.Value));
    }
}
