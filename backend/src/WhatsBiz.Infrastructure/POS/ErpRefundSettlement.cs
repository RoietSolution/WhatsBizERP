using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.POS;

public sealed class ErpRefundSettlement(IConfiguration configuration) : IErpRefundSettlement
{
    public async Task<ErpRefundSettlementResult> SettleAsync(ErpRefundSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.RefundId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SettlementMode) ||
            string.IsNullOrWhiteSpace(request.ExternalReference) ||
            request.ExternalReference.Length > 100)
            throw new ArgumentException("Trusted refund settlement details are required.", nameof(request));
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ERP connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var context = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant", connection))
        {
            context.Parameters.AddWithValue("@tenant", request.TenantId);
            await context.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new SqlCommand("finance.SettleStorefrontRefund", connection)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@RefundId", request.RefundId);
        command.Parameters.AddWithValue("@SettlementMode", request.SettlementMode);
        command.Parameters.AddWithValue("@ExternalReference", request.ExternalReference.Trim());
        command.Parameters.AddWithValue("@SettledAt", request.SettledAt);
        command.Parameters.AddWithValue("@CreatedBy", (object?)request.Actor ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("ERP refund settlement returned no result.");
        return new ErpRefundSettlementResult(reader.GetGuid(0), reader.GetGuid(1),
            reader.GetDecimal(2), reader.GetString(3), reader.GetBoolean(4));
    }
}
