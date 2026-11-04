using FluentAssertions;
using System.Text.Json;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontPricingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
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
    public void Straightforward268ProductWithoutTaxOrDeliveryRemains268()
    {
        var result = Quote(268, new(true, 0m, false, null));
        result.MerchandiseSubtotal.Should().Be(268m);
        result.MerchandiseTaxAmount.Should().Be(0m);
        result.PromotionDiscount.Should().Be(0m);
        result.DeliveryCharge.Should().Be(0m);
        result.FinalPayableAmount.Should().Be(268m);
    }

    [Fact]
    public void CartPreviewWithoutPincodeReturnsBackendKnownAmountsAsAnEstimate()
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 100, 5)], new(true, 40, false, null),
            null, false, [], false, false, Now);

        result.MerchandiseSubtotal.Should().Be(100m);
        result.MerchandiseTaxAmount.Should().Be(5m);
        result.DeliveryCharge.Should().Be(40m);
        result.FinalPayableAmount.Should().Be(145m);
        result.IsPincodeServiceable.Should().BeFalse();
    }

    [Fact]
    public void CartPreviewUsesFixedChargeBelowFreeDeliveryThresholdWithoutPincode()
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 400, 0)], new(true, 40, true, 500),
            null, false, [], false, false, Now);

        result.DeliveryCharge.Should().Be(40m);
        result.FinalPayableAmount.Should().Be(440m);
        result.IsFreeDeliveryUnlocked.Should().BeFalse();
    }

    [Fact]
    public void CartPreviewUnlocksFreeDeliveryAtThresholdWithoutPincode()
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 500, 0)], new(true, 40, true, 500),
            null, false, [], false, false, Now);

        result.DeliveryCharge.Should().Be(0m);
        result.FinalPayableAmount.Should().Be(500m);
        result.IsFreeDeliveryUnlocked.Should().BeTrue();
    }

    [Fact]
    public void CartPreviewOmitsDeliveryWhenTenantDeliveryIsDisabled()
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 400, 0)], new(false, 40, true, 500),
            null, false, [], false, false, Now);

        result.DeliveryCharge.Should().Be(0m);
        result.FinalPayableAmount.Should().Be(400m);
        result.IsDeliveryEnabled.Should().BeFalse();
    }

    [Fact]
    public void ServiceableCheckoutUsesTheSameDeliveryRulesAsPreview()
    {
        var preview = StorefrontPricingPolicy.Calculate([new(1, 400, 0)], new(true, 40, true, 500),
            null, false, [], false, false, Now);
        var checkout = StorefrontPricingPolicy.Calculate([new(1, 400, 0)], new(true, 40, true, 500),
            "841438", true, [], false, false, Now);

        checkout.DeliveryCharge.Should().Be(preview.DeliveryCharge);
        checkout.FinalPayableAmount.Should().Be(preview.FinalPayableAmount);
        checkout.IsPincodeServiceable.Should().BeTrue();
    }

    [Fact]
    public void UnserviceableAddressDoesNotChangeFixedDeliveryPricing()
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 400, 0)], new(true, 40, true, 500),
            "841438", false, [], false, false, Now);

        result.DeliveryCharge.Should().Be(40m);
        result.FinalPayableAmount.Should().Be(440m);
        result.IsPincodeServiceable.Should().BeFalse();
    }

    [Theory]
    [InlineData(400, "INCLUSIVE")]
    [InlineData(400, "EXCLUSIVE")]
    [InlineData(600, "INCLUSIVE")]
    [InlineData(600, "EXCLUSIVE")]
    public void PricingWithPromotionAndGstIsIndependentOfAddress(decimal amount, string mode)
    {
        StorefrontPricedItem[] items = [new(1, amount, 18, amount + 100, mode)];
        StorefrontPromotionCandidate[] offers = [Offer("MINIMUM_PURCHASE", "FLAT", 25, 0)];
        var preview = StorefrontPricingPolicy.Calculate(items, Standard, null, false, offers, false, false, Now);
        foreach (var serviceable in new[] { true, false })
        {
            var checkout = StorefrontPricingPolicy.Calculate(items, Standard, "226001", serviceable, offers, false, false, Now);
            checkout.FinalPayableAmount.Should().Be(preview.FinalPayableAmount);
            checkout.DeliveryCharge.Should().Be(preview.DeliveryCharge);
            checkout.MerchandiseTaxAmount.Should().Be(preview.MerchandiseTaxAmount);
            checkout.PromotionDiscount.Should().Be(25);
            checkout.ProductSavings.Should().Be(100);
            checkout.IsPincodeServiceable.Should().Be(serviceable);
        }
    }

    [Fact]
    public void ChangedExpectedPayableIsRejectedRatherThanSilentlyCharged()
    {
        var reject = () => StorefrontPricingPolicy.EnsureExpectedPayable(118m, 119m);
        reject.Should().Throw<BusinessRuleException>().WithMessage("*total has changed*");
        StorefrontPricingPolicy.EnsureExpectedPayable(118m, 118m);
        StorefrontPricingPolicy.EnsureExpectedPayable(0m, 0m);
        StorefrontPricingPolicy.EnsureExpectedPayable(null, 118m); // Existing callers retain their contract.
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 4.76)]
    [InlineData(12, 10.71)]
    [InlineData(18, 15.25)]
    [InlineData(28, 21.88)]
    public void InclusiveGstIsExtractedWithoutIncreasingCustomerPrice(decimal rate, decimal expectedTax)
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 100, rate, null, "INCLUSIVE")], new(false, 0, false, null),
            null, false, [], false, false, Now);

        result.IncludedTaxAmount.Should().Be(expectedTax);
        result.AdditionalTaxAmount.Should().Be(0m);
        result.MerchandiseTaxAmount.Should().Be(expectedTax);
        result.FinalPayableAmount.Should().Be(100m);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(12, 12)]
    [InlineData(18, 18)]
    [InlineData(28, 28)]
    public void ExclusiveGstIsAddedToCustomerPrice(decimal rate, decimal expectedTax)
    {
        var result = StorefrontPricingPolicy.Calculate([new(1, 100, rate)], new(false, 0, false, null),
            null, false, [], false, false, Now);

        result.IncludedTaxAmount.Should().Be(0m);
        result.AdditionalTaxAmount.Should().Be(expectedTax);
        result.MerchandiseTaxAmount.Should().Be(expectedTax);
        result.FinalPayableAmount.Should().Be(100m + expectedTax);
    }

    [Fact]
    public void MixedGstModesKeepIncludedTaxOutOfTheAdditionalPayableAmount()
    {
        var result = StorefrontPricingPolicy.Calculate(
            [new(1, 118, 18, null, "INCLUSIVE"), new(1, 100, 18, null, "EXCLUSIVE")],
            new(false, 0, false, null), null, false, [], false, false, Now);

        result.MerchandiseSubtotal.Should().Be(218m);
        result.IncludedTaxAmount.Should().Be(18m);
        result.AdditionalTaxAmount.Should().Be(18m);
        result.MerchandiseTaxAmount.Should().Be(36m);
        result.FinalPayableAmount.Should().Be(236m);
    }

    [Fact]
    public void InclusiveGstScalesWithQuantity()
    {
        var result = StorefrontPricingPolicy.Calculate([new(2, 118, 18, null, "INCLUSIVE")], new(false, 0, false, null),
            null, false, [], false, false, Now);

        result.MerchandiseSubtotal.Should().Be(236m);
        result.IncludedTaxAmount.Should().Be(36m);
        result.FinalPayableAmount.Should().Be(236m);
    }

    [Fact]
    public void ProductSavingsAreCalculatedInTheBackendQuote()
    {
        var result = StorefrontPricingPolicy.Calculate([new(2, 80, 0, 100)], new(false, 0, false, null),
            null, false, [], false, false, Now);

        result.ProductSavings.Should().Be(40m);
        result.MerchandiseSubtotal.Should().Be(160m);
        result.FinalPayableAmount.Should().Be(160m);
    }

    [Fact]
    public void ReportedCartPreviewReconcilesSubtotalAndProductSavings()
    {
        var result = StorefrontPricingPolicy.Calculate(
            [new(2, 268m, 0, 400m), new(1, 600m, 0, 600m)],
            new(true, 40m, false, null), null, false, [], false, false, Now);

        result.MerchandiseSubtotal.Should().Be(1136m);
        result.ProductSavings.Should().Be(264m);
        result.MerchandiseTaxAmount.Should().Be(0m);
        result.PromotionDiscount.Should().Be(0m);
        result.FinalPayableAmount.Should().Be(1176m);
    }

    [Theory]
    [InlineData("{\"items\":[{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"quantity\":1}]}")]
    [InlineData("{\"pincode\":\"\",\"items\":[{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"quantity\":1}]}")]
    public void CartQuoteContractAcceptsMissingOrEmptyPincode(string json)
    {
        var input = JsonSerializer.Deserialize<StorefrontCartQuoteInput>(json, WebJsonOptions);

        input.Should().NotBeNull();
        input!.Pincode.Should().BeNullOrEmpty();
        input.Items.Should().ContainSingle();
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
