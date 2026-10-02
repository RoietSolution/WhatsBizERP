using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontPricingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly StorefrontPricingConfiguration Standard = new(true, 40m, true, 500m);
    private static StorefrontPricedItem Item(decimal amount) => new(1, amount, 0);
    private static StorefrontCartQuoteDto Quote(decimal amount, StorefrontPricingConfiguration? config = null,
        StorefrontPromotionCandidate[]? offers = null, bool verified = false, bool previousOrder = false,
        bool serviceable = true, string pincode = "226001")
        => StorefrontPricingPolicy.Calculate([Item(amount)], config ?? Standard, pincode, serviceable,
            offers ?? [], verified, previousOrder, Now);

    [Fact]
    public void CaseAPaidDeliveryAppearsOnceInFinalPayable()
    {
        var result = Quote(400);
        result.MerchandiseAmount.Should().Be(400);
        result.DeliveryCharge.Should().Be(40);
        result.FinalPayableAmount.Should().Be(440);
        result.MerchandiseTaxAmount.Should().Be(0);
    }

    [Fact]
    public void CaseBPromotionIsBeforeDeliveryAndIndependentOfIt()
    {
        var offer = Offer("MINIMUM_PURCHASE", "FLAT", 50, 1000);
        var result = Quote(1000, new(true, 40, false, null), [offer]);
        result.PromotionDiscount.Should().Be(50);
        result.DeliveryCharge.Should().Be(40);
        result.FinalPayableAmount.Should().Be(990);
    }

    [Theory]
    [InlineData(350, 150, 40, 390)]
    [InlineData(500, 0, 0, 500)]
    [InlineData(650, 0, 0, 650)]
    [InlineData(250, 250, 40, 290)]
    public void FreeDeliveryBoundaries(decimal merchandise, decimal remaining, decimal fee, decimal final)
    {
        var result = Quote(merchandise);
        result.RemainingAmount.Should().Be(remaining);
        result.DeliveryCharge.Should().Be(fee);
        result.FinalPayableAmount.Should().Be(final);
    }

    [Fact]
    public void DisabledDeliveryOrFreeDeliveryIsExplicit()
    {
        Quote(400, new(false, 40, true, 500)).IsDeliveryEnabled.Should().BeFalse();
        var noFree = Quote(600, new(true, 40, false, 500));
        noFree.FreeDeliveryThreshold.Should().BeNull();
        noFree.DeliveryCharge.Should().Be(40);
        Quote(400, new(true, 0, false, null)).DeliveryCharge.Should().Be(0);
    }

    [Fact]
    public void UnsupportedAndInvalidPincodeCannotBecomeServiceable()
    {
        Quote(400, serviceable: false).IsPincodeServiceable.Should().BeFalse();
        Quote(400, pincode: "22601").IsPincodeServiceable.Should().BeFalse();
        Quote(400, pincode: "2260AB").IsPincodeServiceable.Should().BeFalse();
    }

    [Fact]
    public void PercentageOfferHonoursMaximumCapAndMinimumExcludesDelivery()
    {
        var offer = Offer("MINIMUM_PURCHASE", "PERCENTAGE", 10, 1000, maximum: 100);
        Quote(960, new(true, 40, false, null), [offer]).PromotionDiscount.Should().Be(0);
        var result = Quote(1200, new(true, 40, false, null), [offer]);
        result.PromotionDiscount.Should().Be(100);
        result.FinalPayableAmount.Should().Be(1140);
    }

    [Fact]
    public void PromotionReducesFreeDeliveryEligibleAmountWithoutReducingGst()
    {
        var offer = Offer("MINIMUM_PURCHASE", "FLAT", 50, 500);
        var result = Quote(520, Standard, [offer]);
        result.EligibleAmount.Should().Be(470);
        result.DeliveryCharge.Should().Be(40);
        result.FinalPayableAmount.Should().Be(510);
    }

    [Fact]
    public void FirstOrderRequiresVerifiedCustomerWithoutQualifyingOrderOrPriorUse()
    {
        var offer = Offer("FIRST_ORDER", "FLAT", 50, 500, limit: 1);
        Quote(600, offers: [offer]).PromotionDiscount.Should().Be(0);
        Quote(600, offers: [offer], verified: true).PromotionDiscount.Should().Be(50);
        Quote(600, offers: [offer], verified: true, previousOrder: true).PromotionDiscount.Should().Be(0);
        Quote(600, offers: [offer with { PreviousUses = 1 }], verified: true).PromotionDiscount.Should().Be(0);
    }

    [Fact]
    public void InactiveFutureAndExpiredOffersAreExcluded()
    {
        var offer = Offer("MINIMUM_PURCHASE", "FLAT", 50, 0);
        Quote(600, offers: [offer with { IsActive = false }]).PromotionDiscount.Should().Be(0);
        Quote(600, offers: [offer with { StartsAt = Now.AddDays(1) }]).PromotionDiscount.Should().Be(0);
        Quote(600, offers: [offer with { EndsAt = Now.AddSeconds(-1) }]).PromotionDiscount.Should().Be(0);
    }

    [Fact]
    public void ProductTaxRemainsMerchandiseTaxAndDeliveryTaxIsNotCalculated()
    {
        var quote = StorefrontPricingPolicy.Calculate([new(1, 100, 5)], new(true, 40, false, null),
            "226001", true, [], false, false, Now);
        quote.MerchandiseTaxAmount.Should().Be(5);
        quote.MerchandiseAmount.Should().Be(105);
        quote.DeliveryCharge.Should().Be(40);
        quote.FinalPayableAmount.Should().Be(145);
    }

    [Fact]
    public void AuthoritativeSubtotalTaxDiscountDeliveryComponentsReconcileToPayable()
    {
        var offer=Offer("MINIMUM_PURCHASE","FLAT",5,0);
        var quote=StorefrontPricingPolicy.Calculate([new(1,112m,6.5625m)],new(true,0,false,null),
            "226001",true,[offer],false,false,Now);
        var subtotal=quote.MerchandiseAmount-quote.MerchandiseTaxAmount;
        subtotal.Should().Be(112m);
        quote.MerchandiseSubtotal.Should().Be(112m);
        quote.MerchandiseTaxAmount.Should().Be(7.35m);
        (subtotal+quote.MerchandiseTaxAmount-quote.PromotionDiscount+quote.DeliveryCharge).Should().Be(quote.FinalPayableAmount);
        quote.FinalPayableAmount.Should().Be(114.35m);
    }

    [Fact]
    public void PromotionCannotConsumeUnchangedProductGst()
    {
        var offer = Offer("MINIMUM_PURCHASE", "FLAT", 105, 0);
        var result = StorefrontPricingPolicy.Calculate([new(1, 100, 5)], new(true, 0, false, null),
            "226001", true, [offer], false, false, Now);
        result.PromotionDiscount.Should().Be(100);
        result.MerchandiseTaxAmount.Should().Be(5);
        result.FinalPayableAmount.Should().Be(5);
    }
    private static StorefrontPromotionCandidate Offer(string type, string discountType, decimal discount,
        decimal minimum, decimal? maximum = null, int? limit = null)
        => new(Guid.NewGuid(), "Test offer", type, minimum, discountType, discount, maximum,
            null, null, true, limit);
}