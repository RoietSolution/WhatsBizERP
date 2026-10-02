using System.Net.Mail;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed partial class StorefrontCheckoutService(
    IConfiguration configuration,
    IPOSEngine pos,
    ICommercePaymentService payments,
    IStorefrontCustomerService customers,
    ILogger<StorefrontCheckoutService> logger) : IStorefrontCheckoutService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontCartQuoteDto?> QuoteAsync(string storeKey, StorefrontCartQuoteInput input,
        string? customerSessionToken, CancellationToken token)
    {
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        var items = NormalizeItems(input.Items);
        var (_, lines) = await PriceCart(tenantId.Value, JsonSerializer.Serialize(items), items.Length, token);
        var authenticated = string.IsNullOrWhiteSpace(customerSessionToken) ? null
            : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        return await CalculateQuote(tenantId.Value, lines, input.Pincode, authenticated?.Id, input.PromoCode, token);
    }

    private static StorefrontCheckoutItem[] NormalizeItems(IReadOnlyCollection<StorefrontCheckoutItem>? source)
    {
        var items = (source ?? []).GroupBy(x => x.ProductId)
            .Select(x => new StorefrontCheckoutItem(x.Key, x.Sum(y => y.Quantity))).ToArray();
        if (items.Length is 0 or > 100 || items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0 || x.Quantity > 9999))
            throw new BusinessRuleException("The cart contains invalid items or quantities.");
        return items;
    }

    private async Task<StorefrontCartQuoteDto> CalculateQuote(Guid tenantId, IReadOnlyCollection<PricedLine> lines,
        string? pincode, Guid? verifiedCustomerId, string? promotionCode, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(pincode) && (pincode.Length != 6 || !pincode.All(char.IsAsciiDigit)))
            throw new BusinessRuleException("Enter a valid six-digit Indian pincode.");
        await using var connection = await Open(tenantId, token);
        StorefrontPricingConfiguration settings;
        await using (var command = new SqlCommand("SELECT CONVERT(bit,CASE WHEN s.DeliveryEnabled=1 AND d.DeliveryEnabled=1 THEN 1 ELSE 0 END),s.StandardDeliveryCharge,s.FreeDeliveryEnabled,s.FreeDeliveryThreshold FROM commerce.StorefrontConfigurations s LEFT JOIN commerce.TenantDeliverySettings d ON d.TenantId=s.TenantId WHERE s.TenantId=@tenant", connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId);
            await using var reader = await command.ExecuteReaderAsync(token);
            settings = await reader.ReadAsync(token)
                ? new(reader.GetBoolean(0), reader.GetDecimal(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3))
                : new(false, 0, false, null);
        }
        var serviceable = false;
        if (pincode?.Length == 6 && pincode.All(char.IsAsciiDigit))
        {
            await using var command = new SqlCommand("SELECT COUNT(1) FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1", connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@pincode", pincode);
            serviceable = (int)(await command.ExecuteScalarAsync(token) ?? 0) == 1;
        }
        bool hasOrder = false;
        if (verifiedCustomerId is Guid customerId)
        {
            await using var command = new SqlCommand("""
                SELECT CASE WHEN EXISTS(
                  SELECT 1 FROM sales.SalesInvoices i
                  JOIN integration.WhatsAppCommerceOrders w ON w.InvoiceId=i.InvoiceId AND w.TenantId=i.TenantId AND w.SourceChannel=N'STOREFRONT'
                  WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND i.Status=N'COMPLETED'
                ) OR EXISTS(
                  SELECT 1 FROM commerce.StorefrontPromotionUses u
                  JOIN sales.SalesInvoices x ON x.InvoiceId=u.InvoiceId AND x.TenantId=u.TenantId
                  OUTER APPLY(SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=x.TenantId AND cp.InvoiceId=x.InvoiceId ORDER BY cp.AttemptNumber DESC) payment
                  WHERE u.TenantId=@tenant AND u.CustomerId=@customer AND x.Status NOT IN(N'CANCELLED',N'VOID') AND (x.Status=N'COMPLETED' OR payment.Status IS NULL OR payment.Status<>N'FAILED')
                ) THEN 1 ELSE 0 END;
                """, connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId);
            hasOrder = (int)(await command.ExecuteScalarAsync(token) ?? 0) > 0;
        }
        var offers = new List<StorefrontPromotionCandidate>();
        await using (var command = new SqlCommand("""
            SELECT p.PromotionId,p.OfferName,p.OfferType,p.MinimumPurchaseAmount,p.DiscountType,p.DiscountValue,p.MaximumDiscount,
              p.StartsAt,p.EndsAt,p.IsActive,p.UsageLimitPerCustomer,p.PromoCode,
              (SELECT COUNT(1) FROM commerce.StorefrontPromotionUses u JOIN sales.SalesInvoices i ON i.InvoiceId=u.InvoiceId AND i.TenantId=u.TenantId
                OUTER APPLY(SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=i.TenantId AND cp.InvoiceId=i.InvoiceId ORDER BY cp.AttemptNumber DESC) payment
                WHERE u.TenantId=@tenant AND u.PromotionId=p.PromotionId AND u.CustomerId=@customer AND i.Status NOT IN(N'CANCELLED',N'VOID') AND (i.Status=N'COMPLETED' OR payment.Status IS NULL OR payment.Status<>N'FAILED'))
            FROM commerce.StorefrontPromotions p WHERE p.TenantId=@tenant AND p.IsDeleted=0 AND (@promoCode IS NULL OR p.PromoCode=@promoCode);
            """, connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId);
            command.Parameters.AddWithValue("@customer", verifiedCustomerId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@promoCode", string.IsNullOrWhiteSpace(promotionCode) ? DBNull.Value : promotionCode.Trim());
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                offers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3),
                    reader.GetString(4), reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7), reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                    reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.GetInt32(12), reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        var requestedCode = string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim();
        if (requestedCode is not null)
        {
            var selected = offers.FirstOrDefault();
            if (selected is null)
                throw new BusinessRuleException("Promo code is not valid.");
            var now = DateTimeOffset.UtcNow;
            if (!selected.IsActive || selected.EndsAt is not null && selected.EndsAt <= now)
                throw new BusinessRuleException("This promo code has expired.");
            if (selected.StartsAt is not null && selected.StartsAt > now)
                throw new BusinessRuleException("Promo code is not valid.");
            var merchandise = lines.Sum(x => x.Quantity * x.UnitPrice * (1m + x.TaxPercentage / 100m));
            if (selected.MinimumPurchaseAmount > merchandise)
            {
                var remaining = selected.MinimumPurchaseAmount - merchandise;
                throw new BusinessRuleException($"Add {remaining.ToString("C0", CultureInfo.GetCultureInfo("en-IN"))} more to use this promo code.");
            }
            if (selected.OfferType == "FIRST_ORDER" && (!verifiedCustomerId.HasValue || hasOrder))
                throw new BusinessRuleException("This promo code is not applicable to this order.");
            if (selected.UsageLimitPerCustomer is not null && (!verifiedCustomerId.HasValue || selected.PreviousUses >= selected.UsageLimitPerCustomer.Value))
                throw new BusinessRuleException("This promo code is no longer available.");
        }        return StorefrontPricingPolicy.Calculate(lines.Select(x => new StorefrontPricedItem(x.Quantity, x.UnitPrice, x.TaxPercentage)).ToArray(),
            settings, pincode, serviceable, offers, verifiedCustomerId is not null, hasOrder, DateTimeOffset.UtcNow, requestedCode);
    }
    public async Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, string? customerSessionToken, CancellationToken token)
    {
        var customerName = input.CustomerName?.Trim();
        var mobile = Digits().Replace(input.Mobile ?? string.Empty, string.Empty);
        var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        var address = input.DeliveryAddress?.Trim();
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Length > 250)
            throw new BusinessRuleException("Enter a valid customer name.");
        if (mobile.Length is < 10 or > 15)
            throw new BusinessRuleException("Enter a valid mobile number.");
        if (email is not null && (!MailAddress.TryCreate(email, out _) || email.Length > 256))
            throw new BusinessRuleException("Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(address) || address.Length > 1000)
            throw new BusinessRuleException("Enter a valid delivery address.");
        if (!Guid.TryParse(idempotencyKey, out var requestKey) || requestKey == Guid.Empty)
            throw new BusinessRuleException("A valid idempotency key is required.");

        var items = NormalizeItems(input.Items);

        var tenantId = await ResolveTenant(storeKey, token)
            ?? throw new EntityNotFoundException("Store was not found.");
        var method = NormalizePaymentMethod(input.PaymentMethod);
        var provider = method == "COD" ? PaymentProviders.Cod : PaymentProviders.Razorpay;
        var methods = await payments.GetEnabledMethodsForTenantAsync(tenantId, token);
        if (methods.All(enabled => enabled.Code != method))
            throw new BusinessRuleException("The selected payment method is not currently available for this store.");

        var cartJson = JsonSerializer.Serialize(items);
        var (warehouseId, lines) = await PriceCart(tenantId, cartJson, items.Length, token);
        var authenticated = string.IsNullOrWhiteSpace(customerSessionToken) ? null : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        var quote = await CalculateQuote(tenantId, lines, input.Pincode, authenticated?.Id, input.PromoCode, token);
        if (!quote.IsDeliveryEnabled)
            throw new BusinessRuleException("Delivery is not currently available for this store.");
        if (!quote.IsPincodeServiceable)
            throw new BusinessRuleException("Sorry, delivery is not available at this pincode yet.");
        var customerId = authenticated?.Id ?? await FindOrCreateCustomer(tenantId, customerName, mobile, email, token);
        var posItems = lines.Select(line => new POSItemInput(line.ProductId, null, line.Quantity,
            line.UnitPrice, 0, 0, line.TaxPercentage)).ToArray();
        var orderRequest = new POSPostRequest(null, null, customerId, warehouseId, null,
            JsonSerializer.Serialize(posItems), "[]", quote.PromotionDiscount, 0, "Storefront order awaiting payment/collection",
            "HELD", false, null, "STOREFRONT", tenantId, "STOREFRONT",
            DeliveryCharge: quote.DeliveryCharge, PromotionDiscountAmount: quote.PromotionDiscount,
            AppliedPromotionId: quote.PromotionId, AppliedPromotionName: quote.PromotionName,
            FreeDeliveryApplied: quote.IsFreeDeliveryUnlocked, FreeDeliveryThresholdSnapshot: quote.FreeDeliveryThreshold,
            ServicePincode: input.Pincode);
        var order = await pos.PostForTenant(orderRequest, tenantId, $"STOREFRONT:{tenantId:N}:{requestKey:N}", token);

        await UpdateOrderMetadata(tenantId, order.InvoiceId, address, method, token);
        var existing = await ExistingPayment(tenantId, order.InvoiceId, provider, method, token);
        if (existing is not null)
            return new(order.InvoiceId, order.InvoiceNumber, existing.Value.Amount, "INR", existing.Value.PaymentId,
                provider == PaymentProviders.Cod ? null : existing.Value.Url, provider, existing.Value.Status, CustomerMessage(method), null, method);

        var attempt = await payments.CreateAttemptForTenantAsync(tenantId,
            new(order.InvoiceId, provider, method), "STOREFRONT", token);
        var checkoutUrl = attempt.PaymentAction ?? attempt.Payment.PaymentLink;
        if (provider == PaymentProviders.Cod) checkoutUrl = null;
        if (provider == PaymentProviders.Razorpay && (string.IsNullOrWhiteSpace(checkoutUrl)
            || !Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var checkoutUri)
            || checkoutUri.Scheme != Uri.UriSchemeHttps))
        {
            StorefrontCheckoutLogs.InvalidPaymentAction(logger, tenantId, order.InvoiceId, provider);
            throw new BusinessRuleException("Online payment is currently unavailable. Please try again.");
        }
        return new(order.InvoiceId, order.InvoiceNumber, attempt.Payment.Amount,
            attempt.Payment.Currency, attempt.Payment.PaymentId, checkoutUrl, provider, attempt.Payment.Status, CustomerMessage(method), null, method);
    }

    private async Task<Guid?> ResolveTenant(string storeKey, CancellationToken token)
    {
        var normalized = storeKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !StoreKeyPattern().IsMatch(normalized)) return null;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;", connection);
        command.Parameters.AddWithValue("@key", normalized);
        return await command.ExecuteScalarAsync(token) is Guid tenant ? tenant : null;
    }

    private async Task<(Guid WarehouseId, IReadOnlyCollection<PricedLine> Lines)> PriceCart(
        Guid tenantId, string cartJson, int expectedItems, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var warehouse = new SqlCommand("""
            SELECT TOP(1) w.WarehouseId
            FROM inventory.Warehouses w
            WHERE w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0
              AND NOT EXISTS (
                SELECT 1 FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j
                WHERE NOT EXISTS (
                  SELECT 1 FROM inventory.InventoryBalances b
                  WHERE b.TenantId=@tenant AND b.WarehouseId=w.WarehouseId AND b.ProductId=j.ProductId
                  GROUP BY b.ProductId HAVING SUM(b.QuantityOnHand-b.QuantityReserved)>=j.Quantity))
            ORDER BY w.IsDefault DESC,w.WarehouseName;
            """, connection);
        warehouse.Parameters.AddWithValue("@tenant", tenantId);
        warehouse.Parameters.AddWithValue("@items", cartJson);
        if (await warehouse.ExecuteScalarAsync(token) is not Guid warehouseId)
            throw new BusinessRuleException("The cart items are not available together at this store.");

        await using var products = new SqlCommand("""
            SELECT p.ProductId,j.Quantity,p.SellingPrice,p.GSTPercentage
            FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j
            JOIN master.Products p ON p.ProductId=j.ProductId AND p.TenantId=@tenant
            JOIN master.ProductCategories c ON c.ProductCategoryId=p.CategoryId AND c.IsActive=1 AND c.IsDeleted=0
            JOIN master.UnitsOfMeasure u ON u.UnitId=p.UnitId AND u.IsActive=1 AND u.IsDeleted=0
            WHERE p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1
              AND EXISTS(SELECT 1 FROM master.TenantProductCategories x WHERE x.TenantId=@tenant AND x.ProductCategoryId=p.CategoryId)
              AND EXISTS(SELECT 1 FROM master.TenantUnitsOfMeasure x WHERE x.TenantId=@tenant AND x.UnitId=p.UnitId);
            """, connection);
        products.Parameters.AddWithValue("@tenant", tenantId);
        products.Parameters.AddWithValue("@items", cartJson);
        var lines = new List<PricedLine>();
        await using var reader = await products.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            lines.Add(new(reader.GetGuid(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        if (lines.Count != expectedItems)
            throw new BusinessRuleException("One or more cart items are no longer available.");
        return (warehouseId, lines);
    }

    private async Task<Guid> FindOrCreateCustomer(Guid tenantId, string name, string mobile, string? email,
        CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        await using var find = new SqlCommand("""
            SELECT TOP(1) CustomerId FROM sales.Customers WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0
              AND REPLACE(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),N'(',N'') LIKE N'%'+@mobile
            ORDER BY CreatedOn;
            """, connection, transaction);
        find.Parameters.AddWithValue("@tenant", tenantId);
        find.Parameters.AddWithValue("@mobile", mobile);
        var existing = await find.ExecuteScalarAsync(token);
        if (existing is Guid customerId)
        {
            await transaction.CommitAsync(token);
            return customerId;
        }

        var id = Guid.NewGuid();
        await using var insert = new SqlCommand("""
            INSERT sales.Customers(CustomerId,TenantId,CustomerCode,CustomerName,CustomerType,Email,Mobile,
              Currency,CreditLimit,OpeningBalance,IsGSTRegistered,IsActive,IsDeleted,Remarks,CreatedBy)
            VALUES(@id,@tenant,@code,@name,N'RETAIL',@email,@mobile,N'INR',0,0,0,1,0,N'Created by customer storefront',N'STOREFRONT');
            """, connection, transaction);
        insert.Parameters.AddWithValue("@id", id);
        insert.Parameters.AddWithValue("@tenant", tenantId);
        insert.Parameters.AddWithValue("@code", $"WEB-{id:N}"[..16]);
        insert.Parameters.AddWithValue("@name", name);
        insert.Parameters.AddWithValue("@email", email ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("@mobile", mobile);
        await insert.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return id;
    }

    private async Task UpdateOrderMetadata(Guid tenantId, Guid orderId, string address, string method, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppCommerceOrders
            SET DeliveryAddress=@address,FulfillmentMethod=N'RETAILER_DELIVERY',PaymentType=@paymentType
            WHERE TenantId=@tenant AND InvoiceId=@order;
            """, connection);
        command.Parameters.AddWithValue("@address", address);
        command.Parameters.AddWithValue("@paymentType", method);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@order", orderId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<(Guid PaymentId, decimal Amount, string Status, string? Url)?> ExistingPayment(Guid tenantId, Guid orderId, string provider, string method, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) PaymentId,Amount,Status,COALESCE(PaymentLink,ProviderReference) FROM commerce.CommercePayments
            WHERE TenantId=@tenant AND InvoiceId=@order AND Provider=@provider AND PaymentMethod=@method
              AND Status IN(N'PENDING',N'PENDING_VERIFICATION',N'COD_PENDING')
            ORDER BY AttemptNumber DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@order", orderId);
        command.Parameters.AddWithValue("@provider", provider); command.Parameters.AddWithValue("@method", method);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }    private async Task<SqlConnection> Open(Guid tenantId, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection);
        context.Parameters.AddWithValue("@tenant", tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }

    private sealed record PricedLine(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal TaxPercentage);
    private static string NormalizePaymentMethod(string? method)
    {
        var value = method?.Trim().ToUpperInvariant();
        if (value is "COD" or "UPI" or "NET_BANKING") return value;
        throw new BusinessRuleException("Select a supported payment method.");
    }
    private static string CustomerMessage(string method) => method == "COD"
        ? "Your order is confirmed for cash payment on delivery."
        : "Payment is processing. The order will update when Razorpay confirms it.";
    [GeneratedRegex("^[A-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant)] private static partial Regex StoreKeyPattern();
    [GeneratedRegex("[^0-9]+", RegexOptions.CultureInvariant)] private static partial Regex Digits();
}

internal static partial class StorefrontCheckoutLogs
{
    [LoggerMessage(3401, LogLevel.Warning, "Storefront payment provider {Provider} returned an invalid action for tenant {TenantId}, order {OrderId}.")]
    public static partial void InvalidPaymentAction(ILogger logger, Guid tenantId, Guid orderId, string provider);
}
