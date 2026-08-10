#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Purchases;

public sealed class PurchaseEngine(
    SqlIdempotencyExecutor idempotency,
    IHttpContextAccessor httpContext) : IPurchaseEngine
{
    public async Task<PurchasePostResult> Post(PurchasePostRequest r, CancellationToken token)
    {
        try
        {
            return await idempotency.Execute(
                IdempotencyKeyReader.Read(httpContext), "PURCHASE", r, r.User,
                async (connection, transaction, ct) =>
                {
                    await using var command = Command(connection, transaction, "purchase.Purchase_Post",
                    [
                        ("@SupplierId", r.SupplierId), ("@SupplierInvoiceNo", r.SupplierInvoiceNo),
                        ("@InvoiceDate", r.InvoiceDate), ("@DueDate", r.DueDate),
                        ("@WarehouseId", r.WarehouseId), ("@ItemsJson", r.ItemsJson),
                        ("@ExpensesJson", r.ExpensesJson), ("@PaymentsJson", r.PaymentsJson),
                        ("@BillDiscount", r.BillDiscount), ("@RoundOff", r.RoundOff),
                        ("@Remarks", r.Remarks), ("@Status", r.Status), ("@CreatedBy", r.User)
                    ]);
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Purchase post returned no result.");
                    return new PurchasePostResult(
                        reader.GetGuid(reader.GetOrdinal("PurchaseInvoiceId")),
                        reader.GetString(reader.GetOrdinal("InvoiceNumber")),
                        reader.GetDecimal(reader.GetOrdinal("GrandTotal")),
                        reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                        reader.GetDecimal(reader.GetOrdinal("BalanceAmount")),
                        reader.GetString(reader.GetOrdinal("Status")));
                }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51200)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    public Task Pay(PurchasePaymentRequest r, CancellationToken token) => ExecuteMutation(
        "PURCHASE_PAYMENT", r, r.User, "purchase.Purchase_AddPayment",
        [("@PurchaseInvoiceId", r.PurchaseInvoiceId), ("@MethodCode", r.MethodCode),
         ("@Amount", r.Amount), ("@ReferenceNumber", r.ReferenceNumber), ("@CreatedBy", r.User)], token);

    public Task Return(PurchaseReturnRequest r, CancellationToken token) => ExecuteMutation(
        "PURCHASE_RETURN", r, r.User, "purchase.Purchase_Return",
        [("@PurchaseInvoiceId", r.PurchaseInvoiceId), ("@ItemsJson", r.ItemsJson),
         ("@Reason", r.Reason), ("@CreatedBy", r.User)], token);

    private async Task ExecuteMutation(string operation, object request, string? user, string procedure,
        IReadOnlyCollection<(string Name, object? Value)> parameters, CancellationToken token)
    {
        try
        {
            await idempotency.Execute(
                IdempotencyKeyReader.Read(httpContext), operation, request, user,
                async (connection, transaction, ct) =>
                {
                    await using var command = Command(connection, transaction, procedure, parameters);
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct)) throw new InvalidOperationException($"{operation} returned no result.");
                    return new MutationResult(reader.GetValue(0)?.ToString() ?? string.Empty,
                        reader.FieldCount > 1 ? reader.GetValue(1)?.ToString() : null);
                }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51200)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string procedure,
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
