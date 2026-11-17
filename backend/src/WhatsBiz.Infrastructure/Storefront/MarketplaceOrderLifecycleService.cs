using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class MarketplaceOrderLifecycleService(
    SqlIdempotencyExecutor idempotency,
    IConfiguration configuration) : IMarketplaceOrderLifecycleService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public Task<MarketplaceOrderLifecycleResult> CompleteAsync(
        Guid tenantId, Guid orderId, string idempotencyKey, string? actor, CancellationToken token)
        => ExecuteOrderOperation(tenantId, orderId, idempotencyKey, actor, "MARKETPLACE_COMPLETE",
            "commerce.Marketplace_CompleteOrder", null, token);

    public Task<MarketplaceOrderLifecycleResult> CancelAsync(
        Guid tenantId, Guid orderId, string idempotencyKey, string? actor, CancellationToken token)
        => ExecuteOrderOperation(tenantId, orderId, idempotencyKey, actor, "MARKETPLACE_CANCEL",
            "commerce.Marketplace_ReleaseOrder", "CANCEL", token);

    private async Task<MarketplaceOrderLifecycleResult> ExecuteOrderOperation(Guid tenantId, Guid orderId,
        string idempotencyKey, string? actor, string operation, string procedure, string? releaseReason,
        CancellationToken token)
    {
        var key = idempotencyKey?.Trim();
        if (tenantId == Guid.Empty || orderId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 160)
            throw new BusinessRuleException("A tenant, marketplace order and valid idempotency key are required.");
        var request = new LifecycleRequest(tenantId, orderId, releaseReason);
        try
        {
            return await idempotency.ExecuteForTenant(key, operation, request, actor, tenantId,
                async (connection, transaction, ct) =>
                {
                    await using var command = new SqlCommand(procedure, connection, transaction)
                        { CommandType = CommandType.StoredProcedure };
                    command.Parameters.AddWithValue("@TenantId", tenantId);
                    command.Parameters.AddWithValue("@OrderId", orderId);
                    if (releaseReason is not null) command.Parameters.AddWithValue("@ReleaseReason", releaseReason);
                    command.Parameters.AddWithValue("@ModifiedBy", (object?)actor ?? DBNull.Value);
                    return await ReadResult(command, ct);
                }, token);
        }
        catch (SqlException exception) when (exception.Number >= 87000)
        {
            throw new BusinessRuleException(exception.Message);
        }
    }

    public async Task<int> ExpireDueBatchAsync(Guid tenantId, int batchSize, string? actor, CancellationToken token)
    {
        if (tenantId == Guid.Empty) throw new BusinessRuleException("A tenant context is required.");
        if (batchSize is < 1 or > 500) throw new BusinessRuleException("The expiry batch size must be between 1 and 500.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using (var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection))
        {
            context.Parameters.AddWithValue("@tenant", tenantId);
            await context.ExecuteNonQueryAsync(token);
        }
        await using var command = new SqlCommand("commerce.Marketplace_ExpireReservationBatch", connection)
            { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BatchSize", batchSize);
        command.Parameters.AddWithValue("@ModifiedBy", (object?)actor ?? "MARKETPLACE_EXPIRATION_WORKER");
        try
        {
            // Marketplace_ExpireReservationBatch executes Marketplace_ReleaseOrder for each
            // expired order. That procedure returns its own result set before the batch returns
            // ExpiredOrderCount, so ExecuteScalar would read an OrderId instead of the count.
            await using var reader = await command.ExecuteReaderAsync(token);
            do
            {
                var countOrdinal = -1;
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (string.Equals(reader.GetName(ordinal), "ExpiredOrderCount", StringComparison.OrdinalIgnoreCase))
                    {
                        countOrdinal = ordinal;
                        break;
                    }
                }

                if (countOrdinal >= 0 && await reader.ReadAsync(token))
                    return Convert.ToInt32(reader.GetValue(countOrdinal), System.Globalization.CultureInfo.InvariantCulture);
            }
            while (await reader.NextResultAsync(token));

            return 0;
        }
        catch (SqlException exception) when (exception.Number >= 87000)
        {
            throw new BusinessRuleException(exception.Message);
        }
    }

    public async Task<int> ExpireDueAcrossTenantsAsync(int batchSize, string? actor, CancellationToken token)
    {
        if (batchSize is < 1 or > 500) throw new BusinessRuleException("The expiry batch size must be between 1 and 500.");
        Guid? tenantId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                SELECT TOP(1) o.TenantId
                FROM commerce.MarketplaceOrders o
                JOIN sales.SalesInvoices i ON i.TenantId=o.TenantId AND i.InvoiceId=o.OrderId AND i.OrderType='MARKETPLACE' AND i.PaidAmount=0
                WHERE o.LifecycleStatus IN('RESERVED','PAYMENT_PENDING') AND o.ReservationExpiresAt<=SYSUTCDATETIME()
                  AND NOT EXISTS(SELECT 1 FROM commerce.CommercePayments p
                    WHERE p.TenantId=o.TenantId AND p.InvoiceId=o.OrderId
                      AND p.Status=N'PAID')
                ORDER BY o.ReservationExpiresAt,o.TenantId;
                """, connection);
            var value = await command.ExecuteScalarAsync(token);
            tenantId = value is Guid id ? id : null;
        }
        return tenantId is Guid tenant
            ? await ExpireDueBatchAsync(tenant, batchSize, actor ?? "MARKETPLACE_EXPIRATION_WORKER", token)
            : 0;
    }

    private static async Task<MarketplaceOrderLifecycleResult> ReadResult(SqlCommand command, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Marketplace lifecycle operation returned no result.");
        return new(reader.GetGuid(reader.GetOrdinal("OrderId")), reader.GetString(reader.GetOrdinal("LifecycleStatus")),
            reader.GetString(reader.GetOrdinal("InvoiceStatus")), reader.GetBoolean(reader.GetOrdinal("IsReplay")));
    }

    private sealed record LifecycleRequest(Guid TenantId, Guid OrderId, string? ReleaseReason);
}
