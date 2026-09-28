namespace WhatsBiz.Application.Features.Storefront;

// Refunds are bounded by the committed invoice and actual collections.
// Reserved includes pending provider refunds as well as completed refunds.
public static class StorefrontCancellationPolicy
{
    public static decimal AvailableRefund(decimal committedTotal, decimal collected, decimal reserved)
        => Math.Max(0m, Math.Min(Math.Max(0m, committedTotal),
            Math.Max(0m, collected) - Math.Max(0m, reserved)));

    public static bool CanCancelWithoutFinancialReversal(
        string invoiceStatus, decimal paidAmount, decimal collectedAmount,
        string? paymentProvider, string? deliveryStatus, bool hasReturns, bool hasPendingOnlinePayment)
        => invoiceStatus is "HELD" or "SUSPENDED"
            && paidAmount == 0m && collectedAmount == 0m
            && paymentProvider is "COD" or null
            && deliveryStatus is not ("DELIVERED" or "RETURNED" or "CANCELLED")
            && !hasReturns && !hasPendingOnlinePayment;
}