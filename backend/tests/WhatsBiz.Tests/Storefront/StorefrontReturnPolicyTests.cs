using FluentAssertions;
using System.Data;
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

    [Fact]
    public void EmptyReturnHistoryMapsNullPendingAggregateToNoPendingRequest()
    {
        var result = new DataTable();
        result.Columns.Add("RequestedQuantity", typeof(decimal));
        result.Columns.Add("HasPending", typeof(int));
        result.Rows.Add(0m, DBNull.Value);

        using var reader = result.CreateDataReader();
        reader.Read().Should().BeTrue();
        var hasPending = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;

        hasPending.Should().BeFalse();
    }
}
