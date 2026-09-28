using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Infrastructure.Payments;

// Provider dispatch is deliberately outside SQL transactions. A STARTED/UNKNOWN
// attempt is never sent again; reconciliation reads the provider instead.
public sealed class CommerceRefundService(IConfiguration configuration,
    IDataProtectionProvider protection, IPaymentGatewayResolver gateways,
    IErpRefundSettlement erp) : ICommerceRefundService
{
    private const string SecretPurpose = "WhatsBiz.Payments.Razorpay.Secrets.v1";
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontRefundDto> PrepareFullCancellationAsync(Guid tenantId,
        Guid invoiceId, Guid actorId, CancellationToken token)
    {
        if (tenantId == Guid.Empty || invoiceId == Guid.Empty || actorId == Guid.Empty)
            throw new System.UnauthorizedAccessException("An authenticated retailer is required.");
        await using var connection = await Open(tenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await using var read = new SqlCommand("""
            SELECT v.RefundRequiredAmount,v.RefundStatus,v.Reason,p.PaymentId,p.Provider,
                a.Amount,p.Status,i.Status,
                (SELECT TOP(1) RefundId FROM commerce.StorefrontRefunds r WITH(UPDLOCK,HOLDLOCK)
                 WHERE r.TenantId=@tenant AND r.InvoiceId=@invoice AND r.RefundType=N'FULL_CANCEL')
            FROM sales.FullSaleReversals v WITH(UPDLOCK,HOLDLOCK)
            JOIN sales.SalesInvoices i ON i.InvoiceId=v.InvoiceId AND i.TenantId=v.TenantId
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=v.TenantId
                AND w.InvoiceId=v.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            JOIN commerce.PaymentApplications a ON a.TenantId=v.TenantId AND a.InvoiceId=v.InvoiceId
            JOIN commerce.CommercePayments p ON p.PaymentId=a.PaymentId AND p.TenantId=a.TenantId
                AND p.InvoiceId=a.InvoiceId
            WHERE v.TenantId=@tenant AND v.InvoiceId=@invoice;
            """, connection, transaction);
        P(read, "@tenant", tenantId); P(read, "@invoice", invoiceId);
        decimal obligation, applied; string status, reason, provider, paymentStatus, invoiceStatus;
        Guid paymentId; Guid? existing;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
                throw new BusinessRuleException("A reversed storefront sale with applied payment is required.");
            obligation=reader.GetDecimal(0); status=reader.GetString(1); reason=reader.GetString(2);
            paymentId=reader.GetGuid(3); provider=reader.GetString(4); applied=reader.GetDecimal(5);
            paymentStatus=reader.GetString(6); invoiceStatus=reader.GetString(7);
            existing=reader.IsDBNull(8)?null:reader.GetGuid(8);
        }
        if (existing is not null)
        {
            await transaction.CommitAsync(token);
            return await GetAsync(tenantId, existing.Value, token);
        }
        if (obligation<=0 || applied!=obligation || paymentStatus!="PAID" ||
            invoiceStatus!="CANCELLED" || status!="REFUND_REQUIRED")
            throw new BusinessRuleException("The committed refund obligation does not match the collected payment.");
        var refundId=Guid.NewGuid();
        await using var insert=new SqlCommand("""
            INSERT commerce.StorefrontRefunds(RefundId,TenantId,InvoiceId,PaymentId,Provider,
                RefundType,RefundAmount,RefundStatus,Reason,RequestedBy)
            VALUES(@refund,@tenant,@invoice,@payment,@provider,N'FULL_CANCEL',@amount,
                N'REFUND_REQUIRED',@reason,@actor);
            """,connection,transaction);
        P(insert,"@refund",refundId); P(insert,"@tenant",tenantId); P(insert,"@invoice",invoiceId);
        P(insert,"@payment",paymentId); P(insert,"@provider",provider);
        P(insert,"@amount",obligation); P(insert,"@reason",reason); P(insert,"@actor",actorId);
        await insert.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(tenantId, refundId, token);
    }

    public async Task<StorefrontRefundDto> StartRazorpayAsync(Guid tenantId, Guid refundId,
        CancellationToken token)
    {
        var row=await Read(tenantId,refundId,token);
        if(row.Provider!="RAZORPAY" || string.IsNullOrWhiteSpace(row.ProviderPaymentId))
            throw new BusinessRuleException("A verified Razorpay collection is required.");
        var config=await RazorpayConfiguration(tenantId,token);
        var attemptId=Guid.NewGuid();
        await using(var connection=await Open(tenantId,token))
        await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token))
        {
            await using var claim=new SqlCommand("""
                UPDATE commerce.StorefrontRefunds SET RefundStatus=N'REFUND_PENDING',
                    ProviderRefundId=NULL,FailedAt=NULL,FailureCode=NULL,
                    ProcessingStartedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND RefundId=@refund AND Provider=N'RAZORPAY'
                    AND RefundStatus IN(N'REFUND_REQUIRED',N'REFUND_FAILED')
                    AND NOT EXISTS(SELECT 1 FROM commerce.StorefrontRefundAttempts a
                        WHERE a.TenantId=@tenant AND a.RefundId=@refund
                          AND a.Status IN(N'STARTED',N'ACCEPTED',N'UNKNOWN',N'CONFIRMED'));
                """,connection,transaction);
            P(claim,"@tenant",tenantId); P(claim,"@refund",refundId);
            if(await claim.ExecuteNonQueryAsync(token)!=1)
                throw new BusinessRuleException("Refund dispatch was already claimed; reconcile its existing attempt.");
            await using var attempt=new SqlCommand("""
                INSERT commerce.StorefrontRefundAttempts(RefundAttemptId,TenantId,RefundId,AttemptNumber,Status)
                SELECT @attemptId,@tenant,@refund,ISNULL(MAX(AttemptNumber),0)+1,N'STARTED'
                FROM commerce.StorefrontRefundAttempts WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@tenant AND RefundId=@refund;
                """,connection,transaction);
            P(attempt,"@tenant",tenantId); P(attempt,"@refund",refundId);
            P(attempt,"@attemptId",attemptId);
            await attempt.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        row=row with { ProviderRefundId=null };
        // A crash/timeout after the durable claim leaves a reconcilable attempt, never a retryable POST.
        GatewayRefundResult result;
        try
        {
            result=await gateways.Resolve(PaymentProviders.Razorpay).CreateRefundAsync(
                config,row.ProviderPaymentId,row.Amount,row.Currency,refundId,attemptId,token);
        }
        catch(Exception exception) when (exception is not OutOfMemoryException)
        {
            await MarkUnknown(tenantId,attemptId,CancellationToken.None);
            return await GetAsync(tenantId,refundId,token);
        }
        await ApplyProviderResult(tenantId,row,attemptId,result,token);
        return await GetAsync(tenantId,refundId,token);
    }

    public async Task<StorefrontRefundDto> ReconcileRazorpayAsync(Guid tenantId,Guid refundId,
        CancellationToken token)
    {
        var row=await Read(tenantId,refundId,token);
        if(row.Provider!="RAZORPAY" || row.Status=="REFUND_REQUIRED")
            throw new BusinessRuleException("No Razorpay refund attempt is awaiting reconciliation.");
        if(row.Status=="REFUNDED") return row.ToDto();
        var attempt=await LatestAttempt(tenantId,refundId,token);
        if(attempt.Status=="CONFIRMED")
        {
            await SettleConfirmed(tenantId,row,attempt.ProviderRefundId!,"RAZORPAY",DateTimeOffset.UtcNow,token);
            return await GetAsync(tenantId,refundId,token);
        }
        var config=await RazorpayConfiguration(tenantId,token);
        GatewayRefundResult? result;
        try
        {
            result=await gateways.Resolve(PaymentProviders.Razorpay).FindRefundAsync(config,
                row.ProviderPaymentId!,attempt.ProviderRefundId,refundId,attempt.AttemptId,token);
        }
        catch(Exception exception) when (exception is not OutOfMemoryException)
        {
            await MarkUnknown(tenantId,attempt.AttemptId,CancellationToken.None);
            return await GetAsync(tenantId,refundId,token);
        }
        if(result is not null) await ApplyProviderResult(tenantId,row,attempt.AttemptId,result,token);
        else await MarkUnknown(tenantId,attempt.AttemptId,token);
        return await GetAsync(tenantId,refundId,token);
    }

    public async Task<StorefrontRefundDto> ConfirmManualAsync(Guid tenantId,Guid refundId,
        ConfirmManualRefundInput input,string actor,CancellationToken token)
    {
        var mode=input.SettlementMode?.Trim().ToUpperInvariant();
        var reference=input.ExternalReference?.Trim();
        if(string.IsNullOrWhiteSpace(reference) || reference.Length>100 ||
            input.SettledAt> DateTimeOffset.UtcNow.AddMinutes(5) ||
            mode is not ("CASH" or "BANK" or "UPI"))
            throw new BusinessRuleException("A valid external refund reference, method and date are required.");
        var row=await Read(tenantId,refundId,token);
        if(row.Provider is not ("DIRECT_UPI" or "COD") ||
            row.Provider=="DIRECT_UPI" && mode=="CASH")
            throw new BusinessRuleException("This refund requires provider confirmation or another settlement mode.");
        if(row.Status=="REFUNDED") return row.ToDto();
        await using(var connection=await Open(tenantId,token))
        await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token))
        {
            await using var lockRow=new SqlCommand("""
                SELECT RefundStatus FROM commerce.StorefrontRefunds WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@tenant AND RefundId=@refund;
                """,connection,transaction);
            P(lockRow,"@tenant",tenantId); P(lockRow,"@refund",refundId);
            var state=(string?)await lockRow.ExecuteScalarAsync(token);
            if(state=="REFUND_REQUIRED")
            {
                await using var insert=new SqlCommand("""
                    INSERT commerce.StorefrontRefundAttempts(RefundAttemptId,TenantId,RefundId,AttemptNumber,
                        Status,SettlementMode,ExternalReference,SettledAt,FinishedAt)
                    VALUES(NEWID(),@tenant,@refund,1,N'CONFIRMED',@mode,@reference,@date,SYSUTCDATETIME());
                    UPDATE commerce.StorefrontRefunds SET RefundStatus=N'REFUND_PENDING',
                        ProcessingStartedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
                    WHERE TenantId=@tenant AND RefundId=@refund;
                    """,connection,transaction);
                P(insert,"@tenant",tenantId); P(insert,"@refund",refundId);
                P(insert,"@mode",mode); P(insert,"@reference",reference);
                P(insert,"@date",input.SettledAt);
                await insert.ExecuteNonQueryAsync(token);
            }
            else if(state=="REFUND_PENDING")
            {
                await using var check=new SqlCommand("""
                    SELECT COUNT(*) FROM commerce.StorefrontRefundAttempts
                    WHERE TenantId=@tenant AND RefundId=@refund AND Status=N'CONFIRMED'
                        AND SettlementMode=@mode AND ExternalReference=@reference AND SettledAt=@date;
                    """,connection,transaction);
                P(check,"@tenant",tenantId); P(check,"@refund",refundId);
                P(check,"@mode",mode); P(check,"@reference",reference); P(check,"@date",input.SettledAt);
                if((int)(await check.ExecuteScalarAsync(token)??0)!=1)
                    throw new BusinessRuleException("This refund already has a different settlement attempt.");
            }
            else throw new BusinessRuleException("Refund is not awaiting manual confirmation.");
            await transaction.CommitAsync(token);
        }
        await erp.SettleAsync(new(tenantId,refundId,mode!,reference!,input.SettledAt,actor),token);
        return await GetAsync(tenantId,refundId,token);
    }

    public async Task<StorefrontRefundDto> GetAsync(Guid tenantId,Guid refundId,CancellationToken token)
        => (await Read(tenantId,refundId,token)).ToDto();

    public async Task ProcessRazorpayRefundWebhookAsync(ReadOnlyMemory<byte> rawBody,
        string signature,CancellationToken token)
    {
        // Unverified identifiers are used only to locate the tenant's verification key.
        Guid? marker=null; string? providerRefundId=null; string? paymentId=null; string? eventType=null;
        try
        {
            using var json=JsonDocument.Parse(rawBody);
            var root=json.RootElement;
            eventType=root.GetProperty("event").GetString();
            var refund=root.GetProperty("payload").GetProperty("refund").GetProperty("entity");
            providerRefundId=refund.GetProperty("id").GetString();
            paymentId=refund.GetProperty("payment_id").GetString();
            if(refund.TryGetProperty("notes",out var notes) && notes.ValueKind==JsonValueKind.Object &&
                notes.TryGetProperty("refund_id",out var value) && value.ValueKind==JsonValueKind.String &&
                Guid.TryParseExact(value.GetString(),"N",out var parsed)) marker=parsed;
        }
        catch(Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new BusinessRuleException("Razorpay refund event is invalid."); }
        if(eventType is not ("refund.created" or "refund.processed" or "refund.failed") ||
            string.IsNullOrWhiteSpace(providerRefundId) || string.IsNullOrWhiteSpace(paymentId))
            throw new BusinessRuleException("Razorpay refund event is not supported.");
        Guid tenantId, refundId;
        await using(var connection=new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync(token);
            await using var command=new SqlCommand("""
                SELECT TOP(1) r.TenantId,r.RefundId
                FROM commerce.StorefrontRefunds r
                JOIN commerce.CommercePayments p ON p.PaymentId=r.PaymentId
                    AND p.TenantId=r.TenantId AND p.InvoiceId=r.InvoiceId
                WHERE r.Provider=N'RAZORPAY' AND p.ProviderPaymentId=@payment
                  AND (@marker IS NULL OR r.RefundId=@marker)
                  AND (r.ProviderRefundId=@providerRefund OR r.RefundId=@marker
                    OR EXISTS(SELECT 1 FROM commerce.StorefrontRefundAttempts a
                        WHERE a.TenantId=r.TenantId AND a.RefundId=r.RefundId
                          AND a.ProviderRefundId=@providerRefund));
                """,connection);
            P(command,"@payment",paymentId); P(command,"@marker",marker);
            P(command,"@providerRefund",providerRefundId);
            await using var reader=await command.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token))
                throw new BusinessRuleException("Razorpay refund event could not be correlated.");
            tenantId=reader.GetGuid(0); refundId=reader.GetGuid(1);
        }
        var config=await RazorpayConfiguration(tenantId,token);
        var verified=gateways.Resolve(PaymentProviders.Razorpay)
            .VerifyWebhook(config,rawBody,signature,null);
        if(!verified.SignatureValid)
            throw new System.UnauthorizedAccessException("Razorpay refund signature is invalid.");
        // Read the provider's current refund state; the browser or webhook payload is not final proof.
        await ReconcileRazorpayAsync(tenantId,refundId,token);
    }

    private async Task ApplyProviderResult(Guid tenantId,RefundRow row,Guid attemptId,GatewayRefundResult result,
        CancellationToken token)
    {
        if(result.Amount!=row.Amount || result.Currency!=row.Currency ||
            result.ProviderPaymentId!=row.ProviderPaymentId || string.IsNullOrWhiteSpace(result.ProviderRefundId) ||
            row.ProviderRefundId is not null && row.ProviderRefundId!=result.ProviderRefundId)
        {
            await MarkUnknown(tenantId,attemptId,token);
            return;
        }
        var attemptStatus=result.Status switch
        {
            "PROCESSED" => "CONFIRMED",
            "FAILED" => "FAILED",
            _ => "ACCEPTED"
        };
        await using(var connection=await Open(tenantId,token))
        await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token))
        {
            await using var command=new SqlCommand("""
                UPDATE commerce.StorefrontRefundAttempts SET
                    Status=CASE WHEN Status=N'CONFIRMED' THEN Status ELSE @attempt END,
                    ProviderRefundId=@providerId,
                    FinishedAt=CASE WHEN Status=N'CONFIRMED' THEN FinishedAt
                        WHEN @attempt IN(N'CONFIRMED',N'FAILED') THEN SYSUTCDATETIME() ELSE NULL END
                WHERE TenantId=@tenant AND RefundId=@refund AND RefundAttemptId=@attemptId
                    AND Status IN(N'STARTED',N'UNKNOWN',N'ACCEPTED',N'FAILED',N'CONFIRMED');
                UPDATE commerce.StorefrontRefunds SET ProviderRefundId=@providerId,
                    RefundStatus=CASE WHEN @attempt=N'FAILED' THEN N'REFUND_FAILED' ELSE N'REFUND_PENDING' END,
                    FailedAt=CASE WHEN @attempt=N'FAILED' THEN SYSUTCDATETIME() ELSE FailedAt END,
                    UpdatedAt=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND RefundId=@refund AND RefundStatus<>N'REFUNDED'
                    AND (@attempt=N'CONFIRMED' OR NOT EXISTS(SELECT 1
                        FROM commerce.StorefrontRefundAttempts a
                        WHERE a.TenantId=@tenant AND a.RefundId=@refund AND a.Status=N'CONFIRMED'));
                """,connection,transaction);
            P(command,"@attempt",attemptStatus); P(command,"@providerId",result.ProviderRefundId);
            P(command,"@tenant",tenantId); P(command,"@refund",row.RefundId);
            P(command,"@attemptId",attemptId);
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        if(attemptStatus=="CONFIRMED")
            await SettleConfirmed(tenantId,row,result.ProviderRefundId,"RAZORPAY",DateTimeOffset.UtcNow,token);
    }

    private async Task SettleConfirmed(Guid tenantId,RefundRow row,string reference,string mode,
        DateTimeOffset date,CancellationToken token)
        => await erp.SettleAsync(new(tenantId,row.RefundId,mode,reference,date,"RAZORPAY_CONFIRMED"),token);

    private async Task MarkUnknown(Guid tenantId,Guid attemptId,CancellationToken token)
    {
        await using var connection=await Open(tenantId,token);
        await using var command=new SqlCommand("""
            UPDATE commerce.StorefrontRefundAttempts SET Status=N'UNKNOWN',
                SafeFailureCode=N'PROVIDER_OUTCOME_UNKNOWN'
            WHERE TenantId=@tenant AND RefundAttemptId=@attemptId AND Status=N'STARTED';
            """,connection);
        P(command,"@tenant",tenantId); P(command,"@attemptId",attemptId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<(Guid AttemptId,string Status,string? ProviderRefundId)> LatestAttempt(Guid tenantId,
        Guid refundId,CancellationToken token)
    {
        await using var connection=await Open(tenantId,token);
        await using var command=new SqlCommand("""
            SELECT TOP(1) RefundAttemptId,Status,ProviderRefundId FROM commerce.StorefrontRefundAttempts
            WHERE TenantId=@tenant AND RefundId=@refund ORDER BY AttemptNumber DESC;
            """,connection);
        P(command,"@tenant",tenantId); P(command,"@refund",refundId);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)) throw new BusinessRuleException("Refund attempt is missing.");
        return (reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2));
    }

    private async Task<PaymentGatewayConfiguration> RazorpayConfiguration(Guid tenantId,CancellationToken token)
    {
        await using var connection=await Open(tenantId,token);
        await using var command=new SqlCommand("""
            SELECT KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode
            FROM commerce.TenantPaymentProviders WHERE TenantId=@tenant AND Provider=N'RAZORPAY';
            """,connection);
        P(command,"@tenant",tenantId);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)) throw new BusinessRuleException("Razorpay configuration is unavailable.");
        var key=reader.IsDBNull(0)?null:reader.GetString(0);
        var secret=reader.IsDBNull(1)?null:reader.GetString(1);
        var webhook=reader.IsDBNull(2)?null:reader.GetString(2);
        var test=reader.GetBoolean(3);
        try
        {
            var protector=protection.CreateProtector(SecretPurpose);
            return new(PaymentProviders.Razorpay,key,secret is null?null:protector.Unprotect(secret),
                webhook is null?null:protector.Unprotect(webhook),test,null,null);
        }
        catch(CryptographicException)
        { throw new BusinessRuleException("Stored Razorpay configuration cannot be used for refund."); }
    }

    private async Task<RefundRow> Read(Guid tenantId,Guid refundId,CancellationToken token)
    {
        await using var connection=await Open(tenantId,token);
        await using var command=new SqlCommand("""
            SELECT r.RefundId,r.InvoiceId,r.Provider,r.RefundAmount,r.RefundStatus,
                r.ProviderRefundId,r.SettlementMode,r.SettlementReference,r.RefundedAt,
                p.ProviderPaymentId,p.Currency
            FROM commerce.StorefrontRefunds r
            JOIN commerce.CommercePayments p ON p.PaymentId=r.PaymentId AND p.TenantId=r.TenantId
                AND p.InvoiceId=r.InvoiceId
            WHERE r.TenantId=@tenant AND r.RefundId=@refund;
            """,connection);
        P(command,"@tenant",tenantId); P(command,"@refund",refundId);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)) throw new BusinessRuleException("Refund was not found for this tenant.");
        return new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3),
            reader.GetString(4),S(reader,5),S(reader,6),S(reader,7),
            reader.IsDBNull(8)?null:reader.GetDateTimeOffset(8),S(reader,9),reader.GetString(10));
    }

    private async Task<SqlConnection> Open(Guid tenantId,CancellationToken token)
    {
        if(tenantId==Guid.Empty) throw new System.UnauthorizedAccessException("A trusted tenant is required.");
        var connection=new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context=new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant",connection);
        P(context,"@tenant",tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }
    private static string? S(SqlDataReader reader,int ordinal)
        => reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static void P(SqlCommand command,string name,object? value)
        => command.Parameters.AddWithValue(name,value??DBNull.Value);
    private sealed record RefundRow(Guid RefundId,Guid InvoiceId,string Provider,decimal Amount,string Status,
        string? ProviderRefundId,string? SettlementMode,string? SettlementReference,
        DateTimeOffset? RefundedAt,string? ProviderPaymentId,string Currency)
    {
        public StorefrontRefundDto ToDto() => new(RefundId,InvoiceId,Provider,Amount,Status,
            ProviderRefundId,SettlementMode,SettlementReference,RefundedAt);
    }
}
