using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontOrderPresentationTests
{
    [Theory]
    [InlineData("HELD", null, "Order Confirmed")]
    [InlineData("HELD", "READY_FOR_PICKUP", "Packed")]
    [InlineData("HELD", "PICKED_UP", "Out for Delivery")]
    [InlineData("HELD", "OUT_FOR_DELIVERY", "Out for Delivery")]
    [InlineData("COMPLETED", "DELIVERED", "Delivered")]
    [InlineData("HELD", "DELIVERY_FAILED", "Delivery Failed")]
    [InlineData("CANCELLED", "OUT_FOR_DELIVERY", "Cancelled")]
    public void MapsAuthoritativeStatuses(string invoice, string? delivery, string expected)
        => StorefrontOrderPresentation.Status(invoice, delivery).Should().Be(expected);

    [Fact]
    public void CancelledOrderDoesNotShowLaterMilestones()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = StorefrontOrderPresentation.Timeline("CANCELLED", "OUT_FOR_DELIVERY", now, now, now, null, null, now);
        timeline.Select(x => x.Label).Should().Equal("Order Confirmed", "Cancelled");
        timeline.Last().OccurredAt.Should().Be(now);
    }

    [Theory]
    [InlineData(350, 500, 150, 70, false)]
    [InlineData(500, 500, 0, 100, true)]
    [InlineData(650, 500, 0, 100, true)]
    [InlineData(250, 500, 250, 50, false)]
    public void CalculatesThresholdBoundaries(decimal amount, decimal threshold, decimal remaining, int progress, bool unlocked)
    {
        var quote = StorefrontDeliveryProgress.Calculate(amount, threshold);
        quote.RemainingAmount.Should().Be(remaining);
        quote.ProgressPercent.Should().Be(progress);
        quote.IsFreeDeliveryUnlocked.Should().Be(unlocked);
    }

    [Fact]
    public void DisabledThresholdDoesNotAdvertiseFreeDelivery()
    {
        var quote = StorefrontDeliveryProgress.Calculate(350, null);
        quote.FreeDeliveryThreshold.Should().BeNull();
        quote.IsFreeDeliveryUnlocked.Should().BeFalse();
    }
}