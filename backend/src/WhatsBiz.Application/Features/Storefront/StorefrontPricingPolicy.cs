namespace WhatsBiz.Application.Features.Storefront;

public sealed record StorefrontPricingConfiguration(bool DeliveryEnabled, decimal StandardDeliveryCharge,
    bool FreeDeliveryEnabled, decimal? FreeDeliveryThreshold);
public sealed record StorefrontPricedItem(decimal Quantity, decimal UnitPrice, decimal TaxPercentage);
public sealed record StorefrontPromotionCandidate(Guid PromotionId, string OfferName, string OfferType,
    decimal MinimumPurchaseAmount, string DiscountType, decimal DiscountValue, decimal? MaximumDiscount,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool IsActive, int? UsageLimitPerCustomer,
    int PreviousUses = 0, string? PromoCode = null);

public static class StorefrontPricingPolicy
{
    // V1 invoice-level offer is a post-GST bill discount. Delivery is untaxed and excluded from merchandise.
    // One best offer applies. The free-delivery threshold uses merchandise payable after that offer.
    public static StorefrontCartQuoteDto Calculate(IReadOnlyCollection<StorefrontPricedItem> items,
        StorefrontPricingConfiguration configuration, string? pincode, bool pincodeServiceable,
        IReadOnlyCollection<StorefrontPromotionCandidate> promotions, bool verifiedCustomer, bool hasQualifyingOrder,
        DateTimeOffset now, string? promotionCode = null)
    {
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || x.TaxPercentage < 0))
            throw new ArgumentException("Cart items are invalid.", nameof(items));
        var subtotal = decimal.Round(items.Sum(x => x.Quantity * x.UnitPrice), 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round(items.Sum(x => x.Quantity * x.UnitPrice * x.TaxPercentage / 100m), 2, MidpointRounding.AwayFromZero);
        var merchandise = subtotal + tax;
        var validPincode = pincode?.Length == 6 && pincode.All(char.IsAsciiDigit);
        var eligible = promotions.Where(x => (promotionCode is null || string.Equals(x.PromoCode, promotionCode, StringComparison.OrdinalIgnoreCase)) && x.IsActive && x.MinimumPurchaseAmount <= merchandise
            && (x.StartsAt is null || x.StartsAt <= now) && (x.EndsAt is null || x.EndsAt > now)
            && (x.OfferType == "MINIMUM_PURCHASE" || x.OfferType == "FIRST_ORDER" && verifiedCustomer && !hasQualifyingOrder)
            && (x.UsageLimitPerCustomer is null || verifiedCustomer && x.PreviousUses < x.UsageLimitPerCustomer.Value))
            .Select(x => new { Offer = x, Discount = Discount(x, merchandise, subtotal) })
            .Where(x => x.Discount > 0)
            .OrderByDescending(x => x.Discount).ThenBy(x => x.Offer.PromotionId).FirstOrDefault();
        var discount = eligible?.Discount ?? 0;
        var afterPromotion = merchandise - discount;
        var threshold = configuration.FreeDeliveryEnabled && configuration.FreeDeliveryThreshold is > 0
            ? configuration.FreeDeliveryThreshold : null;
        var serviceable = validPincode && pincodeServiceable;
        var unlocked = configuration.DeliveryEnabled && threshold is not null && afterPromotion >= threshold.Value;
        var remaining = threshold is null ? 0 : Math.Max(0, threshold.Value - afterPromotion);
        var percent = threshold is null ? 0 : Math.Clamp((int)decimal.Round(afterPromotion / threshold.Value * 100m, 0), 0, 100);
        var delivery = configuration.DeliveryEnabled && serviceable && !unlocked ? configuration.StandardDeliveryCharge : 0;
        return new(afterPromotion, threshold, remaining, percent, unlocked,
            configuration.DeliveryEnabled, serviceable, merchandise, tax, configuration.StandardDeliveryCharge,
            configuration.FreeDeliveryEnabled, delivery, discount, eligible?.Offer.OfferName, eligible?.Offer.PromotionId,
            afterPromotion + delivery, subtotal, eligible?.Offer.PromoCode);
    }

    private static decimal Discount(StorefrontPromotionCandidate offer, decimal merchandise, decimal merchandiseBeforeTax)
    {
        var raw = offer.DiscountType == "PERCENTAGE"
            ? decimal.Round(merchandise * offer.DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : offer.DiscountValue;
        return Math.Min(merchandiseBeforeTax, offer.MaximumDiscount is > 0 ? Math.Min(raw, offer.MaximumDiscount.Value) : raw);
    }
}