#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Application.Features.Loyalty;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSEngine(
    SqlIdempotencyExecutor idempotency,
    IHttpContextAccessor httpContext,
    ICurrentUserService currentUser,
    ILoyaltyService loyalty) : IPOSEngine
{
    public async Task<POSPostResult> Post(POSPostRequest r, CancellationToken token)
        => await PostInternal(r, currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required."), IdempotencyKeyReader.Read(httpContext), token);

    public async Task<POSPostResult> PostForTenant(POSPostRequest r, Guid tenantId, string idempotencyKey, CancellationToken token)
    {
        try
        {
            return await idempotency.ExecuteForTenant(idempotencyKey, "POS_SALE", r, r.User, tenantId,
                async (connection, transaction, ct) =>
                {
                    if (r.AppliedPromotionId is Guid promotionId)
                    {
                        if (r.CustomerId is not Guid promotionCustomerId)
                            throw new BusinessRuleException("An authenticated customer is required for this offer.");
                        await using var customerLock = new SqlCommand("SELECT CustomerId FROM sales.Customers WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0", connection, transaction);
                        customerLock.Parameters.AddWithValue("@tenant", tenantId);
                        customerLock.Parameters.AddWithValue("@customer", promotionCustomerId);
                        if (await customerLock.ExecuteScalarAsync(ct) is not Guid)
                            throw new BusinessRuleException("Customer is no longer available.");
                        await using var eligibility = new SqlCommand("""
                            SELECT p.OfferType,p.UsageLimitPerCustomer,
                              (SELECT COUNT(1) FROM commerce.StorefrontPromotionUses u
                               JOIN sales.SalesInvoices i ON i.InvoiceId=u.InvoiceId AND i.TenantId=u.TenantId
                               OUTER APPLY(SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=i.TenantId AND cp.InvoiceId=i.InvoiceId ORDER BY cp.AttemptNumber DESC) payment
                               WHERE u.TenantId=@tenant AND u.PromotionId=p.PromotionId AND u.CustomerId=@customer
                                 AND i.Status NOT IN(N'CANCELLED',N'VOID') AND (i.Status=N'COMPLETED' OR payment.Status IS NULL OR payment.Status<>N'FAILED')),
                              (SELECT COUNT(1) FROM sales.SalesInvoices i
                               JOIN integration.WhatsAppCommerceOrders w ON w.InvoiceId=i.InvoiceId AND w.TenantId=i.TenantId AND w.SourceChannel=N'STOREFRONT'
                               WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND i.Status=N'COMPLETED')
                            FROM commerce.StorefrontPromotions p WITH(UPDLOCK,HOLDLOCK)
                            WHERE p.TenantId=@tenant AND p.PromotionId=@promotion AND p.IsDeleted=0 AND p.IsActive=1
                              AND (p.StartsAt IS NULL OR p.StartsAt<=SYSUTCDATETIME())
                              AND (p.EndsAt IS NULL OR p.EndsAt>SYSUTCDATETIME());
                            """, connection, transaction);
                        eligibility.Parameters.AddWithValue("@tenant", tenantId);
                        eligibility.Parameters.AddWithValue("@customer", promotionCustomerId);
                        eligibility.Parameters.AddWithValue("@promotion", promotionId);
                        await using var eligibilityReader = await eligibility.ExecuteReaderAsync(ct);
                        if (!await eligibilityReader.ReadAsync(ct))
                            throw new BusinessRuleException("The offer is no longer available. Refresh your cart.");
                        var offerType = eligibilityReader.GetString(0);
                        var usageLimit = eligibilityReader.IsDBNull(1) ? (int?)null : eligibilityReader.GetInt32(1);
                        var previousUses = eligibilityReader.GetInt32(2);
                        var completedOrders = eligibilityReader.GetInt32(3);
                        if (usageLimit is not null && previousUses >= usageLimit.Value
                            || offerType == "FIRST_ORDER" && (previousUses > 0 || completedOrders > 0))
                            throw new BusinessRuleException("The offer is no longer available. Refresh your cart.");
                        await eligibilityReader.CloseAsync();
                    }                    await using var command = Command(connection, transaction, "sales.POS_PostInvoice", [
                        ("@TenantId", tenantId), ("@CounterId", r.CounterId), ("@ShiftId", r.ShiftId), ("@CustomerId", r.CustomerId), ("@WarehouseId", r.WarehouseId),
                        ("@SalesPersonId", r.SalesPersonId), ("@ItemsJson", r.ItemsJson), ("@PaymentsJson", r.PaymentsJson), ("@BillDiscount", r.BillDiscount), ("@RoundOff", r.RoundOff),
                        ("@Remarks", r.Remarks), ("@Status", r.Status), ("@InterState", r.InterState), ("@DiscountAuthorizedBy", r.DiscountAuthorizedBy), ("@CreatedBy", r.User), ("@DeliveryCharge", r.DeliveryCharge), ("@PromotionDiscountAmount", r.PromotionDiscountAmount), ("@AppliedPromotionId", r.AppliedPromotionId), ("@AppliedPromotionName", r.AppliedPromotionName), ("@FreeDeliveryApplied", r.FreeDeliveryApplied), ("@FreeDeliveryThresholdSnapshot", r.FreeDeliveryThresholdSnapshot), ("@ServicePincode", r.ServicePincode)]);
                    POSPostResult result;
                    await using (var reader = await command.ExecuteReaderAsync(ct))
                    {
                        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Invoice post returned no result.");
                        result = new(reader.GetGuid(reader.GetOrdinal("InvoiceId")), reader.GetString(reader.GetOrdinal("InvoiceNumber")), reader.GetDecimal(reader.GetOrdinal("GrandTotal")), reader.GetDecimal(reader.GetOrdinal("PaidAmount")), reader.GetString(reader.GetOrdinal("Status")));
                    }
                    if (r.TenantId.HasValue && !string.IsNullOrWhiteSpace(r.SourceChannel))
                    {
                        await using var source = new SqlCommand("INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,SourceChannel,ProviderMode,LastNotifiedErpStatus,LastNotifiedOn,CreatedBy) VALUES(NEWID(),@tenant,@invoice,@channel,N'LIVE',@status,SYSUTCDATETIME(),@user);", connection, transaction);
                        source.Parameters.AddWithValue("@tenant", tenantId); source.Parameters.AddWithValue("@invoice", result.InvoiceId); source.Parameters.AddWithValue("@channel", r.SourceChannel); source.Parameters.AddWithValue("@status", result.Status); source.Parameters.AddWithValue("@user", r.User ?? (object)DBNull.Value); await source.ExecuteNonQueryAsync(ct);
                    }
                    if (r.AppliedPromotionId is Guid usedPromotionId && r.CustomerId is Guid usedCustomerId)
                    {
                        await using var redemption = new SqlCommand("INSERT commerce.StorefrontPromotionUses(TenantId,PromotionId,CustomerId,InvoiceId,CreatedAt) VALUES(@tenant,@promotion,@customer,@invoice,SYSUTCDATETIME())", connection, transaction);
                        redemption.Parameters.AddWithValue("@tenant", tenantId);
                        redemption.Parameters.AddWithValue("@promotion", usedPromotionId);
                        redemption.Parameters.AddWithValue("@customer", usedCustomerId);
                        redemption.Parameters.AddWithValue("@invoice", result.InvoiceId);
                        await redemption.ExecuteNonQueryAsync(ct);
                    }                    return result;
                }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51100) { throw new BusinessRuleException(ex.Message); }
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
