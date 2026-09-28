using System.Data.Common;

namespace WhatsBiz.Application.Common.Interfaces;

// Transaction is supplied by the orchestrator so the ERP receipt and its
// commerce settlement state commit (or roll back) together.
public sealed record ErpPaymentPostRequest(Guid TenantId, Guid InvoiceId, string MethodCode,
    decimal Amount, string? ReferenceNumber, string? Actor);
public sealed record ErpPaymentPostResult(Guid PaymentId, Guid InvoiceId,
    decimal PaidAmount, decimal BalanceAmount);

public interface IErpPaymentPosting
{
    Task<ErpPaymentPostResult> ApplyAsync(DbConnection connection, DbTransaction transaction,
        ErpPaymentPostRequest request, CancellationToken token);
}