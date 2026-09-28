namespace WhatsBiz.Application.Features.Storefront;

public static class StorefrontDeliveryProgress
{
    public static StorefrontCartQuoteDto Calculate(decimal eligibleAmount, decimal? threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleAmount);
        if (threshold is null or <= 0) return new(eligibleAmount, null, 0, 0, false);
        var remaining = Math.Max(0, threshold.Value - eligibleAmount);
        var progress = Math.Clamp((int)decimal.Round(eligibleAmount / threshold.Value * 100, 0), 0, 100);
        return new(eligibleAmount, threshold, remaining, progress, remaining == 0);
    }
}