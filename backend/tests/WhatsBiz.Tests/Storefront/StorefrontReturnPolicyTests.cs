using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontReturnPolicyTests
{
    [Fact]
    public void CustomPolicyUsesProductDays()
    {
        var policy = StorefrontReturnPolicy.Resolve("CUSTOM", 7, 30, new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));
        policy.IsReturnable.Should().BeTrue();
        policy.EligibleDays.Should().Be(7);
        policy.Deadline.Should().Be(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NonReturnableOverridesTenantDefault()
    {
        var policy = StorefrontReturnPolicy.Resolve("NON_RETURNABLE", null, 30, DateTimeOffset.UtcNow);
        policy.IsReturnable.Should().BeFalse();
        policy.EligibleDays.Should().BeNull();
    }

    [Fact]
    public void ZeroDaysMeansDeliveryDateOnly()
    {
        var delivery = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        StorefrontReturnPolicy.Resolve("CUSTOM", 0, 30, delivery).Deadline.Should().Be(delivery);
    }
}
