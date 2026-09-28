using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.POS;

// The existing POS procedure is the ERP owner of the SalesPayment, invoice
// balance, receipt journal, customer ledger, and cash/bank book posting.
public sealed class ErpPaymentPosting : IErpPaymentPosting
{
    public async Task<ErpPaymentPostResult> ApplyAsync(DbConnection connection, DbTransaction transaction,
        ErpPaymentPostRequest request, CancellationToken token)
    {
        if (request.TenantId == Guid.Empty || request.InvoiceId == Guid.Empty || request.Amount <= 0)
            throw new ArgumentException("An authoritative tenant, invoice, and positive amount are required.", nameof(request));
        if (connection is not SqlConnection sqlConnection || transaction is not SqlTransaction sqlTransaction
            || sqlTransaction.Connection != sqlConnection)
            throw new ArgumentException("The ERP payment requires the active SQL transaction.", nameof(transaction));

        await using var command = new SqlCommand("sales.POS_AddPayment", sqlConnection, sqlTransaction)
            { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@InvoiceId", request.InvoiceId);
        command.Parameters.AddWithValue("@MethodCode", request.MethodCode);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        command.Parameters.AddWithValue("@ReferenceNumber", (object?)request.ReferenceNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedBy", (object?)request.Actor ?? DBNull.Value);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("ERP payment posting returned no result.");
        return new(reader.GetGuid(reader.GetOrdinal("PaymentId")),
            reader.GetGuid(reader.GetOrdinal("InvoiceId")),
            reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
            reader.GetDecimal(reader.GetOrdinal("BalanceAmount")));
    }
}