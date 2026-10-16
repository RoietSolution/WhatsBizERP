namespace WhatsBiz.Application.Features.Storefront;

public static class StorefrontOrderPresentation
{
    public static bool CanPrintShippingLabel(string invoiceStatus, string? paymentStatus, string? paymentMethod, out string reason)
        => CanPrintShippingLabel(invoiceStatus, paymentStatus, paymentMethod, null, out reason);

    public static bool CanPrintShippingLabel(string invoiceStatus, string? paymentStatus, string? paymentMethod, string? deliveryStatus, out string reason)
    {
        var invoice = invoiceStatus?.Trim().ToUpperInvariant();
        var delivery = deliveryStatus?.Trim().ToUpperInvariant();
        var method = paymentMethod?.Trim().ToUpperInvariant();
        var payment = paymentStatus?.Trim().ToUpperInvariant();
        if (invoice is "CANCELLED" or "VOID" or "RETURNED" || delivery is "CANCELLED" or "FAILED")
        {
            reason = "Shipping label unavailable: this order is cancelled or no longer eligible for fulfillment.";
            return false;
        }
        if (method == "COD")
        {
            reason = string.Empty;
            return true;
        }
        if (payment is "PAID" or "CAPTURED")
        {
            reason = string.Empty;
            return true;
        }
        reason = payment is "FAILED" or "CANCELLED"
            ? "Shipping label unavailable: online payment failed."
            : "Shipping label unavailable: online payment has not been confirmed.";
        return false;
    }

    public static string PaymentStatus(string status, string method) => (status?.ToUpperInvariant(), method?.ToUpperInvariant()) switch
    {
        ("PAID", _) or ("CAPTURED", _) => "Payment Successful",
        ("PAYMENT_FAILED", _) or ("FAILED", _) => "Payment Unsuccessful",
        ("REFUND_PENDING", _) => "Refund in Progress",
        ("REFUNDED", _) => "Refund Completed",
        (_, "COD") => "Cash on Delivery — Pay on delivery",
        ("PAYMENT_PENDING", _) or ("PENDING", _) => "Awaiting Payment",
        _ => "Payment status unavailable"
    };
    public static string Status(string invoiceStatus, string? deliveryStatus) => Status(invoiceStatus, deliveryStatus, null, null);
    public static string Status(string invoiceStatus, string? deliveryStatus, string? paymentStatus, string? paymentMethod) => (deliveryStatus?.ToUpperInvariant(), invoiceStatus.ToUpperInvariant(), paymentStatus?.ToUpperInvariant(), paymentMethod?.ToUpperInvariant()) switch
    {
        (_, "CANCELLED", _, _) or (_, "VOID", _, _) => "Cancelled",
        (_, _, "FAILED" or "CANCELLED", var method) when method is not "COD" => "Payment Failed",
        (_, _, "PENDING" or "PENDING_VERIFICATION", var method) when method is not "COD" => "Payment Pending",
        ("DELIVERED", _, _, _) => "Delivered",
        ("DELIVERY_FAILED", _, _, _) => "Delivery Failed",
        ("CANCELLED", _, _, _) => "Cancelled",
        ("PICKED_UP", _, _, _) or ("OUT_FOR_DELIVERY", _, _, _) => "Out for Delivery",
        ("READY_FOR_PICKUP", _, _, _) => "Packed",
        _ => "Order Confirmed"
    };

    public static IReadOnlyCollection<StorefrontOrderMilestoneDto> Timeline(string invoiceStatus, string? deliveryStatus,
        DateTimeOffset confirmedAt, DateTimeOffset? packedAt, DateTimeOffset? outForDeliveryAt, DateTimeOffset? deliveredAt,
        DateTimeOffset? failedAt, DateTimeOffset? cancelledAt)
        => Timeline(invoiceStatus, deliveryStatus, null, null, confirmedAt, packedAt, outForDeliveryAt, deliveredAt, failedAt, cancelledAt);

    public static IReadOnlyCollection<StorefrontOrderMilestoneDto> Timeline(string invoiceStatus, string? deliveryStatus,
        string? paymentStatus, string? paymentMethod, DateTimeOffset confirmedAt, DateTimeOffset? packedAt,
        DateTimeOffset? outForDeliveryAt, DateTimeOffset? deliveredAt, DateTimeOffset? failedAt, DateTimeOffset? cancelledAt)
    {
        var status = Status(invoiceStatus, deliveryStatus, paymentStatus, paymentMethod);
        if (status is "Payment Failed" or "Payment Pending")
            return [new("PAYMENT", status, status == "Payment Failed" ? "exception" : "current", null)];
        if (status is "Cancelled" or "Delivery Failed")
            return
            [
                new("CONFIRMED", "Order Confirmed", "complete", confirmedAt),
                new(status == "Cancelled" ? "CANCELLED" : "FAILED", status, "exception", status == "Cancelled" ? cancelledAt : failedAt)
            ];
        var rank = status switch { "Packed" => 1, "Out for Delivery" => 2, "Delivered" => 3, _ => 0 };
        return
        [
            new("CONFIRMED", "Order Confirmed", rank == 0 ? "current" : "complete", confirmedAt),
            new("PACKED", "Packed", rank > 1 ? "complete" : rank == 1 ? "current" : "pending", packedAt),
            new("OUT_FOR_DELIVERY", "Out for Delivery", rank > 2 ? "complete" : rank == 2 ? "current" : "pending", outForDeliveryAt),
            new("DELIVERED", "Delivered", rank == 3 ? "complete" : "pending", deliveredAt)
        ];
    }
}
