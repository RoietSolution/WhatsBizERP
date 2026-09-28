namespace WhatsBiz.Application.Common.Interfaces;

public sealed record ErpFullSaleReversalRequest(Guid TenantId, Guid InvoiceId, string Reason, string? Actor);

public sealed record ErpFullSaleReversalResult(Guid InvoiceId, Guid ReversalId,
    Guid ReversalJournalId, Guid InventoryRestorationId, decimal OriginalGrandTotal,
    decimal CollectedAmount, decimal RefundRequiredAmount, bool AlreadyReversed)
{
    public bool RefundRequired => RefundRequiredAmount > 0;
    public string RefundStatus => RefundRequired ? "REFUND_REQUIRED" : "NONE";
}

// ERP owns the local sale, finance, and inventory transaction. No provider calls occur here.
public interface IErpSaleReversal
{
    Task<ErpFullSaleReversalResult> ReverseFullSaleAsync(ErpFullSaleReversalRequest request,
        CancellationToken cancellationToken);
}
