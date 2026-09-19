#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Purchases;

public sealed class PurchaseEngine(
    SqlIdempotencyExecutor idempotency,
    IHttpContextAccessor httpContext,
    ICurrentUserService currentUser,
    ILogger<PurchaseEngine> logger) : IPurchaseEngine
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
                        ("@TenantId", currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required.")),
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
        catch (SqlException ex) when (ex.Number >= 51200 || ex.Number is 547 or 2601 or 2627)
        {
            PurchaseIntegrityDiagnostics.Log(logger, "Purchase", "purchase.Purchase_Post", r.SupplierId, currentUser.TenantId, ex.Number, ex);
            throw new BusinessRuleException(ex.Number >= 51200 ? ex.Message : PurchaseErrorMessages.PostIntegrity);
        }
    }

    public Task Pay(PurchasePaymentRequest r, CancellationToken token) => ExecuteMutation(
        "PURCHASE_PAYMENT", r, r.User, "purchase.Purchase_AddPayment",
        [("@PurchaseInvoiceId", r.PurchaseInvoiceId), ("@MethodCode", r.MethodCode),
         ("@Amount", r.Amount), ("@ReferenceNumber", r.ReferenceNumber), ("@CreatedBy", r.User), ("@TenantId", currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."))], PurchaseErrorMessages.PaymentIntegrity, "Purchase Payment", r.PurchaseInvoiceId, token);

    public Task Return(PurchaseReturnRequest r, CancellationToken token) => ExecuteMutation(
        "PURCHASE_RETURN", r, r.User, "purchase.Purchase_Return",
        [("@PurchaseInvoiceId", r.PurchaseInvoiceId), ("@ItemsJson", r.ItemsJson),
         ("@Reason", r.Reason), ("@CreatedBy", r.User), ("@TenantId", currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."))], PurchaseErrorMessages.ReturnIntegrity, "Purchase Return", r.PurchaseInvoiceId, token);

    private async Task ExecuteMutation(string operation, object request, string? user, string procedure,
        IReadOnlyCollection<(string Name, object? Value)> parameters, string integrityMessage,
        string diagnosticOperation, Guid sourceId, CancellationToken token)
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
        catch (SqlException ex) when (ex.Number >= 51200 || ex.Number is 547 or 2601 or 2627)
        {
            PurchaseIntegrityDiagnostics.Log(logger, diagnosticOperation, procedure, sourceId, currentUser.TenantId, ex.Number, ex);
            throw new BusinessRuleException(ex.Number >= 51200 ? ex.Message : integrityMessage);
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

internal static class PurchaseErrorMessages
{
    internal const string PostIntegrity = "The purchase could not be posted because it violated an inventory, accounting, or data-integrity rule.";
    internal const string PaymentIntegrity = "The purchase payment could not be posted because the purchase or payment account is invalid.";
    internal const string ReturnIntegrity = "The purchase return could not be posted because it violated an inventory or accounting rule.";
}

internal static class PurchaseIntegrityDiagnostics
{
    private static readonly Action<ILogger, string, int, string, string, Guid, Guid?, Exception?> LogIntegrityFailure =
        LoggerMessage.Define<string, int, string, string, Guid, Guid?>(
            LogLevel.Error,
            new EventId(5210, nameof(LogIntegrityFailure)),
            "Purchase SQL integrity failure. Operation={Operation} SqlErrorNumber={SqlErrorNumber} SqlErrorMessage={SqlErrorMessage} Procedure={Procedure} SourceId={SourceId} TenantId={TenantId}");

    internal static void Log(ILogger logger, string operation, string procedure, Guid sourceId, Guid? tenantId, int sqlErrorNumber, Exception exception)
    {
        LogIntegrityFailure(logger, operation, sqlErrorNumber, exception.Message, procedure, sourceId, tenantId, exception);
    }
}
