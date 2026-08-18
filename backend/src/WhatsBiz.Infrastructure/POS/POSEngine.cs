#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSEngine(
    SqlIdempotencyExecutor idempotency,
    IHttpContextAccessor httpContext) : IPOSEngine
{
    public async Task<POSPostResult> Post(POSPostRequest r, CancellationToken token)
    {
        try
        {
            return await idempotency.Execute(
                IdempotencyKeyReader.Read(httpContext),
                "POS_SALE",
                r,
                r.User,
                async (connection, transaction, ct) =>
                {
                    await using var command = Command(connection, transaction, "sales.POS_PostInvoice",
                    [
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
            ("@CreatedBy", r.User)
        ], token);
    }

    public async Task Return(POSReturnRequest r, CancellationToken token)
    {
        await ExecuteMutation("POS_RETURN", r, r.User, "sales.POS_ReturnInvoice",
        [
            ("@InvoiceId", r.InvoiceId), ("@ItemsJson", r.ItemsJson),
            ("@Reason", r.Reason), ("@CreatedBy", r.User)
        ], token);
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
