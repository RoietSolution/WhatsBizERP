#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Application.Features.Loyalty;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSEngine(
    SqlIdempotencyExecutor idempotency,
    IHttpContextAccessor httpContext,
    ICurrentUserService currentUser,
    ILoyaltyService loyalty,
    IConfiguration configuration) : IPOSEngine
{
    public async Task<POSPostResult> Post(POSPostRequest r, CancellationToken token)
        => await PostInternal(r, currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."), IdempotencyKeyReader.Read(httpContext), token);

    public async Task<POSPostResult> PostForTenant(POSPostRequest r, Guid tenantId, string idempotencyKey, CancellationToken token)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable."));
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await using (var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection, transaction))
            { context.Parameters.AddWithValue("@tenant", tenantId); await context.ExecuteNonQueryAsync(token); }
            await using var command = Command(connection, transaction, "sales.POS_PostInvoice", [
                ("@TenantId", tenantId), ("@CounterId", r.CounterId), ("@ShiftId", r.ShiftId), ("@CustomerId", r.CustomerId), ("@WarehouseId", r.WarehouseId),
                ("@SalesPersonId", r.SalesPersonId), ("@ItemsJson", r.ItemsJson), ("@PaymentsJson", r.PaymentsJson), ("@BillDiscount", r.BillDiscount), ("@RoundOff", r.RoundOff),
                ("@Remarks", r.Remarks), ("@Status", r.Status), ("@InterState", r.InterState), ("@DiscountAuthorizedBy", r.DiscountAuthorizedBy), ("@CreatedBy", r.User)]);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Invoice post returned no result.");
            var result = new POSPostResult(reader.GetGuid(reader.GetOrdinal("InvoiceId")), reader.GetString(reader.GetOrdinal("InvoiceNumber")), reader.GetDecimal(reader.GetOrdinal("GrandTotal")), reader.GetDecimal(reader.GetOrdinal("PaidAmount")), reader.GetString(reader.GetOrdinal("Status")));
            await reader.CloseAsync();
            if (r.TenantId.HasValue && !string.IsNullOrWhiteSpace(r.SourceChannel))
            {
                await using var source = new SqlCommand("INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,SourceChannel,ProviderMode,LastNotifiedErpStatus,LastNotifiedOn,CreatedBy) VALUES(NEWID(),@tenant,@invoice,@channel,N'LIVE',@status,SYSUTCDATETIME(),@user);", connection, transaction);
                source.Parameters.AddWithValue("@tenant", tenantId); source.Parameters.AddWithValue("@invoice", result.InvoiceId); source.Parameters.AddWithValue("@channel", r.SourceChannel); source.Parameters.AddWithValue("@status", result.Status); source.Parameters.AddWithValue("@user", r.User ?? (object)DBNull.Value); await source.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token); return result;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }


    private async Task<POSPostResult> PostInternal(POSPostRequest r, Guid effectiveTenantId, Guid? idempotencyKey, CancellationToken token)
    {
        try
        {
            return await idempotency.Execute(
                idempotencyKey,
                "POS_SALE",
                r,
                r.User,
                async (connection, transaction, ct) =>
                {
                    if (r.CustomerId is Guid customerId)
                    {
                        var tenantId = effectiveTenantId;
                        if (r.TenantId is Guid requestTenant && requestTenant != tenantId) throw new BusinessRuleException("The transaction tenant does not match the authenticated tenant.");
                        await using var customer = new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE CustomerId=@customer AND TenantId=@tenant AND IsDeleted=0;", connection, transaction);
                        customer.Parameters.AddWithValue("@customer", customerId); customer.Parameters.AddWithValue("@tenant", tenantId);
                        if (Convert.ToInt32(await customer.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new BusinessRuleException("The selected customer is not available in the current tenant.");
                    }
                    await using var command = Command(connection, transaction, "sales.POS_PostInvoice",
                    [
                        ("@TenantId", effectiveTenantId),
                        ("@CounterId", r.CounterId), ("@ShiftId", r.ShiftId),
                        ("@CustomerId", r.CustomerId), ("@WarehouseId", r.WarehouseId),
                        ("@SalesPersonId", r.SalesPersonId), ("@ItemsJson", r.ItemsJson),
                        ("@PaymentsJson", r.PaymentsJson), ("@BillDiscount", r.BillDiscount),
                        ("@RoundOff", r.RoundOff), ("@Remarks", r.Remarks),
                        ("@Status", r.Status), ("@InterState", r.InterState),
                        ("@DiscountAuthorizedBy", r.DiscountAuthorizedBy), ("@CreatedBy", r.User)
                    ]);
                    POSPostResult result;
                    await using (var reader = await command.ExecuteReaderAsync(ct))
                    {
                        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Invoice post returned no result.");
                        result = new POSPostResult(
                            reader.GetGuid(reader.GetOrdinal("InvoiceId")),
                            reader.GetString(reader.GetOrdinal("InvoiceNumber")),
                            reader.GetDecimal(reader.GetOrdinal("GrandTotal")),
                            reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                            reader.GetString(reader.GetOrdinal("Status")));
                    }
                    if (r.TenantId.HasValue && !string.IsNullOrWhiteSpace(r.SourceChannel))
                    {
                        await using var source = new SqlCommand("INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,SourceChannel,ProviderMode,LastNotifiedErpStatus,LastNotifiedOn,CreatedBy) VALUES(NEWID(),@tenant,@invoice,@channel,@mode,@status,SYSUTCDATETIME(),@user);", connection, transaction);
                        source.Parameters.AddWithValue("@tenant", r.TenantId.Value);
                        source.Parameters.AddWithValue("@invoice", result.InvoiceId);
                        source.Parameters.AddWithValue("@channel", r.SourceChannel);
                        source.Parameters.AddWithValue("@mode", r.SourceChannel == "WHATSAPP_DEMO" ? "MOCK" : "LIVE");
                        source.Parameters.AddWithValue("@status", result.Status);
                        source.Parameters.AddWithValue("@user", r.User ?? (object)DBNull.Value);
                        await source.ExecuteNonQueryAsync(ct);
                    }
                    if (r.LoyaltyCoins > 0)
                    {
                        if (r.TenantId is not Guid tenantId || r.CustomerId is not Guid loyaltyCustomerId) throw new BusinessRuleException("A tenant and customer are required for coin redemption.");
                        await using var redeem = new SqlCommand("loyalty.RedeemForOrder", connection, transaction) { CommandType = CommandType.StoredProcedure };
                        redeem.Parameters.AddWithValue("@TenantId", tenantId); redeem.Parameters.AddWithValue("@CustomerId", loyaltyCustomerId);
                        redeem.Parameters.AddWithValue("@OrderId", result.InvoiceId); redeem.Parameters.AddWithValue("@Coins", r.LoyaltyCoins);
                        redeem.Parameters.AddWithValue("@OtherDiscount", r.OtherDiscount); redeem.Parameters.AddWithValue("@CreatedBy", r.User ?? (object)DBNull.Value);
                        await redeem.ExecuteNonQueryAsync(ct);
                    }
                    return result;
                }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51100)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    public async Task Pay(POSPaymentRequest r, CancellationToken token)
    {
        await ExecuteMutation("POS_PAYMENT", r, r.User, "sales.POS_AddPayment",
        [
            ("@InvoiceId", r.InvoiceId), ("@MethodCode", r.MethodCode),
            ("@Amount", r.Amount), ("@ReferenceNumber", r.ReferenceNumber),
            ("@CreatedBy", r.User), ("@TenantId", currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."))
        ], token);
    }

    public async Task Return(POSReturnRequest r, CancellationToken token)
    {
        await ExecuteMutation("POS_RETURN", r, r.User, "sales.POS_ReturnInvoice",
        [
            ("@InvoiceId", r.InvoiceId), ("@ItemsJson", r.ItemsJson),
            ("@Reason", r.Reason), ("@CreatedBy", r.User), ("@TenantId", currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."))
        ], token);
        if (currentUser.TenantId is Guid tenantId)
            await loyalty.ProcessOrderAsync(tenantId, r.InvoiceId, "CURRENT", r.User, token);
    }

    private async Task ExecuteMutation(
        string operation,
        object request,
        string? user,
        string procedure,
        IReadOnlyCollection<(string Name, object? Value)> parameters,
        CancellationToken token)
    {
        try
        {
            await idempotency.Execute(
                IdempotencyKeyReader.Read(httpContext), operation, request, user,
                async (connection, transaction, ct) =>
                {
                    await using var command = Command(connection, transaction, procedure, parameters);
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        throw new InvalidOperationException($"{operation} returned no result.");
                    return new MutationResult(
                        reader.GetValue(0)?.ToString() ?? string.Empty,
                        reader.FieldCount > 1 ? reader.GetValue(1)?.ToString() : null);
                }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51100)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    private static SqlCommand Command(
        SqlConnection connection,
        SqlTransaction transaction,
        string procedure,
        IEnumerable<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = procedure;
        command.CommandType = CommandType.StoredProcedure;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private sealed record MutationResult(string ReferenceId, string? ReferenceNumber);
}
