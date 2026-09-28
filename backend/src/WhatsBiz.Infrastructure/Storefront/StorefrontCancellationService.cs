using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

// A request, an ERP sale reversal, and a money refund remain distinct operations.
public sealed class StorefrontCancellationService(IConfiguration configuration,
    IStorefrontCustomerService customers, IErpSaleReversal reversal,
    ICommerceRefundService refunds, ILogger<StorefrontCancellationService> logger) : IStorefrontCancellationService
{
    private static readonly Action<ILogger, Guid, Exception?> RefundPreparationWarning =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1, "RefundPreparation"),
            "Refund preparation needs attention for reversed storefront invoice {InvoiceId}");
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontCancellationDto?> GetCustomerStatusAsync(
        string storeKey, string sessionToken, Guid orderId, CancellationToken token)
    {
        var identity = await Identity(storeKey, sessionToken, token);
        if (identity is null || orderId == Guid.Empty) return null;
        await using var connection = await Open(identity.Value.TenantId, token);
        var row = await Read(connection, null, identity.Value.TenantId, orderId, identity.Value.CustomerId, false, token);
        return row is null ? null : Map(row);
    }

    public async Task<StorefrontCancellationDto?> RequestAsync(
        string storeKey, string sessionToken, Guid orderId, string reason, CancellationToken token)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length is < 3 or > 250)
            throw new BusinessRuleException("Choose a cancellation reason.");
        var identity = await Identity(storeKey, sessionToken, token);
        if (identity is null || orderId == Guid.Empty) return null;
        await using var connection = await Open(identity.Value.TenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var row = await Read(connection, transaction, identity.Value.TenantId, orderId,
            identity.Value.CustomerId, true, token);
        if (row is null) return null;
        if (row.RequestStatus == "REQUESTED")
        {
            await transaction.CommitAsync(token);
            return Map(row);
        }
        if (!Eligible(row))
            throw new BusinessRuleException(Reason(row));
        await using var insert = new SqlCommand("""
            INSERT commerce.StorefrontCancellationRequests(CancellationRequestId,TenantId,InvoiceId,CustomerId,Reason,Status)
            VALUES(NEWID(),@tenant,@invoice,@customer,@reason,N'REQUESTED');
            """, connection, transaction);
        Add(insert, "@tenant", identity.Value.TenantId); Add(insert, "@invoice", orderId);
        Add(insert, "@customer", identity.Value.CustomerId); Add(insert, "@reason", normalized);
        await insert.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return await StatusForTenant(identity.Value.TenantId, orderId, identity.Value.CustomerId, token);
    }

    public async Task<StorefrontCancellationDto> DecideAsync(
        Guid tenantId, Guid orderId, Guid actorId, bool approve, string? note, CancellationToken token)
    {
        if (tenantId == Guid.Empty || actorId == Guid.Empty || orderId == Guid.Empty)
            throw new System.UnauthorizedAccessException("An authenticated retailer is required.");
        if (note?.Length > 500) throw new BusinessRuleException("Decision note is too long.");
        await using var connection = await Open(tenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var row = await Read(connection, transaction, tenantId, orderId, null, true, token)
            ?? throw new EntityNotFoundException("Storefront order was not found.");
        if (row.RequestStatus == "APPROVED" && approve)
        {
            await transaction.CommitAsync(token);
            if (row.RefundStatus == "REFUND_REQUIRED" && row.CollectedAmount > 0)
            {
                try { await refunds.PrepareFullCancellationAsync(tenantId, orderId, actorId, token); }
                catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
                { RefundPreparationWarning(logger, orderId, exception); }
            }
            return (await StatusForTenant(tenantId, orderId, null, token))!;
        }
        if (row.RequestStatus == "APPROVING" && approve)
        {
            await transaction.CommitAsync(token);
            return await CompleteSaleApproval(tenantId, orderId, actorId, row.RequestId!.Value, row.Reason!, token);
        }
        if (row.RequestStatus != "REQUESTED" || row.RequestId is null)
            throw new BusinessRuleException("No active cancellation request exists.");
        if (approve)
        {
            if (!Eligible(row, allowRequested: true))
                throw new BusinessRuleException(Reason(row));
            if (row.InvoiceStatus == "COMPLETED")
            {
                // Phase 3 full-cancellation refunds currently attach to one paid Commerce
                // application. Fail before reversal rather than leave a multi-tender sale
                // cancelled with an obligation that this orchestrator cannot prepare.
                if (row.PaidAmount > 0)
                {
                    await using var paymentShape = new SqlCommand("""
                        SELECT COUNT(*),ISNULL(SUM(a.Amount),0)
                        FROM commerce.PaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                        JOIN commerce.CommercePayments p WITH(UPDLOCK,HOLDLOCK)
                          ON p.PaymentId=a.PaymentId AND p.TenantId=a.TenantId
                          AND p.InvoiceId=a.InvoiceId AND p.Status=N'PAID'
                        WHERE a.TenantId=@tenant AND a.InvoiceId=@invoice
                          AND p.Provider IN(N'RAZORPAY',N'DIRECT_UPI',N'COD');
                        """, connection, transaction);
                    Add(paymentShape, "@tenant", tenantId); Add(paymentShape, "@invoice", orderId);
                    await using var paymentReader = await paymentShape.ExecuteReaderAsync(token);
                    await paymentReader.ReadAsync(token);
                    if (paymentReader.GetInt32(0) != 1 || paymentReader.GetDecimal(1) != row.PaidAmount)
                        throw new BusinessRuleException("This paid order needs finance review before full cancellation.");
                }
                await using var claim = new SqlCommand("""
                    UPDATE commerce.StorefrontCancellationRequests SET Status=N'APPROVING'
                    WHERE TenantId=@tenant AND CancellationRequestId=@request AND Status=N'REQUESTED';
                    """, connection, transaction);
                Add(claim, "@tenant", tenantId); Add(claim, "@request", row.RequestId.Value);
                if (await claim.ExecuteNonQueryAsync(token) != 1)
                    throw new BusinessRuleException("The cancellation request changed; refresh and retry.");
                await transaction.CommitAsync(token);
                return await CompleteSaleApproval(tenantId, orderId, actorId, row.RequestId.Value, row.Reason!, token);
            }
            if (!StorefrontCancellationPolicy.CanCancelWithoutFinancialReversal(
                row.InvoiceStatus, row.PaidAmount, row.CollectedAmount, row.PaymentProvider,
                row.DeliveryStatus, row.HasReturns, false))
                throw new BusinessRuleException("Paid-order cancellation requires a verified finance reversal and refund workflow; this order has not been changed.");
            await using (var pending = new SqlCommand("""
                SELECT COUNT(1) FROM commerce.CommercePayments WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@tenant AND InvoiceId=@invoice
                  AND (Status=N'PAID' OR Provider<>N'COD' AND Status IN(N'PENDING',N'PENDING_VERIFICATION'));
                """, connection, transaction))
            {
                Add(pending, "@tenant", tenantId); Add(pending, "@invoice", orderId);
                if ((int)(await pending.ExecuteScalarAsync(token) ?? 0) != 0)
                    throw new BusinessRuleException("A payment is collected or still pending; resolve it before cancellation.");
            }
            await using (var cancel = new SqlCommand("sales.POS_TransitionHeldInvoice", connection, transaction)
                { CommandType = CommandType.StoredProcedure })
            {
                Add(cancel, "@InvoiceId", orderId); Add(cancel, "@Action", "CANCEL");
                Add(cancel, "@ModifiedBy", actorId.ToString("N"));
                await cancel.ExecuteNonQueryAsync(token);
            }
            if (row.DeliveryStatus is not null)
            {
                await using var delivery = new SqlCommand("""
                    UPDATE commerce.OrderDeliveries SET DeliveryStatus=N'CANCELLED',UpdatedAt=SYSUTCDATETIME()
                    WHERE TenantId=@tenant AND OrderId=@invoice AND DeliveryStatus=@prior;
                    INSERT commerce.OrderDeliveryEvents(TenantId,OrderDeliveryId,EventType,PreviousStatus,NewStatus,ActorUserId,Notes)
                    SELECT @tenant,OrderDeliveryId,N'CANCELLED',@prior,N'CANCELLED',@actor,N'Storefront cancellation approved'
                    FROM commerce.OrderDeliveries WHERE TenantId=@tenant AND OrderId=@invoice AND DeliveryStatus=N'CANCELLED';
                    """, connection, transaction);
                Add(delivery, "@tenant", tenantId); Add(delivery, "@invoice", orderId);
                Add(delivery, "@prior", row.DeliveryStatus); Add(delivery, "@actor", actorId);
                await delivery.ExecuteNonQueryAsync(token);
            }
            await using (var cancelCod = new SqlCommand("""
                UPDATE commerce.CommercePayments SET Status=N'CANCELLED',UpdatedAt=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND InvoiceId=@invoice AND Provider=N'COD' AND Status=N'COD_PENDING';
                """, connection, transaction))
            {
                Add(cancelCod, "@tenant", tenantId); Add(cancelCod, "@invoice", orderId);
                await cancelCod.ExecuteNonQueryAsync(token);
            }
        }
        await using (var decision = new SqlCommand("""
            UPDATE commerce.StorefrontCancellationRequests
            SET Status=@status,DecidedAt=SYSUTCDATETIME(),DecidedBy=@actor,DecisionNote=@note
            WHERE TenantId=@tenant AND CancellationRequestId=@request AND Status=N'REQUESTED';
            """, connection, transaction))
        {
            Add(decision, "@status", approve ? "APPROVED" : "REJECTED");
            Add(decision, "@actor", actorId); Add(decision, "@note", note?.Trim());
            Add(decision, "@tenant", tenantId); Add(decision, "@request", row.RequestId);
            if (await decision.ExecuteNonQueryAsync(token) != 1)
                throw new BusinessRuleException("The cancellation request changed; refresh and retry.");
        }
        await transaction.CommitAsync(token);
        return (await StatusForTenant(tenantId, orderId, null, token))!;
    }

    private async Task<StorefrontCancellationDto> CompleteSaleApproval(Guid tenantId, Guid orderId,
        Guid actorId, Guid requestId, string reason, CancellationToken token)
    {
        // The ERP primitive owns its own atomic finance/inventory transaction. A durable
        // APPROVING claim prevents rejection while it runs and permits crash-safe replay.
        var result = await reversal.ReverseFullSaleAsync(
            new(tenantId, orderId, reason, actorId.ToString("N")), token);
        await using (var connection = await Open(tenantId, token))
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token))
        {
            await using var decision = new SqlCommand("""
                UPDATE commerce.StorefrontCancellationRequests
                SET Status=N'APPROVED',DecidedAt=SYSUTCDATETIME(),DecidedBy=@actor,
                    DecisionNote=N'Full sale reversed by ERP'
                WHERE TenantId=@tenant AND InvoiceId=@invoice AND CancellationRequestId=@request
                  AND Status=N'APPROVING'
                  AND EXISTS(SELECT 1 FROM sales.FullSaleReversals v
                    WHERE v.TenantId=@tenant AND v.InvoiceId=@invoice AND v.ReversalId=@reversal);
                """, connection, transaction);
            Add(decision, "@tenant", tenantId); Add(decision, "@invoice", orderId);
            Add(decision, "@request", requestId); Add(decision, "@actor", actorId);
            Add(decision, "@reversal", result.ReversalId);
            var changed = await decision.ExecuteNonQueryAsync(token);
            if (changed != 1)
            {
                await using var check = new SqlCommand("""
                    SELECT COUNT(1) FROM commerce.StorefrontCancellationRequests
                    WHERE TenantId=@tenant AND InvoiceId=@invoice AND CancellationRequestId=@request AND Status=N'APPROVED';
                    """, connection, transaction);
                Add(check, "@tenant", tenantId); Add(check, "@invoice", orderId); Add(check, "@request", requestId);
                if ((int)(await check.ExecuteScalarAsync(token) ?? 0) != 1)
                    throw new BusinessRuleException("Sale was reversed but cancellation approval needs reconciliation.");
            }
            await transaction.CommitAsync(token);
        }
        if (result.RefundRequired)
        {
            try { await refunds.PrepareFullCancellationAsync(tenantId, orderId, actorId, token); }
            catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
            {
                RefundPreparationWarning(logger, orderId, exception);
                // The committed reversal is never undone because refund preparation failed.
            }
        }
        return (await StatusForTenant(tenantId, orderId, null, token))!;
    }

    private async Task<StorefrontCancellationDto?> StatusForTenant(
        Guid tenantId, Guid invoiceId, Guid? customerId, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        var row = await Read(connection, null, tenantId, invoiceId, customerId, false, token);
        return row is null ? null : Map(row);
    }

    private async Task<(Guid TenantId, Guid CustomerId)?> Identity(
        string storeKey, string sessionToken, CancellationToken token)
    {
        var customer = await customers.GetSessionAsync(storeKey, sessionToken, token);
        if (customer is null || string.IsNullOrWhiteSpace(storeKey)) return null;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;
            """, connection);
        Add(command, "@key", storeKey.Trim().ToUpperInvariant());
        return await command.ExecuteScalarAsync(token) is Guid tenant
            ? (tenant, customer.Id) : null;
    }

    private async Task<SqlConnection> Open(Guid tenantId, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant", connection);
        Add(context, "@tenant", tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }

    private static async Task<Row?> Read(SqlConnection connection, SqlTransaction? transaction,
        Guid tenantId, Guid invoiceId, Guid? customerId, bool locked, CancellationToken token)
    {
        var hint = locked ? "WITH(UPDLOCK,HOLDLOCK)" : "";
        await using var command = new SqlCommand($"""
            SELECT i.Status,i.GrandTotal,i.PaidAmount,i.CustomerId,
              d.DeliveryStatus,
              CASE WHEN EXISTS(SELECT 1 FROM sales.SalesInvoiceItems x WHERE x.InvoiceId=i.InvoiceId AND x.ReturnedQuantity>0) THEN 1 ELSE 0 END,
              req.CancellationRequestId,req.Status,req.Reason,
              pay.Provider,pay.Amount,
              ISNULL((SELECT SUM(r.RefundAmount) FROM commerce.StorefrontRefunds r
                WHERE r.TenantId=i.TenantId AND r.InvoiceId=i.InvoiceId AND r.RefundStatus IN(N'REFUND_PENDING',N'REFUNDED')),0),
              COALESCE(refund.RefundStatus,reversal.RefundStatus),
              COALESCE(refund.RefundAmount,reversal.RefundRequiredAmount),refund.RefundedAt,
              attempt.Status,req.DecisionNote
            FROM sales.SalesInvoices i {hint}
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            OUTER APPLY(SELECT TOP(1) d.DeliveryStatus FROM commerce.OrderDeliveries d {(locked ? "WITH(UPDLOCK,HOLDLOCK)" : "")}
              WHERE d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId) d
            OUTER APPLY(SELECT TOP(1) r.CancellationRequestId,r.Status,r.Reason,r.DecisionNote FROM commerce.StorefrontCancellationRequests r
              WHERE r.TenantId=i.TenantId AND r.InvoiceId=i.InvoiceId ORDER BY r.RequestedAt DESC) req
            OUTER APPLY(SELECT TOP(1) p.Provider,a.Amount FROM commerce.PaymentApplications a
              JOIN commerce.CommercePayments p ON p.PaymentId=a.PaymentId AND p.TenantId=a.TenantId AND p.Status=N'PAID'
              WHERE a.TenantId=i.TenantId AND a.InvoiceId=i.InvoiceId) pay
            OUTER APPLY(SELECT TOP(1) r.RefundId,r.RefundStatus,r.RefundAmount,r.RefundedAt FROM commerce.StorefrontRefunds r
              WHERE r.TenantId=i.TenantId AND r.InvoiceId=i.InvoiceId ORDER BY r.RequestedAt DESC) refund
            OUTER APPLY(SELECT TOP(1) v.RefundStatus,v.RefundRequiredAmount FROM sales.FullSaleReversals v
              WHERE v.TenantId=i.TenantId AND v.InvoiceId=i.InvoiceId) reversal
            OUTER APPLY(SELECT TOP(1) a.Status FROM commerce.StorefrontRefundAttempts a
              WHERE a.TenantId=i.TenantId AND a.RefundId=refund.RefundId ORDER BY a.AttemptNumber DESC) attempt
            WHERE i.TenantId=@tenant AND i.InvoiceId=@invoice AND (@customer IS NULL OR i.CustomerId=@customer);
            """, connection, transaction);
        Add(command, "@tenant", tenantId); Add(command, "@invoice", invoiceId);
        Add(command, "@customer", customerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5) != 0,
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? 0 : reader.GetDecimal(10), reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? 0 : reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetDateTimeOffset(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16))
            : null;
    }

    private static bool Eligible(Row row, bool allowRequested = false)
        => row.InvoiceStatus is "HELD" or "SUSPENDED" or "COMPLETED"
            && row.DeliveryStatus is null or "UNASSIGNED" or "ASSIGNED" or "READY_FOR_PICKUP"
            && !row.HasReturns && (row.RequestStatus is null or "REJECTED" ||
                allowRequested && row.RequestStatus == "REQUESTED");

    private static string Reason(Row row)
        => row.InvoiceStatus is not ("HELD" or "SUSPENDED" or "COMPLETED")
            ? "This order can no longer be cancelled."
            : row.DeliveryStatus is not (null or "UNASSIGNED" or "ASSIGNED" or "READY_FOR_PICKUP")
                ? "Delivery has progressed beyond cancellable status."
                : row.HasReturns ? "An order with existing returns cannot use full cancellation."
                    : row.RequestStatus == "REQUESTED" ? "Cancellation is already requested."
                        : row.RequestStatus == "APPROVING" ? "Cancellation approval is in progress."
                        : "Cancellation is unavailable for this order.";

    private static StorefrontCancellationDto Map(Row row)
    {
        var refundable = StorefrontCancellationPolicy.AvailableRefund(row.GrandTotal, row.CollectedAmount, row.ReservedRefundAmount);
        return new(Eligible(row), Eligible(row) ? null : Reason(row), row.RequestStatus, row.Reason,
            refundable, row.RefundStatus ?? "NONE", row.RefundAmount, row.RefundedAt,
            row.RefundAttemptStatus == "UNKNOWN" || row.RefundAttemptStatus == "CONFIRMED" && row.RefundStatus != "REFUNDED",
            row.DecisionNote);
    }

    private static void Add(SqlCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record Row(string InvoiceStatus, decimal GrandTotal, decimal PaidAmount, Guid CustomerId,
        string? DeliveryStatus, bool HasReturns, Guid? RequestId, string? RequestStatus, string? Reason,
        string? PaymentProvider, decimal CollectedAmount, decimal ReservedRefundAmount,
        string? RefundStatus, decimal RefundAmount, DateTimeOffset? RefundedAt, string? RefundAttemptStatus,
        string? DecisionNote);
}
