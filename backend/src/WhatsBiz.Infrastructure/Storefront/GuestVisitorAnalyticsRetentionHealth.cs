namespace WhatsBiz.Infrastructure.Storefront;

public sealed class GuestVisitorAnalyticsRetentionHealth
{
    private int _healthy;

    public bool IsHealthy => Volatile.Read(ref _healthy) == 1;

    public void MarkHealthy() => Volatile.Write(ref _healthy, 1);

    public void MarkUnhealthy() => Volatile.Write(ref _healthy, 0);
}
