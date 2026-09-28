using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.POS;

// This is an internal ERP boundary. Provider refunds and Commerce cancellation approval
// deliberately do not call it until the separate refund-settlement phase is ready.
public sealed class ErpSaleReversal(IConfiguration configuration) : IErpSaleReversal
{
    public async Task<ErpFullSaleReversalResult> ReverseFullSaleAsync(
        ErpFullSaleReversalRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.InvoiceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 250)
            throw new ArgumentException("A trusted tenant, invoice and reason are required.", nameof(request));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ERP connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var tenantContext = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant", connection))
        {
            tenantContext.Parameters.AddWithValue("@tenant", request.TenantId);
            await tenantContext.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new SqlCommand("sales.POS_ReverseFullSale", connection)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@InvoiceId", request.InvoiceId);
        command.Parameters.AddWithValue("@Reason", request.Reason.Trim());
        command.Parameters.AddWithValue("@CreatedBy", (object?)request.Actor ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("ERP full sale reversal returned no result.");
        return new ErpFullSaleReversalResult(
            reader.GetGuid(reader.GetOrdinal("InvoiceId")),
            reader.GetGuid(reader.GetOrdinal("ReversalId")),
            reader.GetGuid(reader.GetOrdinal("ReversalJournalId")),
            reader.GetGuid(reader.GetOrdinal("InventoryRestorationId")),
            reader.GetDecimal(reader.GetOrdinal("OriginalGrandTotal")),
            reader.GetDecimal(reader.GetOrdinal("CollectedAmount")),
            reader.GetDecimal(reader.GetOrdinal("RefundRequiredAmount")),
            reader.GetBoolean(reader.GetOrdinal("AlreadyReversed")));
    }
}
