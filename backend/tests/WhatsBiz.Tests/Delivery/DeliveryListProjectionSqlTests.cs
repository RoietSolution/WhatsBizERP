using FluentAssertions;
using Microsoft.Data.SqlClient;
using WhatsBiz.Tests.Integration;

namespace WhatsBiz.Tests.Delivery;

public sealed class DeliveryListProjectionSqlTests
{
    [Fact(Timeout = 30_000)]
    public async Task DeliveryListProjectionMaterializesOptionalCollectionFieldsWithTheirActualSqlTypes()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);

        Guid? tenant = null;
        await using (var tenantCommand = new SqlCommand("SELECT TOP (1) TenantId FROM commerce.OrderDeliveries ORDER BY UpdatedAt DESC", connection))
        {
            var value = await tenantCommand.ExecuteScalarAsync();
            tenant = value is Guid id ? id : null;
        }
        tenant.Should().NotBeNull("the disposable integration database must contain a delivery row");
        var tenantId = tenant!.Value;

        const string sql = """
            SELECT TOP (20)
                d.OrderDeliveryId,d.OrderId,i.InvoiceNumber,i.InvoiceDate,d.DeliveryStatus,d.DeliveryAgentId,a.DisplayName,
                d.AssignmentSource,d.CustomerNameSnapshot,d.CustomerMobileSnapshot,d.DeliveryAddressSnapshot,d.DeliveryNotes,
                i.GrandTotal,d.CodRequired,d.CodAmount,d.CodCollected,d.CodPaymentMethod,d.ScheduledDate,d.TimeWindow,
                d.SourceChannel,d.OtpVerified,d.UpdatedAt,d.Latitude,d.Longitude,d.CodCollectedAt,d.CodReference,
                d.ProofType,d.DeliveredAt,cod.Amount,cod.AppliedBy,cod.AppliedAt
            FROM commerce.OrderDeliveries d
            JOIN sales.SalesInvoices i ON i.InvoiceId=d.OrderId AND i.TenantId=d.TenantId
            LEFT JOIN commerce.DeliveryAgents a ON a.DeliveryAgentId=d.DeliveryAgentId AND a.TenantId=d.TenantId
            OUTER APPLY (
                SELECT TOP(1) pa.Amount,pa.AppliedBy,pa.AppliedAt
                FROM commerce.PaymentApplications pa
                JOIN commerce.CommercePayments cp ON cp.TenantId=pa.TenantId AND cp.PaymentId=pa.PaymentId
                WHERE pa.TenantId=d.TenantId AND pa.InvoiceId=d.OrderId
                  AND cp.Provider=N'COD' AND cp.Status IN(N'PAID',N'CAPTURED')
                ORDER BY pa.AppliedAt DESC
            ) cod
            WHERE d.TenantId=@tenant
            ORDER BY d.UpdatedAt DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
            reader.GetDecimal(12).Should().BeGreaterThanOrEqualTo(0);
            if (!reader.IsDBNull(14)) reader.GetDecimal(14).Should().BeGreaterThanOrEqualTo(0);
            if (!reader.IsDBNull(28)) reader.GetDecimal(28).Should().BeGreaterThanOrEqualTo(0);
            if (!reader.IsDBNull(24)) reader.GetDateTimeOffset(24).Should().BeAfter(DateTimeOffset.MinValue);
            if (!reader.IsDBNull(30)) reader.GetDateTimeOffset(30).Should().BeAfter(DateTimeOffset.MinValue);
            if (!reader.IsDBNull(27)) reader.GetDateTimeOffset(27).Should().BeAfter(DateTimeOffset.MinValue);
            if (!reader.IsDBNull(29)) reader.GetString(29).Should().NotBeNullOrWhiteSpace();
        }

        count.Should().BeGreaterThan(0);
    }
}
