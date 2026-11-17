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
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Payments;

public sealed class CommercePaymentService(IConfiguration configuration, IDataProtectionProvider protection,
    ICurrentUserService currentUser, IPaymentGatewayResolver gateways, IErpPaymentPosting erpPayments,
    IOutboundSafetyPolicy outboundSafety, ILoyaltyService? loyalty = null,
    SqlIdempotencyExecutor? idempotency = null) : ICommercePaymentService
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
        => await SaveOptionsForTenantAsync(Tenant, input, actor, token);

    public async Task<PaymentSettingsDto> SaveOptionsForTenantAsync(Guid trustedTenantId, SavePaymentOptions input, string actor, CancellationToken token)
    {
        await using var c = await Open(trustedTenantId, token);
        await using var q = new SqlCommand(@"MERGE commerce.TenantPaymentConfigurations t USING(SELECT @tenant TenantId)s ON s.TenantId=t.TenantId
WHEN MATCHED THEN UPDATE SET OnlinePaymentEnabled=@online,UpiEnabled=@upi,NetBankingEnabled=@netBanking,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHEN NOT MATCHED THEN INSERT(TenantId,OnlinePaymentEnabled,UpiEnabled,NetBankingEnabled,CreatedBy)VALUES(@tenant,@online,@upi,@netBanking,@actor);", c);
        P(q, "@tenant", trustedTenantId); P(q, "@online", input.OnlinePaymentEnabled); P(q, "@upi", input.UpiEnabled);
        P(q, "@netBanking", input.NetBankingEnabled); P(q, "@actor", actor);
        await q.ExecuteNonQueryAsync(token);
        return await GetSettings(trustedTenantId, token);
    }
    public async Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsAsync(CancellationToken token)
        => await GetEnabledMethodsForTenantAsync(Tenant, token);

    public async Task<IReadOnlyCollection<EnabledPaymentMethod>> GetEnabledMethodsForTenantAsync(Guid trustedTenantId, CancellationToken token)
    {
        var settings = await GetSettings(trustedTenantId, token);
        var methods = new List<EnabledPaymentMethod>();
        var cod = settings.Providers.Single(x => x.Provider == PaymentProviders.Cod);
        if (cod.IsEnabled && cod.IsConfigured) methods.Add(new("COD", PaymentProviders.Cod, "Cash on Delivery", cod.IsDefault));
        var razorpay = settings.Providers.Single(x => x.Provider == PaymentProviders.Razorpay);
        if (settings.OnlinePaymentEnabled && razorpay.IsEnabled && razorpay.IsConfigured)
        {
            if (settings.UpiEnabled) methods.Add(new("UPI", PaymentProviders.Razorpay, "UPI", razorpay.IsDefault));
            if (settings.NetBankingEnabled) methods.Add(new("NET_BANKING", PaymentProviders.Razorpay, "Net Banking", razorpay.IsDefault && !settings.UpiEnabled));
        }
        return methods;
    }

    public async Task<PaymentAttemptResult> CreateAttemptAsync(CreatePaymentAttemptInput input, string actor, CancellationToken token)
        => await CreateAttemptForTenantAsync(Tenant, input, actor, token);

    public async Task<PaymentAttemptResult> CreateAttemptForTenantAsync(Guid trustedTenantId, CreatePaymentAttemptInput input, string actor, CancellationToken token)
    {
        var tenant = trustedTenantId; var provider = NormalizeProvider(input.Provider);
        if (provider == PaymentProviders.Razorpay) outboundSafety.EnsureExternalDeliveryAllowed("Razorpay payments");
        var paymentMethod = NormalizeMethod(provider, input.PaymentMethod);
        InvoiceRow invoice; ConfigRow config; Guid paymentId = Guid.NewGuid(); int attempt; string reference;
        Guid? replayPaymentId = null;
        await using (var connection = await Open(tenant, token))
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(token))
        {
            invoice = await ReadInvoice(connection, tx, tenant, input.OrderId, true, token);
            if (invoice.Balance <= 0) throw new BusinessRuleException("This order has no outstanding balance.");
            if (invoice.OrderType == "MARKETPLACE" || string.Equals(actor, "STOREFRONT", StringComparison.Ordinal))
            {
                if (invoice.OrderType == "MARKETPLACE")
                {
                    var codReplay = provider == PaymentProviders.Cod && invoice.MarketplaceLifecycleStatus == "POSTED";
                    if (!codReplay && (invoice.MarketplaceLifecycleStatus is not ("RESERVED" or "PAYMENT_PENDING" or "CONFIRMED")
                        || invoice.ReservationExpiresAt is not DateTimeOffset reservationExpiry || reservationExpiry <= DateTimeOffset.UtcNow))
                        throw new BusinessRuleException("Marketplace inventory is no longer reserved. Refresh the order before requesting payment.");
                    if (invoice.PaidAmount != 0 || invoice.Balance != invoice.GrandTotal)
                        throw new BusinessRuleException("Marketplace checkout requires one payment for the full persisted parent invoice amount.");
                }
                await using var activeAttempt = new SqlCommand("""
                    SELECT TOP(1) PaymentId,Provider,PaymentMethod FROM commerce.CommercePayments WITH(UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@tenant AND InvoiceId=@invoice AND Status IN(N'PENDING',N'PENDING_VERIFICATION',N'COD_PENDING')
                    ORDER BY AttemptNumber DESC;
                    """, connection, tx);
                P(activeAttempt,"@tenant",tenant);P(activeAttempt,"@invoice",input.OrderId);
                await using var activeReader = await activeAttempt.ExecuteReaderAsync(token);
                if (await activeReader.ReadAsync(token))
                {
                    var existingProvider = activeReader.GetString(1); var existingMethod = activeReader.GetString(2);
                    if (existingProvider != provider || existingMethod != paymentMethod)
                        throw new BusinessRuleException("A payment attempt is already active for this order.");
                    replayPaymentId = activeReader.GetGuid(0);
                }
                await activeReader.CloseAsync();
                if (replayPaymentId is not null)
                {
                    await tx.CommitAsync(token);
                    var existingPayment = await GetPayment(tenant,replayPaymentId.Value,token);
                    return new(existingPayment,existingPayment.PaymentLink);
                }
            }
            config = await ReadConfig(connection, tx, tenant, provider, token) ?? throw new BusinessRuleException("The selected payment method is not configured.");
            if (!config.Enabled || !IsConfigured(config)) throw new BusinessRuleException("The selected payment method is not available.");
            if (provider == PaymentProviders.Razorpay)
            {
                await using var online = new SqlCommand("SELECT OnlinePaymentEnabled,UpiEnabled,NetBankingEnabled FROM commerce.TenantPaymentConfigurations WHERE TenantId=@tenant", connection, tx);
                P(online, "@tenant", tenant);
                await using var reader = await online.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token) || !reader.GetBoolean(0)
                    || paymentMethod == "UPI" && !reader.GetBoolean(1)
                    || paymentMethod == "NET_BANKING" && !reader.GetBoolean(2))
                    throw new BusinessRuleException("The selected payment method is not currently available for this store.");
            }
            await using (var count = new SqlCommand("SELECT ISNULL(MAX(AttemptNumber),0)+1 FROM commerce.CommercePayments WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND InvoiceId=@invoice", connection, tx))
            { P(count,"@tenant",tenant);P(count,"@invoice",input.OrderId);attempt=Convert.ToInt32(await count.ExecuteScalarAsync(token),CultureInfo.InvariantCulture); }
            reference = $"KD{paymentId:N}"[..34];
            var status = provider == PaymentProviders.DirectUpi ? CommercePaymentStatuses.PendingVerification : provider == PaymentProviders.Cod ? CommercePaymentStatuses.CodPending : CommercePaymentStatuses.Pending;
            await using var insert = new SqlCommand(@"INSERT commerce.CommercePayments(PaymentId,TenantId,InvoiceId,Provider,PaymentMethod,Amount,Currency,Status,AttemptNumber,TransactionReference,CreatedBy)
VALUES(@id,@tenant,@invoice,@provider,@method,@amount,N'INR',@status,@attempt,@reference,@actor);", connection, tx);
            P(insert,"@id",paymentId);P(insert,"@tenant",tenant);P(insert,"@invoice",input.OrderId);P(insert,"@provider",provider);P(insert,"@method",paymentMethod);P(insert,"@amount",invoice.Balance);P(insert,"@status",status);P(insert,"@attempt",attempt);P(insert,"@reference",reference);P(insert,"@actor",actor);await insert.ExecuteNonQueryAsync(token);
            if (invoice.OrderType == "MARKETPLACE")
            {
                var nextLifecycle = provider == PaymentProviders.Cod ? "CONFIRMED" : "PAYMENT_PENDING";
                if (invoice.MarketplaceLifecycleStatus != nextLifecycle)
                {
                    await using var advance = new SqlCommand("UPDATE commerce.MarketplaceOrders SET LifecycleStatus=@status,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderId=@order AND LifecycleStatus IN('RESERVED','PAYMENT_PENDING');",connection,tx);
                    P(advance,"@status",nextLifecycle);P(advance,"@tenant",tenant);P(advance,"@order",input.OrderId);
                    if(await advance.ExecuteNonQueryAsync(token)!=1)
                        throw new BusinessRuleException("Marketplace order state changed before payment initiation.");
                }
                if (provider == PaymentProviders.Cod)
                {
                    Guid? collectionGroup;
                    await using (var group = new SqlCommand("SELECT TOP(1) FulfilmentGroupId FROM commerce.MarketplaceFulfilmentGroups WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order AND FulfilmentStatus NOT IN('CANCELLED','DELIVERED') ORDER BY CASE WHEN DeliveryAgentId IS NULL THEN 1 ELSE 0 END,GroupNumber;",connection,tx))
                    { P(group,"@tenant",tenant);P(group,"@order",input.OrderId);var selected=await group.ExecuteScalarAsync(token);collectionGroup=selected is Guid id?id:null; }
                    if (collectionGroup is null) throw new BusinessRuleException("A fulfilment group is required for parent COD collection.");
                    await using var codCollection = new SqlCommand("""
                        INSERT commerce.MarketplaceCodCollections(TenantId,OrderId,ExpectedAmount,CollectionStatus,CollectionIdempotencyKey,CollectionFulfilmentGroupId)
                        VALUES(@tenant,@order,@amount,'PENDING',@key,@group);
                        """,connection,tx);
                    P(codCollection,"@tenant",tenant);P(codCollection,"@order",input.OrderId);P(codCollection,"@amount",invoice.GrandTotal);
                    P(codCollection,"@key",$"MARKETPLACE-COD-{paymentId:N}");P(codCollection,"@group",collectionGroup.Value);
                    await codCollection.ExecuteNonQueryAsync(token);
                    await using var completeMarketplace = new SqlCommand("commerce.Marketplace_CompleteOrder",connection,tx){CommandType=CommandType.StoredProcedure};
                    P(completeMarketplace,"@TenantId",tenant);P(completeMarketplace,"@OrderId",input.OrderId);P(completeMarketplace,"@ModifiedBy",actor);
                    await completeMarketplace.ExecuteNonQueryAsync(token);
                }
            }
            if (provider == PaymentProviders.Cod)
            { await using var cod = new SqlCommand("UPDATE integration.WhatsAppCommerceOrders SET PaymentType=N'COD' WHERE TenantId=@tenant AND InvoiceId=@invoice",connection,tx);P(cod,"@tenant",tenant);P(cod,"@invoice",input.OrderId);await cod.ExecuteNonQueryAsync(token); }
            await tx.CommitAsync(token);
        }
        GatewayCreateResult created;
        try
        {
            created = await gateways.Resolve(provider).CreatePaymentAsync(ToGateway(config), new(paymentId,input.OrderId,invoice.Number,invoice.Balance,"INR",reference,invoice.CustomerName,invoice.Mobile,paymentMethod,input.CallbackUrl), token);
        }
        catch
        {
            // A marketplace provider timeout is ambiguous: the provider may have created the
            // intent even though the response was lost. Keep this attempt active so a retry
            // reuses its payment ID instead of risking a second customer charge.
            if (invoice.OrderType != "MARKETPLACE") await UpdateFailed(tenant,paymentId,token);
            throw;
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

    public Task<MarketplaceCodCollectionResult> CollectMarketplaceCodAsync(Guid orderId, MarketplaceCodCollectionInput input, string actor, CancellationToken token)
    {
        var tenant = Tenant;
        var actorUserId = currentUser.UserId ?? throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("A user identity is required.");
        if (idempotency is null) throw new InvalidOperationException("Marketplace COD collection requires the idempotency service.");
        if (input.Amount <= 0 || string.IsNullOrWhiteSpace(input.IdempotencyKey)) throw new BusinessRuleException("A valid idempotency key and full collection amount are required.");
        var method = input.PaymentMethod.Trim().ToUpperInvariant();
        if (method is not ("CASH" or "UPI")) throw new BusinessRuleException("Marketplace COD can be recorded as cash or UPI only.");
        var reference = input.ReceiptReference?.Trim();
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 160) throw new BusinessRuleException("A receipt or collection reference is required.");
        var request = new { orderId, input.FulfilmentGroupId, input.Amount, method, reference, actorUserId };
        return idempotency.ExecuteForTenant(input.IdempotencyKey, "MARKETPLACE_COD_COLLECT", request, actor, tenant,
            (c, tx, ct) => CollectMarketplaceCod(c, tx, tenant, orderId, input.FulfilmentGroupId, input.Amount, method, reference, actorUserId, actor, ct), token);
    }

    public Task<MarketplaceCodCollectionResult> AssignMarketplaceCodCollectionAsync(Guid tenantId, Guid orderId, MarketplaceCodAssignmentInput input, Guid actorUserId, string actor, CancellationToken token)
    {
        if (tenantId != Tenant) throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("The requested tenant does not match the active tenant.");
        if (idempotency is null) throw new InvalidOperationException("Marketplace COD assignment requires the idempotency service.");
        if (string.IsNullOrWhiteSpace(input.Reason) || string.IsNullOrWhiteSpace(input.IdempotencyKey)) throw new BusinessRuleException("A reason and idempotency key are required.");
        var request = new { orderId, input.FulfilmentGroupId, Reason = input.Reason.Trim(), actorUserId };
        return idempotency.ExecuteForTenant(input.IdempotencyKey, "MARKETPLACE_COD_ASSIGN", request, actor, tenantId,
            (c, tx, ct) => AssignMarketplaceCod(c, tx, tenantId, orderId, input.FulfilmentGroupId, input.Reason.Trim(), actorUserId, ct), token);
    }

    private async Task<MarketplaceCodCollectionResult> CollectMarketplaceCod(SqlConnection c, SqlTransaction tx, Guid tenant, Guid orderId, Guid groupId, decimal amount, string method, string reference, Guid actorUserId, string actor, CancellationToken token)
    {
        string lifecycle;
        await using (var q=new SqlCommand("SELECT LifecycleStatus FROM commerce.MarketplaceOrders WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order AND OrderKind='MARKETPLACE';",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);lifecycle=SValue(await q.ExecuteScalarAsync(token))??throw new EntityNotFoundException("Marketplace order was not found.");}
        decimal expected,collected;string state;Guid? assignedGroup,collector;DateTimeOffset? collectedAt;string? receiptReference;
        await using(var q=new SqlCommand("SELECT ExpectedAmount,CollectedAmount,CollectionStatus,CollectionFulfilmentGroupId,CollectorUserId,CollectedAt,ReceiptReference FROM commerce.MarketplaceCodCollections WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new BusinessRuleException("This order has no COD collection obligation.");expected=r.GetDecimal(0);collected=r.GetDecimal(1);state=r.GetString(2);assignedGroup=r.IsDBNull(3)?null:r.GetGuid(3);collector=r.IsDBNull(4)?null:r.GetGuid(4);collectedAt=r.IsDBNull(5)?null:r.GetDateTimeOffset(5);receiptReference=S(r,6);}
        if(state=="COLLECTED")return new(orderId,state,collected,assignedGroup,collector,collectedAt,receiptReference,true);
        if(state!="PENDING"||lifecycle is "CANCELLED" or "EXPIRED")throw new BusinessRuleException("The parent COD collection is no longer pending.");
        if(assignedGroup!=groupId)throw new BusinessRuleException("This fulfilment group is not responsible for the parent COD collection.");
        if(amount!=expected||collected!=0)throw new BusinessRuleException("COD must collect the full parent-order amount exactly once.");
        decimal balance,total;string invoiceStatus;
        await using(var q=new SqlCommand("SELECT BalanceAmount,GrandTotal,Status FROM sales.SalesInvoices WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND InvoiceId=@order AND OrderType='MARKETPLACE';",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Marketplace invoice was not found.");balance=r.GetDecimal(0);total=r.GetDecimal(1);invoiceStatus=r.GetString(2);}
        if(invoiceStatus!="COMPLETED"||balance!=expected||total!=expected)throw new BusinessRuleException("COD amount does not reconcile with the unpaid parent invoice.");
        Guid agentUser;string groupStatus;
        await using(var q=new SqlCommand("SELECT g.FulfilmentStatus,u.Id FROM commerce.MarketplaceFulfilmentGroups g WITH(UPDLOCK,HOLDLOCK) JOIN commerce.DeliveryAgents a ON a.TenantId=g.TenantId AND a.DeliveryAgentId=g.DeliveryAgentId AND a.IsActive=1 JOIN core.Users u ON u.TenantId=a.TenantId AND u.Id=a.UserId WHERE g.TenantId=@tenant AND g.OrderId=@order AND g.FulfilmentGroupId=@group;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);P(q,"@group",groupId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new BusinessRuleException("The fulfilment group has no active assigned delivery agent.");groupStatus=r.GetString(0);agentUser=r.GetGuid(1);}
        if(groupStatus!="OUT_FOR_DELIVERY"||agentUser!=actorUserId)throw new BusinessRuleException("Only the assigned delivery agent can record COD for this delivery.");
        Guid paymentId;
        await using(var q=new SqlCommand("SELECT TOP(1) PaymentId FROM commerce.CommercePayments WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND InvoiceId=@order AND Provider=N'COD' AND Status=N'COD_PENDING' AND Amount=@amount ORDER BY AttemptNumber DESC;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);P(q,"@amount",expected);paymentId=await q.ExecuteScalarAsync(token) is Guid id?id:throw new BusinessRuleException("The parent COD payment is missing or already settled.");}
        var posted=await erpPayments.ApplyAsync(c,tx,new(tenant,orderId,method,expected,reference,actor),token);
        if(posted.InvoiceId!=orderId||posted.BalanceAmount!=0)throw new BusinessRuleException("COD collection did not settle the full parent invoice.");
        await using(var q=new SqlCommand("INSERT commerce.PaymentApplications(PaymentApplicationId,TenantId,PaymentId,InvoiceId,Amount,Currency,AppliedBy)VALUES(NEWID(),@tenant,@payment,@order,@amount,N'INR',@actor);UPDATE commerce.CommercePayments SET Status=N'PAID',PaidAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME(),ProviderReference=@reference,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment AND Status=N'COD_PENDING';IF @@ROWCOUNT<>1 THROW 88100,N'COD payment was concurrently settled.',1;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@payment",paymentId);P(q,"@order",orderId);P(q,"@amount",expected);P(q,"@actor",actor);P(q,"@reference",reference);await q.ExecuteNonQueryAsync(token);}
        await using(var q=new SqlCommand("UPDATE commerce.MarketplaceCodCollections SET CollectionStatus='COLLECTED',CollectedAmount=ExpectedAmount,CollectorUserId=@user,CollectedAt=SYSUTCDATETIME(),ReceiptReference=@reference,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderId=@order AND CollectionStatus='PENDING';IF @@ROWCOUNT<>1 THROW 88101,N'Parent COD collection was concurrently completed.',1;",c,tx))
        {P(q,"@user",actorUserId);P(q,"@reference",reference);P(q,"@tenant",tenant);P(q,"@order",orderId);await q.ExecuteNonQueryAsync(token);}
        await using(var q=new SqlCommand("INSERT commerce.MarketplaceFulfilmentGroupEvents(TenantId,FulfilmentGroupId,EventType,DeliveryAgentId,ActorUserId,Notes) SELECT @tenant,@group,'COD_COLLECTED',DeliveryAgentId,@user,@notes FROM commerce.MarketplaceFulfilmentGroups WHERE TenantId=@tenant AND FulfilmentGroupId=@group;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@group",groupId);P(q,"@user",actorUserId);P(q,"@notes",$"Parent order COD collected: {reference}");await q.ExecuteNonQueryAsync(token);}
        return new(orderId,"COLLECTED",expected,groupId,actorUserId,DateTimeOffset.UtcNow,reference,false);
    }

    private static async Task<MarketplaceCodCollectionResult> AssignMarketplaceCod(SqlConnection c, SqlTransaction tx, Guid tenant, Guid orderId, Guid groupId, string reason, Guid actorUserId, CancellationToken token)
    {
        await using(var q=new SqlCommand("SELECT LifecycleStatus FROM commerce.MarketplaceOrders WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);if(SValue(await q.ExecuteScalarAsync(token)) is null)throw new EntityNotFoundException("Marketplace order was not found.");}
        decimal amount;string state;Guid? oldGroup;
        await using(var q=new SqlCommand("SELECT ExpectedAmount,CollectionStatus,CollectionFulfilmentGroupId FROM commerce.MarketplaceCodCollections WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new BusinessRuleException("Parent COD obligation was not found.");amount=r.GetDecimal(0);state=r.GetString(1);oldGroup=r.IsDBNull(2)?null:r.GetGuid(2);}
        if(state!="PENDING")throw new BusinessRuleException("A completed COD collection cannot be reassigned.");
        if(oldGroup==groupId)return new(orderId,state,amount,oldGroup,null,null,null,true);
        if(oldGroup is null)throw new BusinessRuleException("No previous COD collection assignment exists.");
        string oldStatus;
        await using(var q=new SqlCommand("SELECT FulfilmentStatus FROM commerce.MarketplaceFulfilmentGroups WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderId=@order AND FulfilmentGroupId=@group;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);P(q,"@group",oldGroup.Value);oldStatus=SValue(await q.ExecuteScalarAsync(token))??throw new BusinessRuleException("Previous COD group was not found.");}
        if(oldStatus is not ("DELIVERY_FAILED" or "CANCELLED"))throw new BusinessRuleException("COD collection can be reassigned only after the assigned delivery fails or is cancelled.");
        Guid agentId;string targetStatus;
        await using(var q=new SqlCommand("SELECT g.DeliveryAgentId,g.FulfilmentStatus FROM commerce.MarketplaceFulfilmentGroups g WITH(UPDLOCK,HOLDLOCK) JOIN commerce.DeliveryAgents a ON a.TenantId=g.TenantId AND a.DeliveryAgentId=g.DeliveryAgentId AND a.IsActive=1 WHERE g.TenantId=@tenant AND g.OrderId=@order AND g.FulfilmentGroupId=@group;",c,tx))
        {P(q,"@tenant",tenant);P(q,"@order",orderId);P(q,"@group",groupId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token)||r.IsDBNull(0))throw new BusinessRuleException("Target group must have an active delivery agent.");agentId=r.GetGuid(0);targetStatus=r.GetString(1);}
        if(targetStatus is "CANCELLED" or "DELIVERED")throw new BusinessRuleException("COD cannot be assigned to a cancelled or delivered group.");
        await using(var q=new SqlCommand("UPDATE commerce.MarketplaceCodCollections SET CollectionFulfilmentGroupId=@group,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderId=@order AND CollectionStatus='PENDING';IF @@ROWCOUNT<>1 THROW 88102,N'COD assignment changed concurrently.',1;",c,tx))
        {P(q,"@group",groupId);P(q,"@tenant",tenant);P(q,"@order",orderId);await q.ExecuteNonQueryAsync(token);}
        foreach(var x in new[]{(oldGroup.Value,"COD_COLLECTION_REASSIGNED_OUT",(Guid?)null),(groupId,"COD_COLLECTION_REASSIGNED_IN",(Guid?)agentId)})
        {await using var q=new SqlCommand("INSERT commerce.MarketplaceFulfilmentGroupEvents(TenantId,FulfilmentGroupId,EventType,DeliveryAgentId,ActorUserId,Notes)VALUES(@tenant,@group,@type,@agent,@user,@reason);",c,tx);P(q,"@tenant",tenant);P(q,"@group",x.Item1);P(q,"@type",x.Item2);P(q,"@agent",x.Item3);P(q,"@user",actorUserId);P(q,"@reason",reason);await q.ExecuteNonQueryAsync(token);}
        return new(orderId,"PENDING",amount,groupId,null,null,null,false);
    }

    private async Task<PagedPaymentsDto> ListPayments(Guid? retailerTenant,Guid? selectedTenant,DateTimeOffset? from,DateTimeOffset? to,string? status,string? provider,int pageNumber,int pageSize,CancellationToken token)
    {
        var tenant=retailerTenant??selectedTenant;var page=Math.Max(1,pageNumber);var size=Math.Clamp(pageSize,1,100);
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        var where=" WHERE (@tenant IS NULL OR p.TenantId=@tenant) AND (@from IS NULL OR p.CreatedAt>=@from) AND (@to IS NULL OR p.CreatedAt<DATEADD(day,1,@to)) AND (@status IS NULL OR p.Status=@status) AND (@provider IS NULL OR p.Provider=@provider)";
        var normalizedStatus=string.IsNullOrWhiteSpace(status)?null:status.Trim().ToUpperInvariant();var normalizedProvider=string.IsNullOrWhiteSpace(provider)?null:NormalizeProvider(provider);
        await using var count=new SqlCommand("SELECT COUNT_BIG(*) FROM commerce.CommercePayments p"+where,c);AddFilters(count,tenant,from,to,normalizedStatus,normalizedProvider);var total=Convert.ToInt64(await count.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);
        await using var summary=new SqlCommand("""
            SELECT COALESCE(SUM(x.CollectedAmount),0),COUNT_BIG(*)
            FROM (
                SELECT p.PaymentId,SUM(a.Amount) CollectedAmount
                FROM commerce.CommercePayments p
                JOIN commerce.PaymentApplications a ON a.TenantId=p.TenantId AND a.PaymentId=p.PaymentId AND a.InvoiceId=p.InvoiceId
                WHERE (@tenant IS NULL OR p.TenantId=@tenant)
                  AND (@from IS NULL OR p.CreatedAt>=@from)
                  AND (@to IS NULL OR p.CreatedAt<DATEADD(day,1,@to))
                  AND (@status IS NULL OR p.Status=@status)
                  AND (@provider IS NULL OR p.Provider=@provider)
                  AND p.Status IN(N'PAID',N'CAPTURED')
                GROUP BY p.PaymentId
            ) x;
            """,c);AddFilters(summary,tenant,from,to,normalizedStatus,normalizedProvider);
        await using var summaryReader=await summary.ExecuteReaderAsync(token);
        await summaryReader.ReadAsync(token);
        var totalPaid=summaryReader.GetDecimal(0);var successfulCollections=summaryReader.GetInt64(1);
        await summaryReader.CloseAsync();
        await using var q=new SqlCommand(SelectPayment+where+" ORDER BY p.CreatedAt DESC,p.PaymentId DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",c);AddFilters(q,tenant,from,to,normalizedStatus,normalizedProvider);P(q,"@skip",(page-1)*size);P(q,"@take",size);
        await using var r=await q.ExecuteReaderAsync(token);var rows=new List<CommercePaymentDto>();while(await r.ReadAsync(token))rows.Add(Map(r));return new(rows,total,page,size);
    }

    private static void AddFilters(SqlCommand command,Guid? tenant,DateTimeOffset? from,DateTimeOffset? to,string? status,string? provider)
    {P(command,"@tenant",tenant);P(command,"@from",from);P(command,"@to",to);P(command,"@status",status);P(command,"@provider",provider);}

    public async Task<CommercePaymentDto> VerifyDirectUpiAsync(Guid paymentId, VerifyDirectUpiInput input, Guid userId, string actor, CancellationToken token)
    {
        var tenant=Tenant;var applied=await ApplySuccessfulPayment(tenant,paymentId,PaymentProviders.DirectUpi,null,input.Reference,userId,actor,token);
        if(applied&&loyalty is not null)await loyalty.ProcessOrderAsync(tenant,await OrderId(tenant,paymentId,token),"COMPLETED",actor,token);
        return await GetPaymentAsync(paymentId,token);
    }

    public async Task ProcessRazorpayWebhookAsync(ReadOnlyMemory<byte> rawBody, string signature, string? eventId, CancellationToken token)
    {
        outboundSafety.EnsureExternalDeliveryAllowed("Razorpay webhooks");
        var correlation = WebhookCorrelation(rawBody);
        if (correlation.PaymentId is null && correlation.ProviderReference is null && correlation.ProviderOrderId is null) throw new BusinessRuleException("Razorpay webhook cannot be correlated.");
        PaymentCorrelation payment;
        await using (var c=new SqlConnection(ConnectionString))
        { await c.OpenAsync(token);await using var q=new SqlCommand(@"SELECT TOP(1)p.PaymentId,p.TenantId,p.Amount,p.Currency,p.Status,c.KeyId,c.KeySecretProtected,c.WebhookSecretProtected,c.IsTestMode,p.ProviderOrderId
FROM commerce.CommercePayments p JOIN commerce.TenantPaymentProviders c ON c.TenantId=p.TenantId AND c.Provider=N'RAZORPAY' AND c.IsEnabled=1
WHERE p.Provider=N'RAZORPAY' AND ((@paymentId IS NOT NULL AND p.PaymentId=@paymentId)OR(@reference IS NOT NULL AND p.ProviderReference=@reference)OR(@orderId IS NOT NULL AND p.ProviderOrderId=@orderId));",c);P(q,"@paymentId",correlation.PaymentId);P(q,"@reference",correlation.ProviderReference);P(q,"@orderId",correlation.ProviderOrderId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Razorpay payment correlation was not found.");payment=new(r.GetGuid(0),r.GetGuid(1),r.GetDecimal(2),r.GetString(3),r.GetString(4),r.GetString(5),S(r,6),S(r,7),r.GetBoolean(8),S(r,9)); }
        var config=new PaymentGatewayConfiguration(PaymentProviders.Razorpay,payment.KeyId,Unprotect(payment.KeySecret),Unprotect(payment.WebhookSecret),payment.TestMode,null,null);
        var verified=gateways.Resolve(PaymentProviders.Razorpay).VerifyWebhook(config,rawBody,signature,eventId);
        if(!verified.SignatureValid)throw new System.UnauthorizedAccessException("Razorpay webhook signature is invalid.");
        if(verified.ProviderReference is not null&&!verified.ProviderReference.Equals(correlation.ProviderReference,StringComparison.Ordinal))throw new BusinessRuleException("Razorpay webhook correlation does not match.");
        if(verified.ProviderOrderId is not null&&payment.ProviderOrderId is not null&&!verified.ProviderOrderId.Equals(payment.ProviderOrderId,StringComparison.Ordinal))throw new BusinessRuleException("Razorpay order correlation does not match.");
        var payloadHash=Convert.ToHexString(SHA256.HashData(rawBody.Span));var stableEvent=string.IsNullOrWhiteSpace(verified.EventId)?payloadHash:verified.EventId!;
        var paymentApplied=false;
        await using(var c=await Open(payment.TenantId,token))await using(var tx=(SqlTransaction)await c.BeginTransactionAsync(token))
        {
            await using var add=new SqlCommand(@"IF EXISTS(SELECT 1 FROM commerce.PaymentProviderEvents WITH(UPDLOCK,HOLDLOCK) WHERE Provider=N'RAZORPAY' AND ProviderEventId=@event) SELECT 0;
ELSE BEGIN INSERT commerce.PaymentProviderEvents(PaymentProviderEventId,TenantId,PaymentId,Provider,ProviderEventId,EventType,PayloadHash)VALUES(NEWID(),@tenant,@payment,N'RAZORPAY',@event,@type,@hash);SELECT 1;END",c,tx);
            P(add,"@event",stableEvent);P(add,"@tenant",payment.TenantId);P(add,"@payment",payment.PaymentId);P(add,"@type",verified.EventType);P(add,"@hash",payloadHash);var added=Convert.ToInt32(await add.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);if(added==0){await tx.CommitAsync(token);return;}
            if(verified.Amount is not null&&verified.Amount!=payment.Amount)throw new BusinessRuleException("Razorpay webhook amount does not match the server order.");
            if(verified.Currency is not null&&!verified.Currency.Equals(payment.Currency,StringComparison.OrdinalIgnoreCase))throw new BusinessRuleException("Razorpay webhook currency does not match the server order.");
            if(verified.IsPaid)paymentApplied=await ApplySuccessfulPayment(c,tx,payment.TenantId,payment.PaymentId,PaymentProviders.Razorpay,verified.ProviderPaymentId,verified.ProviderReference,null,"RAZORPAY_WEBHOOK",token,verified.ProviderOrderId);
            else if(verified.IsFailed){await using var fail=new SqlCommand("UPDATE commerce.CommercePayments SET Status=CASE WHEN Status=N'PAID' THEN Status ELSE N'FAILED' END,FailedAt=CASE WHEN Status=N'PAID' THEN FailedAt ELSE SYSUTCDATETIME() END,ProviderPaymentId=COALESCE(@providerPayment,ProviderPaymentId),ProviderOrderId=COALESCE(@providerOrderId,ProviderOrderId),ProviderReference=COALESCE(@reference,ProviderReference),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment",c,tx);P(fail,"@providerPayment",verified.ProviderPaymentId);P(fail,"@providerOrderId",verified.ProviderOrderId);P(fail,"@reference",verified.ProviderReference);P(fail,"@tenant",payment.TenantId);P(fail,"@payment",payment.PaymentId);await fail.ExecuteNonQueryAsync(token);}
            else {await using var ids=new SqlCommand("UPDATE commerce.CommercePayments SET ProviderOrderId=COALESCE(@orderId,ProviderOrderId),ProviderPaymentId=COALESCE(@paymentId,ProviderPaymentId),ProviderReference=COALESCE(@reference,ProviderReference),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment",c,tx);P(ids,"@orderId",verified.ProviderOrderId);P(ids,"@paymentId",verified.ProviderPaymentId);P(ids,"@reference",verified.ProviderReference);P(ids,"@tenant",payment.TenantId);P(ids,"@payment",payment.PaymentId);await ids.ExecuteNonQueryAsync(token);}
            await using var done=new SqlCommand("UPDATE commerce.PaymentProviderEvents SET ProcessedAt=SYSUTCDATETIME() WHERE Provider=N'RAZORPAY' AND ProviderEventId=@event",c,tx);P(done,"@event",stableEvent);await done.ExecuteNonQueryAsync(token);await tx.CommitAsync(token);
        }
        if(paymentApplied&&loyalty is not null)await loyalty.ProcessOrderAsync(payment.TenantId,(await OrderId(payment.TenantId,payment.PaymentId,token)),"COMPLETED","RAZORPAY_WEBHOOK",token);
    }

    private async Task<bool> ApplySuccessfulPayment(Guid tenant,Guid paymentId,string provider,string? providerPayment,string? reference,Guid userId,string actor,CancellationToken token)
    { await using var c=await Open(tenant,token);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(token);var applied=await ApplySuccessfulPayment(c,tx,tenant,paymentId,provider,providerPayment,reference,userId,actor,token);await tx.CommitAsync(token);return applied; }

    private async Task<bool> ApplySuccessfulPayment(SqlConnection c,SqlTransaction tx,Guid tenant,Guid paymentId,string provider,string? providerPayment,string? reference,Guid? userId,string actor,CancellationToken token,string? providerOrderId=null)
    {
        Guid invoice; decimal amount,balance,grandTotal; string currency,status,invoiceStatus;
        string? orderType;
        await using (var kind=new SqlCommand("SELECT OrderType FROM sales.SalesInvoices WHERE TenantId=@tenant AND InvoiceId=(SELECT InvoiceId FROM commerce.CommercePayments WHERE TenantId=@tenant AND PaymentId=@payment);",c,tx))
        {P(kind,"@tenant",tenant);P(kind,"@payment",paymentId);orderType=SValue(await kind.ExecuteScalarAsync(token));}
        string? lifecycle=null;DateTimeOffset? reservationExpiry=null;
        if(orderType=="MARKETPLACE")
        {
            await using var parent=new SqlCommand("SELECT o.LifecycleStatus,o.ReservationExpiresAt FROM commerce.MarketplaceOrders o WITH(UPDLOCK,HOLDLOCK) JOIN sales.SalesInvoices i WITH(UPDLOCK,HOLDLOCK) ON i.TenantId=o.TenantId AND i.InvoiceId=o.OrderId WHERE o.TenantId=@tenant AND o.OrderId=(SELECT InvoiceId FROM commerce.CommercePayments WHERE TenantId=@tenant AND PaymentId=@payment) AND o.OrderKind='MARKETPLACE' AND i.OrderType='MARKETPLACE';",c,tx);
            P(parent,"@tenant",tenant);P(parent,"@payment",paymentId);await using var pr=await parent.ExecuteReaderAsync(token);
            if(!await pr.ReadAsync(token))throw new EntityNotFoundException("Marketplace order was not found for this tenant.");
            lifecycle=pr.GetString(0);reservationExpiry=pr.IsDBNull(1)?null:pr.GetDateTimeOffset(1);
        }
        await using(var q=new SqlCommand(@"SELECT p.InvoiceId,p.Amount,p.Currency,p.Status,i.BalanceAmount,i.Status,i.GrandTotal FROM commerce.CommercePayments p WITH(UPDLOCK,HOLDLOCK) JOIN sales.SalesInvoices i WITH(UPDLOCK,HOLDLOCK) ON i.InvoiceId=p.InvoiceId AND i.TenantId=p.TenantId WHERE p.TenantId=@tenant AND p.PaymentId=@payment AND p.Provider=@provider",c,tx))
        {P(q,"@tenant",tenant);P(q,"@payment",paymentId);P(q,"@provider",provider);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Payment was not found for this tenant.");invoice=r.GetGuid(0);amount=r.GetDecimal(1);currency=r.GetString(2);status=r.GetString(3);balance=r.GetDecimal(4);invoiceStatus=r.GetString(5);grandTotal=r.GetDecimal(6);}
        // A replay must not re-run post-payment hooks such as loyalty processing.
        if(status==CommercePaymentStatuses.Paid)return false;
        if(provider==PaymentProviders.DirectUpi&&status!=CommercePaymentStatuses.PendingVerification)throw new BusinessRuleException("Only a Direct UPI payment pending verification can be verified.");
        if(currency!="INR")throw new BusinessRuleException("Payment currency does not match the parent invoice.");
        if(orderType=="MARKETPLACE")
        {
            var invalidReservation = balance!=amount || grandTotal!=amount || invoiceStatus is not ("HELD" or "SUSPENDED")
                || lifecycle is "CANCELLED" or "EXPIRED" or "POSTED" or "PARTIALLY_FULFILLED" or "FULFILLED"
                || reservationExpiry is null || reservationExpiry<=DateTimeOffset.UtcNow;
            if(!invalidReservation)
            {
                await using var reservations=new SqlCommand("""
                    SELECT CASE WHEN EXISTS(SELECT 1 FROM commerce.MarketplaceOrderAllocations a WITH(UPDLOCK,HOLDLOCK)
                        LEFT JOIN commerce.MarketplaceAllocationReservations r WITH(UPDLOCK,HOLDLOCK)
                          ON r.TenantId=a.TenantId AND r.OrderId=a.OrderId AND r.AllocationId=a.AllocationId
                        WHERE a.TenantId=@tenant AND a.OrderId=@invoice
                          AND (r.ReservationId IS NULL OR r.ReservationStatus NOT IN('RESERVED','CONFIRMED') OR r.ExpiresAt<=SYSUTCDATETIME()))
                      OR NOT EXISTS(SELECT 1 FROM commerce.MarketplaceOrderAllocations WHERE TenantId=@tenant AND OrderId=@invoice)
                      THEN 1 ELSE 0 END;
                    """,c,tx);P(reservations,"@tenant",tenant);P(reservations,"@invoice",invoice);
                invalidReservation=Convert.ToInt32(await reservations.ExecuteScalarAsync(token),CultureInfo.InvariantCulture)!=0;
            }
            if(invalidReservation)
            {
                var reason=balance!=amount||grandTotal!=amount?"PAYABLE_CHANGED":
                    lifecycle is "CANCELLED" or "EXPIRED" or "POSTED" or "PARTIALLY_FULFILLED" or "FULFILLED" || invoiceStatus is not ("HELD" or "SUSPENDED")?"ORDER_TERMINAL":"RESERVATION_EXPIRED";
                await using var reconciliation=new SqlCommand("""
                    IF NOT EXISTS(SELECT 1 FROM commerce.MarketplacePaymentReconciliations WITH(UPDLOCK,HOLDLOCK)
                       WHERE TenantId=@tenant AND PaymentId=@payment)
                    INSERT commerce.MarketplacePaymentReconciliations(TenantId,OrderId,PaymentId,ReasonCode,ExpectedAmount,CapturedAmount,Currency,ProviderPaymentId,ProviderReference,CreatedBy)
                    VALUES(@tenant,@invoice,@payment,@reason,@expected,@captured,@currency,@providerPayment,@reference,@actor);
                    UPDATE commerce.CommercePayments SET Status=N'PAID',ProviderPaymentId=COALESCE(@providerPayment,ProviderPaymentId),
                      ProviderOrderId=COALESCE(@providerOrderId,ProviderOrderId),ProviderReference=COALESCE(@reference,ProviderReference),
                      PaidAt=COALESCE(PaidAt,SYSUTCDATETIME()),VerifiedAt=CASE WHEN @user IS NULL THEN VerifiedAt ELSE SYSUTCDATETIME() END,
                      VerifiedBy=COALESCE(@user,VerifiedBy),AppliedAt=NULL,UpdatedAt=SYSUTCDATETIME()
                    WHERE TenantId=@tenant AND PaymentId=@payment AND Status<>N'PAID';
                    """,c,tx);
                P(reconciliation,"@tenant",tenant);P(reconciliation,"@invoice",invoice);P(reconciliation,"@payment",paymentId);
                P(reconciliation,"@reason",reason);P(reconciliation,"@expected",balance);P(reconciliation,"@captured",amount);
                P(reconciliation,"@currency",currency);P(reconciliation,"@providerPayment",providerPayment);P(reconciliation,"@reference",reference);P(reconciliation,"@actor",actor);
                P(reconciliation,"@providerOrderId",providerOrderId);P(reconciliation,"@user",userId);
                await reconciliation.ExecuteNonQueryAsync(token);
                return false;
            }
        }
        var method=provider==PaymentProviders.Razorpay?"RAZORPAY":"UPI";
        var posted=await erpPayments.ApplyAsync(c,tx,new(tenant,invoice,method,amount,reference??providerPayment,actor),token);
        if(posted.InvoiceId!=invoice||posted.BalanceAmount!=0)throw new BusinessRuleException("ERP payment did not settle the expected invoice balance.");
        if(orderType!="MARKETPLACE" && (invoiceStatus is "HELD" or "SUSPENDED"))
        {
            await using var complete=new SqlCommand("sales.POS_TransitionHeldInvoice",c,tx){CommandType=CommandType.StoredProcedure};
            P(complete,"@InvoiceId",invoice);P(complete,"@Action","COMPLETE");P(complete,"@ModifiedBy",actor);await complete.ExecuteNonQueryAsync(token);
        }
        await using(var app=new SqlCommand("INSERT commerce.PaymentApplications(PaymentApplicationId,TenantId,PaymentId,InvoiceId,Amount,Currency,AppliedBy)VALUES(NEWID(),@tenant,@payment,@invoice,@amount,@currency,@actor)",c,tx)){P(app,"@tenant",tenant);P(app,"@payment",paymentId);P(app,"@invoice",invoice);P(app,"@amount",amount);P(app,"@currency",currency);P(app,"@actor",actor);await app.ExecuteNonQueryAsync(token);}
        await using(var update=new SqlCommand("UPDATE commerce.CommercePayments SET Status=N'PAID',ProviderPaymentId=COALESCE(@providerPayment,ProviderPaymentId),ProviderOrderId=COALESCE(@providerOrderId,ProviderOrderId),ProviderReference=COALESCE(@reference,ProviderReference),PaidAt=SYSUTCDATETIME(),VerifiedAt=CASE WHEN @user IS NULL THEN VerifiedAt ELSE SYSUTCDATETIME() END,VerifiedBy=COALESCE(@user,VerifiedBy),AppliedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment",c,tx)){P(update,"@providerPayment",providerPayment);P(update,"@providerOrderId",providerOrderId);P(update,"@reference",reference);P(update,"@user",userId);P(update,"@tenant",tenant);P(update,"@payment",paymentId);await update.ExecuteNonQueryAsync(token);}
        if(orderType=="MARKETPLACE")
        {
            if(lifecycle!="CONFIRMED")
            {
                await using var confirm=new SqlCommand("UPDATE commerce.MarketplaceOrders SET LifecycleStatus='CONFIRMED',UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderId=@invoice AND LifecycleStatus IN('RESERVED','PAYMENT_PENDING');",c,tx);
                P(confirm,"@tenant",tenant);P(confirm,"@invoice",invoice);
                if(await confirm.ExecuteNonQueryAsync(token)!=1)throw new BusinessRuleException("Marketplace order expired before payment confirmation.");
            }
            await using var completeMarketplace=new SqlCommand("commerce.Marketplace_CompleteOrder",c,tx){CommandType=CommandType.StoredProcedure};
            P(completeMarketplace,"@TenantId",tenant);P(completeMarketplace,"@OrderId",invoice);P(completeMarketplace,"@ModifiedBy",actor);
            await completeMarketplace.ExecuteNonQueryAsync(token);
        }
        return true;
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

    private async Task<PaymentSettingsDto> GetSettings(Guid tenant, CancellationToken token)
    {
        await using var c = await Open(tenant, token);
        bool? online = null, upi = null, netBanking = null;
        await using (var state = new SqlCommand("SELECT OnlinePaymentEnabled,UpiEnabled,NetBankingEnabled FROM commerce.TenantPaymentConfigurations WHERE TenantId=@tenant", c))
        {
            P(state, "@tenant", tenant);
            await using var reader = await state.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) { online = reader.GetBoolean(0); upi = reader.GetBoolean(1); netBanking = reader.GetBoolean(2); }
        }
        var found = new Dictionary<string, PaymentProviderSetting>(StringComparer.Ordinal);
        await using (var q = new SqlCommand("SELECT Provider,IsEnabled,IsDefault,KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode,UpiVpa,PayeeName FROM commerce.TenantPaymentProviders WHERE TenantId=@tenant", c))
        {
            P(q, "@tenant", tenant);
            await using var reader = await q.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var provider = reader.GetString(0); var key = S(reader, 3); var hasKey = !reader.IsDBNull(4); var hasWebhook = !reader.IsDBNull(5);
                var vpa = S(reader, 7); var payee = S(reader, 8);
                var configured = provider switch
                {
                    PaymentProviders.Razorpay => key is not null && hasKey && hasWebhook,
                    PaymentProviders.DirectUpi => PaymentValidation.IsValidVpa(vpa) && !string.IsNullOrWhiteSpace(payee),
                    _ => true
                };
                found[provider] = new(provider, reader.GetBoolean(1), reader.GetBoolean(2), configured,
                    key is null ? null : PaymentValidation.MaskKeyId(key), hasKey, hasWebhook, reader.GetBoolean(6), vpa, payee);
            }
        }
        var providers = new[] { PaymentProviders.Razorpay, PaymentProviders.DirectUpi, PaymentProviders.Cod }
            .Select(provider => found.TryGetValue(provider, out var setting) ? setting
                : new PaymentProviderSetting(provider, false, false, provider == PaymentProviders.Cod, null, false, false, true, null, null)).ToArray();
        return new(online ?? providers.Any(x => x.Provider != PaymentProviders.Cod && x.IsEnabled), providers, upi ?? true, netBanking ?? true);
    }
    private async Task<SqlConnection> Open(Guid tenant,CancellationToken token){var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant",c);P(q,"@tenant",tenant);await q.ExecuteNonQueryAsync(token);return c;}
    private string? ReplaceSecret(string? replacement,string? existing)=>string.IsNullOrWhiteSpace(replacement)?existing:Protector.Protect(replacement.Trim());
    private string? Unprotect(string? value){if(value is null)return null;try{return Protector.Unprotect(value);}catch(CryptographicException){throw new BusinessRuleException("Stored Razorpay credentials cannot be decrypted. Replace the retailer configuration.");}}
    private static string NormalizeProvider(string value){var p=value?.Trim().ToUpperInvariant()??string.Empty;if(!PaymentProviders.All.Contains(p))throw new BusinessRuleException("Select a supported payment provider.");return p;}

    private static string NormalizeMethod(string provider,string? method){var value=method?.Trim().ToUpperInvariant();if(provider==PaymentProviders.Cod)return "COD";if(provider==PaymentProviders.DirectUpi)return "UPI";if(value is "UPI" or "NET_BANKING" or "RAZORPAY")return value;throw new BusinessRuleException("Select a supported Razorpay payment method.");}
    private static bool IsConfigured(ConfigRow x)=>x.Provider switch{PaymentProviders.Razorpay=>x.KeyId is not null&&x.KeySecret is not null&&x.WebhookSecret is not null,PaymentProviders.DirectUpi=>PaymentValidation.IsValidVpa(x.Vpa)&&!string.IsNullOrWhiteSpace(x.Payee),_=>true};
    private PaymentGatewayConfiguration ToGateway(ConfigRow x)=>new(x.Provider,x.KeyId,Unprotect(x.KeySecret),Unprotect(x.WebhookSecret),x.TestMode,x.Vpa,x.Payee);
    private static async Task<ConfigRow?> ReadConfig(SqlConnection c,SqlTransaction? tx,Guid tenant,string provider,CancellationToken token){await using var q=new SqlCommand("SELECT Provider,IsEnabled,KeyId,KeySecretProtected,WebhookSecretProtected,IsTestMode,UpiVpa,PayeeName FROM commerce.TenantPaymentProviders WHERE TenantId=@tenant AND Provider=@provider",c,tx);P(q,"@tenant",tenant);P(q,"@provider",provider);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetString(0),r.GetBoolean(1),S(r,2),S(r,3),S(r,4),r.GetBoolean(5),S(r,6),S(r,7)):null;}
    private static async Task<InvoiceRow> ReadInvoice(SqlConnection c,SqlTransaction tx,Guid tenant,Guid invoice,bool locked,CancellationToken token)
    {
        string? orderType;
        await using (var kind = new SqlCommand("SELECT OrderType FROM sales.SalesInvoices WHERE TenantId=@tenant AND InvoiceId=@invoice;",c,tx))
        { P(kind,"@tenant",tenant);P(kind,"@invoice",invoice);orderType=SValue(await kind.ExecuteScalarAsync(token)); }
        if (orderType is null) throw new EntityNotFoundException("Active order was not found for this tenant.");
        var lockHint=locked?" WITH(UPDLOCK,HOLDLOCK)":"";
        if (orderType=="MARKETPLACE")
        {
            await using var marketplace = new SqlCommand($"""
                SELECT i.InvoiceNumber,i.BalanceAmount,c.CustomerName,c.Mobile,i.GrandTotal,i.PaidAmount,
                       o.LifecycleStatus,o.ReservationExpiresAt
                FROM commerce.MarketplaceOrders o{lockHint}
                JOIN sales.SalesInvoices i{lockHint} ON i.TenantId=o.TenantId AND i.InvoiceId=o.OrderId
                LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId
                WHERE o.TenantId=@tenant AND o.OrderId=@invoice AND o.OrderKind='MARKETPLACE'
                  AND i.OrderType='MARKETPLACE' AND i.Status NOT IN(N'CANCELLED',N'VOID',N'RETURNED');
                """,c,tx);
            P(marketplace,"@tenant",tenant);P(marketplace,"@invoice",invoice);
            await using var reader=await marketplace.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new(reader.GetString(0),reader.GetDecimal(1),S(reader,2),S(reader,3),"MARKETPLACE",
                    reader.GetString(6),reader.IsDBNull(7)?null:reader.GetDateTimeOffset(7),reader.GetDecimal(4),reader.GetDecimal(5))
                : throw new EntityNotFoundException("Marketplace order was not found for this tenant.");
        }
        await using var standard = new SqlCommand($"SELECT i.InvoiceNumber,i.BalanceAmount,c.CustomerName,c.Mobile,i.GrandTotal,i.PaidAmount FROM sales.SalesInvoices i{lockHint} LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId WHERE i.TenantId=@tenant AND i.InvoiceId=@invoice AND i.Status NOT IN(N'CANCELLED',N'VOID',N'RETURNED') AND i.OrderType='STANDARD';",c,tx);
        P(standard,"@tenant",tenant);P(standard,"@invoice",invoice);
        await using var standardReader=await standard.ExecuteReaderAsync(token);
        return await standardReader.ReadAsync(token)
            ? new(standardReader.GetString(0),standardReader.GetDecimal(1),S(standardReader,2),S(standardReader,3),"STANDARD",null,null,standardReader.GetDecimal(4),standardReader.GetDecimal(5))
            : throw new EntityNotFoundException("Active order was not found for this tenant.");
    }
    private static string? SValue(object? value)=>value is null or DBNull?null:Convert.ToString(value,CultureInfo.InvariantCulture);
    private async Task UpdateFailed(Guid tenant,Guid payment,CancellationToken token){await using var c=await Open(tenant,token);await using var q=new SqlCommand("UPDATE commerce.CommercePayments SET Status=N'FAILED',FailedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND PaymentId=@payment AND Status=N'PENDING'",c);P(q,"@tenant",tenant);P(q,"@payment",payment);await q.ExecuteNonQueryAsync(token);}
    private async Task<Guid> OrderId(Guid tenant,Guid payment,CancellationToken token){await using var c=await Open(tenant,token);await using var q=new SqlCommand("SELECT InvoiceId FROM commerce.CommercePayments WHERE TenantId=@tenant AND PaymentId=@payment",c);P(q,"@tenant",tenant);P(q,"@payment",payment);return (Guid)(await q.ExecuteScalarAsync(token)??throw new EntityNotFoundException("Payment was not found."));}
    private static (Guid? PaymentId,string? ProviderReference,string? ProviderOrderId) WebhookCorrelation(ReadOnlyMemory<byte> raw){try{using var j=JsonDocument.Parse(raw);var p=j.RootElement.GetProperty("payload");string? link=null,order=null,internalId=null;if(p.TryGetProperty("payment_link",out var lw)&&lw.TryGetProperty("entity",out var le)){if(le.TryGetProperty("id",out var li))link=li.GetString();if(le.TryGetProperty("notes",out var ln)&&ln.TryGetProperty("payment_id",out var lp))internalId=lp.GetString();}if(p.TryGetProperty("payment",out var pw)&&pw.TryGetProperty("entity",out var pe)){if(pe.TryGetProperty("order_id",out var oi))order=oi.GetString();if(internalId is null&&pe.TryGetProperty("notes",out var pn)&&pn.TryGetProperty("payment_id",out var pp))internalId=pp.GetString();}return(Guid.TryParseExact(internalId,"N",out var id)?id:null,link,order);}catch(JsonException){throw new BusinessRuleException("Razorpay webhook payload is invalid.");}}
    private const string SelectPayment=@"SELECT p.PaymentId,p.TenantId,t.Name,p.InvoiceId,i.InvoiceNumber,c.CustomerName,p.Provider,p.PaymentMethod,p.Amount,p.Currency,p.Status,p.ProviderOrderId,p.ProviderPaymentId,p.PaymentLink,p.TransactionReference,p.CreatedAt,p.PaidAt,p.VerifiedAt,u.UserName FROM commerce.CommercePayments p JOIN core.Tenants t ON t.TenantId=p.TenantId JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=p.TenantId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=p.TenantId LEFT JOIN core.Users u ON u.Id=p.VerifiedBy AND u.TenantId=p.TenantId";
    private static CommercePaymentDto Map(SqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetGuid(3),r.GetString(4),S(r,5),r.GetString(6),r.GetString(7),r.GetDecimal(8),r.GetString(9),r.GetString(10),S(r,11),S(r,12),S(r,13),S(r,14),r.GetDateTimeOffset(15),r.IsDBNull(16)?null:r.GetDateTimeOffset(16),r.IsDBNull(17)?null:r.GetDateTimeOffset(17),S(r,18));
    private static void P(SqlCommand q,string name,object? value)=>q.Parameters.AddWithValue(name,value??DBNull.Value);
    private static string? S(SqlDataReader r,int index)=>r.IsDBNull(index)?null:r.GetString(index);
    private sealed record ConfigRow(string Provider,bool Enabled,string? KeyId,string? KeySecret,string? WebhookSecret,bool TestMode,string? Vpa,string? Payee);
    private sealed record InvoiceRow(string Number,decimal Balance,string? CustomerName,string? Mobile,
        string OrderType,string? MarketplaceLifecycleStatus,DateTimeOffset? ReservationExpiresAt,decimal GrandTotal,decimal PaidAmount);
    private sealed record PaymentCorrelation(Guid PaymentId,Guid TenantId,decimal Amount,string Currency,string Status,string KeyId,string? KeySecret,string? WebhookSecret,bool TestMode,string? ProviderOrderId);
}
