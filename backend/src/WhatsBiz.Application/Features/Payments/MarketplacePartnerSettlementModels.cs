namespace WhatsBiz.Application.Features.Payments;

public sealed record MarketplacePartnerSettlementDto(
    Guid SettlementEntryId, Guid OrderId, string OrderNumber, string InvoiceStatus,
    Guid AllocationId, Guid FulfilmentGroupId, short GroupNumber, Guid PartnerSellerId,
    string SellerName, Guid ProductId, string ProductName, decimal AllocatedQuantity,
    Guid? AgreementId, int? AgreementVersion, string? CalculationBasis,
    decimal GrossAmount, decimal CommissionAmount, decimal SellerPayable,
    string EntryType, string SettlementStatus, string? AdjustmentDirection,
    string? ReviewReason, string? CalculationSnapshotJson,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? ApprovedAt,
    string? ApprovedBy, DateTimeOffset? SettledAt, string? SettledBy, string? PaymentReference,
    Guid WarehouseId, string WarehouseName, IReadOnlyCollection<MarketplacePartnerSettlementEventDto> Events);

public sealed record MarketplacePartnerSettlementEventDto(string? FromStatus, string ToStatus,
    string Actor, string? Reason, string? PaymentReference, DateTimeOffset OccurredAt);

public sealed record ReviewMarketplacePartnerSettlementInput(bool Approve, string Reason,
    decimal? GrossAmount = null, decimal? CommissionAmount = null, decimal? SellerPayable = null);

public sealed record MarkMarketplacePartnerSettlementPaidInput(string PaymentReference,
    DateTimeOffset SettledAt);

public interface IMarketplacePartnerSettlementService
{
    Task<IReadOnlyCollection<MarketplacePartnerSettlementDto>> ListAsync(CancellationToken token);
    Task<MarketplacePartnerSettlementDto> ReviewAsync(Guid settlementEntryId,
        ReviewMarketplacePartnerSettlementInput input, string actor, CancellationToken token);
    Task<MarketplacePartnerSettlementDto> MarkPaidAsync(Guid settlementEntryId,
        MarkMarketplacePartnerSettlementPaidInput input, string actor, CancellationToken token);
}

public sealed record SettlementAllocationShare(Guid AllocationId, decimal Quantity);
public sealed record SettlementAllocationAmount(Guid AllocationId, decimal GrossAmount,
    decimal CommissionAmount, decimal SellerPayable);

public static class MarketplacePartnerSettlementPolicy
{
    public static bool CanCalculateAutomatically(string? commissionType, decimal commissionValue,
        string? calculationBasis, decimal itemDiscount, decimal sharedDiscount, decimal deliveryCharge,
        string? paymentFeeResponsibility, out string? reviewReason)
    {
        reviewReason = null;
        if (commissionType != "PERCENT" || commissionValue is < 0 or > 100)
            reviewReason = "The agreement commission is not an unambiguous percentage calculation.";
        else if (calculationBasis is not ("ITEM_GROSS" or "ITEM_TAXABLE"))
            reviewReason = "The agreement calculation basis is missing or requires financial interpretation.";
        else if (itemDiscount > 0 || sharedDiscount > 0)
            reviewReason = "Customer discounts require an approved supplier/platform allocation rule.";
        else if (deliveryCharge > 0)
            reviewReason = "Delivery charges require an approved supplier/platform allocation rule.";
        else if (!string.Equals(paymentFeeResponsibility, "PLATFORM", StringComparison.OrdinalIgnoreCase))
            reviewReason = "Payment-fee responsibility has no verified fee calculation for this agreement.";
        return reviewReason is null;
    }

    public static IReadOnlyCollection<SettlementAllocationAmount> Apportion(
        decimal basisAmount, decimal commissionPercentage, IEnumerable<SettlementAllocationShare> source)
    {
        if (basisAmount < 0 || commissionPercentage is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(basisAmount));
        var shares = source.Where(x => x.Quantity > 0).OrderBy(x => x.AllocationId).ToArray();
        var totalQuantity = shares.Sum(x => x.Quantity);
        if (shares.Length == 0 || totalQuantity <= 0)
            throw new ArgumentException("At least one positive allocation quantity is required.", nameof(source));

        var totalCommission = decimal.Round(basisAmount * commissionPercentage / 100m, 2, MidpointRounding.AwayFromZero);
        var grossParts = new decimal[shares.Length];
        var commissionParts = new decimal[shares.Length];
        decimal grossRemaining = basisAmount;
        decimal commissionRemaining = totalCommission;
        for (var i = 0; i < shares.Length; i++)
        {
            if (i == shares.Length - 1)
            {
                grossParts[i] = grossRemaining;
                commissionParts[i] = commissionRemaining;
            }
            else
            {
                grossParts[i] = decimal.Round(basisAmount * shares[i].Quantity / totalQuantity, 2, MidpointRounding.AwayFromZero);
                commissionParts[i] = decimal.Round(totalCommission * shares[i].Quantity / totalQuantity, 2, MidpointRounding.AwayFromZero);
                grossRemaining -= grossParts[i];
                commissionRemaining -= commissionParts[i];
            }
            if (grossParts[i] < 0 || commissionParts[i] < 0 || commissionParts[i] > grossParts[i])
                throw new InvalidOperationException("The supplier settlement could not be apportioned without a negative payable.");
        }
        return shares.Select((share, i) => new SettlementAllocationAmount(share.AllocationId,
            grossParts[i], commissionParts[i], grossParts[i] - commissionParts[i])).ToArray();
    }
}
