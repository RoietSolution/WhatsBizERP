using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Loyalty;
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Infrastructure.Payments;

public sealed class CommercePaymentService(IConfiguration configuration, IDataProtectionProvider protection,
    ICurrentUserService currentUser, IPaymentGatewayResolver gateways, ILoyaltyService? loyalty = null) : ICommercePaymentService
{
    private const string Purpose = "WhatsBiz.Payments.Razorpay.Secrets.v1";
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
    private Guid Tenant => currentUser.TenantId ?? throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("A tenant context is required.");
    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public async Task<PaymentSettingsDto> GetSettingsAsync(CancellationToken token) => await GetSettings(Tenant, token);
    public Task<PaymentSettingsDto> GetSettingsForTenantAsync(Guid trustedTenantId,CancellationToken token)=>GetSettings(trustedTenantId,token);

    public async Task<PaymentSettingsDto> SaveRazorpayAsync(SaveRazorpayConfiguration input, string actor, CancellationToken token)
    {
        var tenant = Tenant; var keyId = input.KeyId?.Trim();
        await SaveProvider(tenant, PaymentProviders.Razorpay, input.IsEnabled, input.IsDefault, input.IsTestMode,
            keyId, input.KeySecret, input.WebhookSecret, null, null, actor, token);
        return await GetSettings(tenant, token);
    }
    public async Task<PaymentSettingsDto> SaveRazorpayForTenantAsync(Guid trustedTenantId,SaveRazorpayConfiguration input,string actor,CancellationToken token)
    {var keyId=input.KeyId?.Trim();await SaveProvider(trustedTenantId,PaymentProviders.Razorpay,input.IsEnabled,input.IsDefault,input.IsTestMode,keyId,input.KeySecret,input.WebhookSecret,null,null,actor,token);return await GetSettings(trustedTenantId,token);}

    public async Task<PaymentSettingsDto> SaveDirectUpiAsync(SaveDirectUpiConfiguration input, string actor, CancellationToken token)
    {
        var tenant = Tenant; var vpa = input.UpiVpa?.Trim().ToLowerInvariant(); var payee = input.PayeeName?.Trim();
        if (!PaymentValidation.IsValidVpa(vpa)) throw new BusinessRuleException("Enter a valid UPI ID/VPA.");
        if (string.IsNullOrWhiteSpace(payee) || payee.Length > 200) throw new BusinessRuleException("Enter a valid payee name.");
        await SaveProvider(tenant, PaymentProviders.DirectUpi, input.IsEnabled, input.IsDefault, false, null, null, null, vpa, payee, actor, token);
        return await GetSettings(tenant, token);
    }
    public async Task<PaymentSettingsDto> SaveDirectUpiForTenantAsync(Guid trustedTenantId,SaveDirectUpiConfiguration input,string actor,CancellationToken token)
    {var vpa=input.UpiVpa?.Trim().ToLowerInvariant();var payee=input.PayeeName?.Trim();if(!PaymentValidation.IsValidVpa(vpa))throw new BusinessRuleException("Enter a valid UPI ID/VPA.");if(string.IsNullOrWhiteSpace(payee)||payee.Length>200)throw new BusinessRuleException("Enter a valid payee name.");await SaveProvider(trustedTenantId,PaymentProviders.DirectUpi,input.IsEnabled,input.IsDefault,false,null,null,null,vpa,payee,actor,token);return await GetSettings(trustedTenantId,token);}

    public async Task<PaymentSettingsDto> SaveCodAsync(SaveCodConfiguration input, string actor, CancellationToken token)
    {
        var tenant = Tenant;
        await SaveProvider(tenant, PaymentProviders.Cod, input.IsEnabled, input.IsDefault, false, null, null, null, null, null, actor, token);
        return await GetSettings(tenant, token);
    }
    public async Task<PaymentSettingsDto> SaveCodForTenantAsync(Guid trustedTenantId,SaveCodConfiguration input,string actor,CancellationToken token)
    {await SaveProvider(trustedTenantId,PaymentProviders.Cod,input.IsEnabled,input.IsDefault,false,null,null,null,null,null,actor,token);return await GetSettings(trustedTenantId,token);}

    public async Task<PaymentSettingsDto> SaveOptionsAsync(SavePaymentOptions input, string actor, CancellationToken token)
    {
        var tenant=Tenant;await using var c=await Open(tenant,token);await using var q=new SqlCommand(@"MERGE commerce.TenantPaymentConfigurations t USING(SELECT @tenant TenantId)s ON s.TenantId=t.TenantId
WHEN MATCHED THEN UPDATE SET OnlinePaymentEnabled=@enabled,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHEN NOT MATCHED THEN INSERT(TenantId,OnlinePaymentEnabled,CreatedBy)VALUES(@tenant,@enabled,@actor);",c);P(q,"@tenant",tenant);P(q,"@enabled",input.OnlinePaymentEnabled);P(q,"@actor",actor);await q.ExecuteNonQueryAsync(token);return await GetSettings(tenant,token);
    }
    public async Task<PaymentSettingsDto> SaveOptionsForTenantAsync(Guid trustedTenantId,SavePaymentOptions input,string actor,CancellationToken token)
    {await using var c=await Open(trustedTenantId,token);await using var q=new SqlCommand(@"MERGE commerce.TenantPaymentConfigurations t USING(SELECT @tenant TenantId)s ON s.TenantId=t.TenantId
WHEN MATCHED THEN UPDATE SET OnlinePaymentEnabled=@enabled,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHEN NOT MATCHED THEN INSERT(TenantId,OnlinePaymentEnabled,CreatedBy)VALUES(@tenant,@enabled,@actor);",c);P(q,"@tenant",trustedTenantId);P(q,"@enabled",input.OnlinePaymentEnabled);P(q,"@actor",actor);await q.ExecuteNonQueryAsync(token);return await GetSettings(trustedTenantId,token);}

    public async Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsAsync(CancellationToken token)
        => await GetEnabledMethodsForTenantAsync(Tenant, token);

    public async Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsForTenantAsync(Guid trustedTenantId, CancellationToken token)
    {
        var settings = await GetSettings(trustedTenantId, token);
        return settings.Providers.Where(x => x.IsEnabled && x.IsConfigured && (x.Provider==PaymentProviders.Cod||settings.OnlinePaymentEnabled)).Select(x => new EnabledPaymentMethod(x.Provider,
            x.Provider switch { PaymentProviders.Razorpay => "Pay Online", PaymentProviders.DirectUpi => "Pay via UPI", _ => "Cash on Delivery" }, x.IsDefault)).ToArray();
    }

    public async Task<PaymentAttemptResult> CreateAttemptAsync(CreatePaymentAttemptInput input, string actor, CancellationToken token)
        => await CreateAttemptForTenantAsync(Tenant, input, actor, token);

    public async Task<PaymentAttemptResult> CreateAttemptForTenantAsync(Guid trustedTenantId, CreatePaymentAttemptInput input, string actor, CancellationToken token)
    {
        var tenant = trustedTenantId; var provider = NormalizeProvider(input.Provider);
        InvoiceRow invoice; ConfigRow config; Guid paymentId = Guid.NewGuid(); int attempt; string reference;
        await using (var connection = await Open(tenant, token))
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(token))
        {
            invoice = await ReadInvoice(connection, tx, tenant, input.OrderId, true, token);
            if (invoice.Balance <= 0) throw new BusinessRuleException("This order has no outstanding balance.");
            config = await ReadConfig(connection, tx, tenant, provider, token) ?? throw new BusinessRuleException("The selected payment method is not configured.");
            if (!config.Enabled || !IsConfigured(config)) throw new BusinessRuleException("The selected payment method is not available.");
            if (provider!=PaymentProviders.Cod)
            { await using var online=new SqlCommand("SELECT ISNULL((SELECT OnlinePaymentEnabled FROM commerce.TenantPaymentConfigurations WHERE TenantId=@tenant),0)",connection,tx);P(online,"@tenant",tenant);if(!Convert.ToBoolean(await online.ExecuteScalarAsync(token),CultureInfo.InvariantCulture))throw new BusinessRuleException("Online payments are disabled for this tenant."); }
            await using (var count = new SqlCommand("SELECT ISNULL(MAX(AttemptNumber),0)+1 FROM commerce.CommercePayments WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND InvoiceId=@invoice", connection, tx))
            { P(count,"@tenant",tenant);P(count,"@invoice",input.OrderId);attempt=Convert.ToInt32(await count.ExecuteScalarAsync(token),CultureInfo.InvariantCulture); }
            reference = $"KD{paymentId:N}"[..34];
            var status = provider == PaymentProviders.DirectUpi ? CommercePaymentStatuses.PendingVerification : provider == PaymentProviders.Cod ? CommercePaymentStatuses.CodPending : CommercePaymentStatuses.Pending;
            await using var insert = new SqlCommand(@"INSERT commerce.CommercePayments(PaymentId,TenantId,InvoiceId,Provider,PaymentMethod,Amount,Currency,Status,AttemptNumber,TransactionReference,CreatedBy)
VALUES(@id,@tenant,@invoice,@provider,@provider,@amount,N'INR',@status,@attempt,@reference,@actor);", connection, tx);
            P(insert,"@id",paymentId);P(insert,"@tenant",tenant);P(insert,"@invoice",input.OrderId);P(insert,"@provider",provider);P(insert,"@amount",invoice.Balance);P(insert,"@status",status);P(insert,"@attempt",attempt);P(insert,"@reference",reference);P(insert,"@actor",actor);await insert.ExecuteNonQueryAsync(token);
            if (provider == PaymentProviders.Cod)
            { await using var cod = new SqlCommand("UPDATE integration.WhatsAppCommerceOrders SET PaymentType=N'COD' WHERE TenantId=@tenant AND InvoiceId=@invoice",connection,tx);P(cod,"@tenant",tenant);P(cod,"@invoice",input.OrderId);await cod.ExecuteNonQueryAsync(token); }
            await tx.CommitAsync(token);
        }
        GatewayCreateResult created;
        try
        {
            created = await gateways.Resolve(provider).CreatePaymentAsync(ToGateway(config), new(paymentId,input.OrderId,invoice.Number,invoice.Balance,"INR",reference,invoice.CustomerName,invoice.Mobile), token);
        }
        catch
        {
            await UpdateFailed(tenant,paymentId,token); throw;
        }
        await using (var connection = await Open(tenant, token))
        await using (var command = new SqlCommand("UPDATE commerce.CommercePayments SET ProviderOrderId=@orderId,ProviderReference=@providerReference,PaymentLink=@link,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@id AND Status IN(N'PENDING',N'PENDING_VERIFICATION',N'COD_PENDING')", connection))
        { P(command,"@orderId",created.ProviderOrderId);P(command,"@providerReference",created.ProviderReference);P(command,"@link",created.PaymentLink);P(command,"@tenant",tenant);P(command,"@id",paymentId);await command.ExecuteNonQueryAsync(token); }
        return new(await GetPayment(tenant, paymentId, token), created.PaymentAction);
    }

    public async Task<CommercePaymentDto> GetPaymentAsync(Guid paymentId, CancellationToken token)
        => await GetPayment(Tenant, paymentId, token);

    private async Task<CommercePaymentDto> GetPayment(Guid tenant, Guid paymentId, CancellationToken token)
    {
        await using var c=await Open(tenant,token);await using var q=new SqlCommand(SelectPayment+" WHERE p.TenantId=@tenant AND p.PaymentId=@id",c);P(q,"@tenant",tenant);P(q,"@id",paymentId);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?Map(r):throw new EntityNotFoundException("Payment was not found.");
    }

    public async Task<CommercePaymentDto> GetPaymentForAdministrationAsync(Guid paymentId,CancellationToken token)
    {
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand(SelectPayment+" WHERE p.PaymentId=@id",c);P(q,"@id",paymentId);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?Map(r):throw new EntityNotFoundException("Payment was not found.");
    }

    public Task<PagedPaymentsDto> ListAsync(PaymentQuery query,CancellationToken token)=>ListPayments(Tenant,null,query.DateFrom,query.DateTo,query.Status,query.Provider,query.PageNumber,query.PageSize,token);
    public Task<PagedPaymentsDto> ListForAdministrationAsync(PlatformPaymentQuery query,CancellationToken token)=>ListPayments(null,query.TenantId,query.DateFrom,query.DateTo,query.Status,query.Provider,query.PageNumber,query.PageSize,token);

    private async Task<PagedPaymentsDto> ListPayments(Guid? retailerTenant,Guid? selectedTenant,DateTimeOffset? from,DateTimeOffset? to,string? status,string? provider,int pageNumber,int pageSize,CancellationToken token)
    {
        var tenant=retailerTenant??selectedTenant;var page=Math.Max(1,pageNumber);var size=Math.Clamp(pageSize,1,100);
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        var where=" WHERE (@tenant IS NULL OR p.TenantId=@tenant) AND (@from IS NULL OR p.CreatedAt>=@from) AND (@to IS NULL OR p.CreatedAt<DATEADD(day,1,@to)) AND (@status IS NULL OR p.Status=@status) AND (@provider IS NULL OR p.Provider=@provider)";
        var normalizedStatus=string.IsNullOrWhiteSpace(status)?null:status.Trim().ToUpperInvariant();var normalizedProvider=string.IsNullOrWhiteSpace(provider)?null:NormalizeProvider(provider);
        await using var count=new SqlCommand("SELECT COUNT_BIG(*) FROM commerce.CommercePayments p"+where,c);AddFilters(count,tenant,from,to,normalizedStatus,normalizedProvider);var total=Convert.ToInt64(await count.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);
        await using var q=new SqlCommand(SelectPayment+where+" ORDER BY p.CreatedAt DESC,p.PaymentId DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",c);AddFilters(q,tenant,from,to,normalizedStatus,normalizedProvider);P(q,"@skip",(page-1)*size);P(q,"@take",size);
        await using var r=await q.ExecuteReaderAsync(token);var rows=new List<CommercePaymentDto>();while(await r.ReadAsync(token))rows.Add(Map(r));return new(rows,total,page,size);
    }

    private static void AddFilters(SqlCommand command,Guid? tenant,DateTimeOffset? from,DateTimeOffset? to,string? status,string? provider)
    {P(command,"@tenant",tenant);P(command,"@from",from);P(command,"@to",to);P(command,"@status",status);P(command,"@provider",provider);}

    public async Task<CommercePaymentDto> VerifyDirectUpiAsync(Guid paymentId, VerifyDirectUpiInput input, Guid userId, string actor, CancellationToken token)
    {
        var tenant=Tenant;await ApplySuccessfulPayment(tenant,paymentId,PaymentProviders.DirectUpi,null,input.Reference,userId,actor,token);return await GetPaymentAsync(paymentId,token);
    }

    public async Task ProcessRazorpayWebhookAsync(ReadOnlyMemory<byte> rawBody, string signature, string? eventId, CancellationToken token)
    {
        var correlation = WebhookCorrelation(rawBody);
        if (correlation.PaymentId is null && correlation.ProviderReference is null && correlation.ProviderOrderId is null) throw new BusinessRuleException("Razorpay webhook cannot be correlated.");
        PaymentCorrelation payment;
        await using (var c=new SqlConnection(ConnectionString))
        { await c.OpenAsync(token);await using var q=new SqlCommand(@"SELECT TOP(1)p.PaymentId,p.TenantId,p.Amount,p.Currency,p.Status,c.KeyId,c.KeySecretProtected,c.WebhookSecretProtected,c.IsTestMode
FROM commerce.CommercePayments p JOIN commerce.TenantPaymentProviders c ON c.TenantId=p.TenantId AND c.Provider=N'RAZORPAY' AND c.IsEnabled=1
WHERE p.Provider=N'RAZORPAY' AND ((@paymentId IS NOT NULL AND p.PaymentId=@paymentId)OR(@reference IS NOT NULL AND p.ProviderReference=@reference)OR(@orderId IS NOT NULL AND p.ProviderOrderId=@orderId));",c);P(q,"@paymentId",correlation.PaymentId);P(q,"@reference",correlation.ProviderReference);P(q,"@orderId",correlation.ProviderOrderId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Razorpay payment correlation was not found.");payment=new(r.GetGuid(0),r.GetGuid(1),r.GetDecimal(2),r.GetString(3),r.GetString(4),r.GetString(5),S(r,6),S(r,7),r.GetBoolean(8)); }
        var config=new PaymentGatewayConfiguration(PaymentProviders.Razorpay,payment.KeyId,Unprotect(payment.KeySecret),Unprotect(payment.WebhookSecret),payment.TestMode,null,null);
        var verified=gateways.Resolve(PaymentProviders.Razorpay).VerifyWebhook(config,rawBody,signature,eventId);
        if(!verified.SignatureValid)throw new System.UnauthorizedAccessException("Razorpay webhook signature is invalid.");
        if(verified.ProviderReference is not null&&!verified.ProviderReference.Equals(correlation.ProviderReference,StringComparison.Ordinal))throw new BusinessRuleException("Razorpay webhook correlation does not match.");
        var payloadHash=Convert.ToHexString(SHA256.HashData(rawBody.Span));var stableEvent=string.IsNullOrWhiteSpace(verified.EventId)?payloadHash:verified.EventId!;
        await using(var c=await Open(payment.TenantId,token))await using(var tx=(SqlTransaction)await c.BeginTransactionAsync(token))
        {
            await using var add=new SqlCommand(@"IF EXISTS(SELECT 1 FROM commerce.PaymentProviderEvents WITH(UPDLOCK,HOLDLOCK) WHERE Provider=N'RAZORPAY' AND ProviderEventId=@event) SELECT 0;
ELSE BEGIN INSERT commerce.PaymentProviderEvents(PaymentProviderEventId,TenantId,PaymentId,Provider,ProviderEventId,EventType,PayloadHash)VALUES(NEWID(),@tenant,@payment,N'RAZORPAY',@event,@type,@hash);SELECT 1;END",c,tx);
            P(add,"@event",stableEvent);P(add,"@tenant",payment.TenantId);P(add,"@payment",payment.PaymentId);P(add,"@type",verified.EventType);P(add,"@hash",payloadHash);var added=Convert.ToInt32(await add.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);if(added==0){await tx.CommitAsync(token);return;}
            if(verified.Amount is not null&&verified.Amount!=payment.Amount)throw new BusinessRuleException("Razorpay webhook amount does not match the server order.");
            if(verified.Currency is not null&&!verified.Currency.Equals(payment.Currency,StringComparison.OrdinalIgnoreCase))throw new BusinessRuleException("Razorpay webhook currency does not match the server order.");
            if(verified.IsPaid)await ApplySuccessfulPayment(c,tx,payment.TenantId,payment.PaymentId,PaymentProviders.Razorpay,verified.ProviderPaymentId,verified.ProviderReference,null,"RAZORPAY_WEBHOOK",token);
            else if(verified.IsFailed){await using var fail=new SqlCommand("UPDATE commerce.CommercePayments SET Status=CASE WHEN Status=N'PAID' THEN Status ELSE N'FAILED' END,FailedAt=CASE WHEN Status=N'PAID' THEN FailedAt ELSE SYSUTCDATETIME() END,ProviderPaymentId=COALESCE(@providerPayment,ProviderPaymentId),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment",c,tx);P(fail,"@providerPayment",verified.ProviderPaymentId);P(fail,"@tenant",payment.TenantId);P(fail,"@payment",payment.PaymentId);await fail.ExecuteNonQueryAsync(token);}
            await using var done=new SqlCommand("UPDATE commerce.PaymentProviderEvents SET ProcessedAt=SYSUTCDATETIME() WHERE Provider=N'RAZORPAY' AND ProviderEventId=@event",c,tx);P(done,"@event",stableEvent);await done.ExecuteNonQueryAsync(token);await tx.CommitAsync(token);
        }
        if(verified.IsPaid&&loyalty is not null)await loyalty.ProcessOrderAsync(payment.TenantId,(await OrderId(payment.TenantId,payment.PaymentId,token)),"COMPLETED","RAZORPAY_WEBHOOK",token);
    }

    private async Task ApplySuccessfulPayment(Guid tenant,Guid paymentId,string provider,string? providerPayment,string? reference,Guid userId,string actor,CancellationToken token)
    { await using var c=await Open(tenant,token);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(token);await ApplySuccessfulPayment(c,tx,tenant,paymentId,provider,providerPayment,reference,userId,actor,token);await tx.CommitAsync(token);if(loyalty is not null)await loyalty.ProcessOrderAsync(tenant,await OrderId(tenant,paymentId,token),"COMPLETED",actor,token); }

    private static async Task ApplySuccessfulPayment(SqlConnection c,SqlTransaction tx,Guid tenant,Guid paymentId,string provider,string? providerPayment,string? reference,Guid? userId,string actor,CancellationToken token)
    {
        Guid invoice;decimal amount,balance;string currency,status,invoiceStatus;
        await using(var q=new SqlCommand(@"SELECT p.InvoiceId,p.Amount,p.Currency,p.Status,i.BalanceAmount,i.Status FROM commerce.CommercePayments p WITH(UPDLOCK,HOLDLOCK) JOIN sales.SalesInvoices i WITH(UPDLOCK) ON i.InvoiceId=p.InvoiceId AND i.TenantId=p.TenantId WHERE p.TenantId=@tenant AND p.PaymentId=@payment AND p.Provider=@provider",c,tx))
        {P(q,"@tenant",tenant);P(q,"@payment",paymentId);P(q,"@provider",provider);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Payment was not found for this tenant.");invoice=r.GetGuid(0);amount=r.GetDecimal(1);currency=r.GetString(2);status=r.GetString(3);balance=r.GetDecimal(4);invoiceStatus=r.GetString(5);}
        if(status==CommercePaymentStatuses.Paid)return;
        if(provider==PaymentProviders.DirectUpi&&status!=CommercePaymentStatuses.PendingVerification)throw new BusinessRuleException("Only a Direct UPI payment pending verification can be verified.");
        if(currency!="INR"||amount!=balance)throw new BusinessRuleException("Payment amount or currency no longer matches the order balance.");
        var method=provider==PaymentProviders.Razorpay?"RAZORPAY":"UPI";
        await using(var pay=new SqlCommand("sales.POS_AddPayment",c,tx){CommandType=CommandType.StoredProcedure}){P(pay,"@InvoiceId",invoice);P(pay,"@MethodCode",method);P(pay,"@Amount",amount);P(pay,"@ReferenceNumber",reference??providerPayment);P(pay,"@CreatedBy",actor);P(pay,"@TenantId",tenant);await pay.ExecuteNonQueryAsync(token);}
        if(invoiceStatus is "HELD" or "SUSPENDED"){await using var complete=new SqlCommand("sales.POS_TransitionHeldInvoice",c,tx){CommandType=CommandType.StoredProcedure};P(complete,"@InvoiceId",invoice);P(complete,"@Action","COMPLETE");P(complete,"@ModifiedBy",actor);await complete.ExecuteNonQueryAsync(token);}
        await using(var app=new SqlCommand("INSERT commerce.PaymentApplications(PaymentApplicationId,TenantId,PaymentId,InvoiceId,Amount,Currency,AppliedBy)VALUES(NEWID(),@tenant,@payment,@invoice,@amount,@currency,@actor)",c,tx)){P(app,"@tenant",tenant);P(app,"@payment",paymentId);P(app,"@invoice",invoice);P(app,"@amount",amount);P(app,"@currency",currency);P(app,"@actor",actor);await app.ExecuteNonQueryAsync(token);}
        await using(var update=new SqlCommand("UPDATE commerce.CommercePayments SET Status=N'PAID',ProviderPaymentId=COALESCE(@providerPayment,ProviderPaymentId),ProviderReference=COALESCE(@reference,ProviderReference),PaidAt=SYSUTCDATETIME(),VerifiedAt=CASE WHEN @user IS NULL THEN VerifiedAt ELSE SYSUTCDATETIME() END,VerifiedBy=COALESCE(@user,VerifiedBy),AppliedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment",c,tx)){P(update,"@providerPayment",providerPayment);P(update,"@reference",reference);P(update,"@user",userId);P(update,"@tenant",tenant);P(update,"@payment",paymentId);await update.ExecuteNonQueryAsync(token);}
    }

    private async Task SaveProvider(Guid tenant,string provider,bool enabled,bool isDefault,bool testMode,string? keyId,string? keySecret,string? webhookSecret,string? vpa,string? payee,string actor,CancellationToken token)
    {
        await using var c=await Open(tenant,token);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(token);var existing=await ReadConfig(c,tx,tenant,provider,token);
        var effectiveKeyId=provider==PaymentProviders.Razorpay&&string.IsNullOrWhiteSpace(keyId)?existing?.KeyId:keyId;
        var protectedKey=ReplaceSecret(keySecret,existing?.KeySecret);var protectedWebhook=ReplaceSecret(webhookSecret,existing?.WebhookSecret);
        if(provider==PaymentProviders.Razorpay&&enabled&&(effectiveKeyId is null||protectedKey is null||protectedWebhook is null))throw new BusinessRuleException("Razorpay Key ID, Key Secret and Webhook Secret are required before enabling.");
        if(isDefault&&!enabled)throw new BusinessRuleException("A disabled payment method cannot be the default.");
        if(isDefault){await using var clear=new SqlCommand("UPDATE commerce.TenantPaymentProviders SET IsDefault=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE TenantId=@tenant AND IsDefault=1 AND Provider<>@provider",c,tx);P(clear,"@actor",actor);P(clear,"@tenant",tenant);P(clear,"@provider",provider);await clear.ExecuteNonQueryAsync(token);}
        await using var q=new SqlCommand(@"MERGE commerce.TenantPaymentProviders t USING(SELECT @tenant TenantId,@provider Provider)s ON s.TenantId=t.TenantId AND s.Provider=t.Provider
WHEN MATCHED THEN UPDATE SET IsEnabled=@enabled,IsDefault=@default,KeyId=@keyId,KeySecretProtected=@keySecret,WebhookSecretProtected=@webhook,IsTestMode=@test,UpiVpa=@vpa,PayeeName=@payee,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHEN NOT MATCHED THEN INSERT(PaymentProviderId,TenantId,Provider,IsEnabled,IsDefault,KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode,UpiVpa,PayeeName,CreatedBy)VALUES(NEWID(),@tenant,@provider,@enabled,@default,@keyId,@keySecret,@webhook,@test,@vpa,@payee,@actor);",c,tx);
        foreach(var x in new[]{("@tenant",(object?)tenant),("@provider",provider),("@enabled",enabled),("@default",isDefault),("@keyId",effectiveKeyId),("@keySecret",protectedKey),("@webhook",protectedWebhook),("@test",testMode),("@vpa",vpa),("@payee",payee),("@actor",actor)})P(q,x.Item1,x.Item2);await q.ExecuteNonQueryAsync(token);
        await using(var parent=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM commerce.TenantPaymentConfigurations WHERE TenantId=@tenant) INSERT commerce.TenantPaymentConfigurations(TenantId,OnlinePaymentEnabled,CreatedBy)VALUES(@tenant,@online,@actor)",c,tx)){P(parent,"@tenant",tenant);P(parent,"@online",provider!=PaymentProviders.Cod&&enabled);P(parent,"@actor",actor);await parent.ExecuteNonQueryAsync(token);}await tx.CommitAsync(token);
    }

    private async Task<PaymentSettingsDto> GetSettings(Guid tenant,CancellationToken token)
    {await using var c=await Open(tenant,token);bool? online;await using(var state=new SqlCommand("SELECT OnlinePaymentEnabled FROM commerce.TenantPaymentConfigurations WHERE TenantId=@tenant",c)){P(state,"@tenant",tenant);var value=await state.ExecuteScalarAsync(token);online=value is null or DBNull?null:Convert.ToBoolean(value,CultureInfo.InvariantCulture);}await using var q=new SqlCommand("SELECT Provider,IsEnabled,IsDefault,KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode,UpiVpa,PayeeName FROM commerce.TenantPaymentProviders WHERE TenantId=@tenant",c);P(q,"@tenant",tenant);await using var r=await q.ExecuteReaderAsync(token);var found=new Dictionary<string,PaymentProviderSetting>(StringComparer.Ordinal);while(await r.ReadAsync(token)){var provider=r.GetString(0);var key=S(r,3);var keySecret=!r.IsDBNull(4);var webhook=!r.IsDBNull(5);var vpa=S(r,7);var payee=S(r,8);var configured=provider switch{PaymentProviders.Razorpay=>key is not null&&keySecret&&webhook,PaymentProviders.DirectUpi=>PaymentValidation.IsValidVpa(vpa)&&!string.IsNullOrWhiteSpace(payee),_=>true};found[provider]=new(provider,r.GetBoolean(1),r.GetBoolean(2),configured,key is null?null:PaymentValidation.MaskKeyId(key),keySecret,webhook,r.GetBoolean(6),vpa,payee);}var list=new[]{PaymentProviders.Razorpay,PaymentProviders.DirectUpi,PaymentProviders.Cod}.Select(p=>found.TryGetValue(p,out var x)?x:new PaymentProviderSetting(p,false,false,p==PaymentProviders.Cod,null,false,false,true,null,null)).ToArray();return new(online??list.Any(x=>x.Provider!=PaymentProviders.Cod&&x.IsEnabled),list);}

    private async Task<SqlConnection> Open(Guid tenant,CancellationToken token){var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant",c);P(q,"@tenant",tenant);await q.ExecuteNonQueryAsync(token);return c;}
    private string? ReplaceSecret(string? replacement,string? existing)=>string.IsNullOrWhiteSpace(replacement)?existing:Protector.Protect(replacement.Trim());
    private string? Unprotect(string? value){if(value is null)return null;try{return Protector.Unprotect(value);}catch(CryptographicException){throw new BusinessRuleException("Stored Razorpay credentials cannot be decrypted. Replace the retailer configuration.");}}
    private static string NormalizeProvider(string value){var p=value?.Trim().ToUpperInvariant()??string.Empty;if(!PaymentProviders.All.Contains(p))throw new BusinessRuleException("Select a supported payment provider.");return p;}
    private static bool IsConfigured(ConfigRow x)=>x.Provider switch{PaymentProviders.Razorpay=>x.KeyId is not null&&x.KeySecret is not null&&x.WebhookSecret is not null,PaymentProviders.DirectUpi=>PaymentValidation.IsValidVpa(x.Vpa)&&!string.IsNullOrWhiteSpace(x.Payee),_=>true};
    private PaymentGatewayConfiguration ToGateway(ConfigRow x)=>new(x.Provider,x.KeyId,Unprotect(x.KeySecret),Unprotect(x.WebhookSecret),x.TestMode,x.Vpa,x.Payee);
    private static async Task<ConfigRow?> ReadConfig(SqlConnection c,SqlTransaction? tx,Guid tenant,string provider,CancellationToken token){await using var q=new SqlCommand("SELECT Provider,IsEnabled,KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode,UpiVpa,PayeeName FROM commerce.TenantPaymentProviders WHERE TenantId=@tenant AND Provider=@provider",c,tx);P(q,"@tenant",tenant);P(q,"@provider",provider);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetString(0),r.GetBoolean(1),S(r,2),S(r,3),S(r,4),r.GetBoolean(5),S(r,6),S(r,7)):null;}
    private static async Task<InvoiceRow> ReadInvoice(SqlConnection c,SqlTransaction tx,Guid tenant,Guid invoice,bool locked,CancellationToken token){await using var q=new SqlCommand($"SELECT i.InvoiceNumber,i.BalanceAmount,c.CustomerName,c.Mobile FROM sales.SalesInvoices i {(locked?"WITH(UPDLOCK,HOLDLOCK)":"")} LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId WHERE i.TenantId=@tenant AND i.InvoiceId=@invoice AND i.Status NOT IN(N'CANCELLED',N'VOID',N'RETURNED')",c,tx);P(q,"@tenant",tenant);P(q,"@invoice",invoice);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetString(0),r.GetDecimal(1),S(r,2),S(r,3)):throw new EntityNotFoundException("Active order was not found for this tenant.");}
    private async Task UpdateFailed(Guid tenant,Guid payment,CancellationToken token){await using var c=await Open(tenant,token);await using var q=new SqlCommand("UPDATE commerce.CommercePayments SET Status=N'FAILED',FailedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment AND Status=N'PENDING'",c);P(q,"@tenant",tenant);P(q,"@payment",payment);await q.ExecuteNonQueryAsync(token);}
    private async Task<Guid> OrderId(Guid tenant,Guid payment,CancellationToken token){await using var c=await Open(tenant,token);await using var q=new SqlCommand("SELECT InvoiceId FROM commerce.CommercePayments WHERE TenantId=@tenant AND PaymentId=@payment",c);P(q,"@tenant",tenant);P(q,"@payment",payment);return (Guid)(await q.ExecuteScalarAsync(token)??throw new EntityNotFoundException("Payment was not found."));}
    private static (Guid? PaymentId,string? ProviderReference,string? ProviderOrderId) WebhookCorrelation(ReadOnlyMemory<byte> raw){try{using var j=JsonDocument.Parse(raw);var p=j.RootElement.GetProperty("payload");string? link=null,order=null,internalId=null;if(p.TryGetProperty("payment_link",out var lw)&&lw.TryGetProperty("entity",out var le)){if(le.TryGetProperty("id",out var li))link=li.GetString();if(le.TryGetProperty("notes",out var ln)&&ln.TryGetProperty("payment_id",out var lp))internalId=lp.GetString();}if(p.TryGetProperty("payment",out var pw)&&pw.TryGetProperty("entity",out var pe)){if(pe.TryGetProperty("order_id",out var oi))order=oi.GetString();if(internalId is null&&pe.TryGetProperty("notes",out var pn)&&pn.TryGetProperty("payment_id",out var pp))internalId=pp.GetString();}return(Guid.TryParseExact(internalId,"N",out var id)?id:null,link,order);}catch(JsonException){throw new BusinessRuleException("Razorpay webhook payload is invalid.");}}
    private const string SelectPayment=@"SELECT p.PaymentId,p.TenantId,t.Name,p.InvoiceId,i.InvoiceNumber,c.CustomerName,p.Provider,p.Amount,p.Currency,p.Status,p.ProviderOrderId,p.ProviderPaymentId,p.PaymentLink,p.TransactionReference,p.CreatedAt,p.PaidAt,p.VerifiedAt,u.UserName FROM commerce.CommercePayments p JOIN core.Tenants t ON t.TenantId=p.TenantId JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=p.TenantId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=p.TenantId LEFT JOIN core.Users u ON u.Id=p.VerifiedBy AND u.TenantId=p.TenantId";
    private static CommercePaymentDto Map(SqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetGuid(3),r.GetString(4),S(r,5),r.GetString(6),r.GetDecimal(7),r.GetString(8),r.GetString(9),S(r,10),S(r,11),S(r,12),S(r,13),r.GetDateTimeOffset(14),r.IsDBNull(15)?null:r.GetDateTimeOffset(15),r.IsDBNull(16)?null:r.GetDateTimeOffset(16),S(r,17));
    private static void P(SqlCommand q,string name,object? value)=>q.Parameters.AddWithValue(name,value??DBNull.Value);
    private static string? S(SqlDataReader r,int index)=>r.IsDBNull(index)?null:r.GetString(index);
    private sealed record ConfigRow(string Provider,bool Enabled,string? KeyId,string? KeySecret,string? WebhookSecret,bool TestMode,string? Vpa,string? Payee);
    private sealed record InvoiceRow(string Number,decimal Balance,string? CustomerName,string? Mobile);
    private sealed record PaymentCorrelation(Guid PaymentId,Guid TenantId,decimal Amount,string Currency,string Status,string KeyId,string? KeySecret,string? WebhookSecret,bool TestMode);
}
