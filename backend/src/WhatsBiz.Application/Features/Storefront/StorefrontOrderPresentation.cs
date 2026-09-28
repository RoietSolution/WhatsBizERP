namespace WhatsBiz.Application.Features.Storefront;

public static class StorefrontOrderPresentation
{
    public static string Status(string invoiceStatus, string? deliveryStatus) => (deliveryStatus?.ToUpperInvariant(), invoiceStatus.ToUpperInvariant()) switch
    {
        (_, "CANCELLED") or (_, "VOID") => "Cancelled",
        ("DELIVERED", _) => "Delivered",
        ("DELIVERY_FAILED", _) => "Delivery Failed",
        ("CANCELLED", _) => "Cancelled",
        ("PICKED_UP", _) or ("OUT_FOR_DELIVERY", _) => "Out for Delivery",
        ("READY_FOR_PICKUP", _) => "Packed",
        _ => "Order Confirmed"
    };

    public static IReadOnlyCollection<StorefrontOrderMilestoneDto> Timeline(string invoiceStatus, string? deliveryStatus,
        DateTimeOffset confirmedAt, DateTimeOffset? packedAt, DateTimeOffset? outForDeliveryAt, DateTimeOffset? deliveredAt,
        DateTimeOffset? failedAt, DateTimeOffset? cancelledAt)
    {
        var status = Status(invoiceStatus, deliveryStatus);
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