namespace WhatsBiz.Application.Common.Interfaces;

public sealed record ErpRefundSettlementRequest(Guid TenantId, Guid RefundId, string SettlementMode,
    string ExternalReference, DateTimeOffset SettledAt, string? Actor);

public sealed record ErpRefundSettlementResult(Guid RefundId, Guid JournalEntryId,
    decimal Amount, string SettlementMode, bool AlreadySettled);

// Commerce confirms money movement; ERP validates and posts its financial effect.
public interface IErpRefundSettlement
{
    Task<ErpRefundSettlementResult> SettleAsync(ErpRefundSettlementRequest request,
        CancellationToken cancellationToken);
}
