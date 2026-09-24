using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed partial class StorefrontCheckoutService(
    IConfiguration configuration,
    IPOSEngine pos,
    ICommercePaymentService payments) : IStorefrontCheckoutService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, CancellationToken token)
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

        if (input.Items is null) throw new BusinessRuleException("The cart is empty.");
        var items = input.Items
            .GroupBy(item => item.ProductId)
            .Select(group => new StorefrontCheckoutItem(group.Key, group.Sum(item => item.Quantity)))
            .ToArray();
        if (items.Length is 0 or > 100 || items.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0 || item.Quantity > 9999))
            throw new BusinessRuleException("The cart contains invalid items or quantities.");

        var tenantId = await ResolveTenant(storeKey, token)
            ?? throw new EntityNotFoundException("Store was not found.");
        var methods = await payments.GetEnabledMethodsForTenantAsync(tenantId, token);
        if (methods.All(method => method.Provider != PaymentProviders.Razorpay))
            throw new BusinessRuleException("Online payment is not currently available for this store.");

        var cartJson = JsonSerializer.Serialize(items);
        var (warehouseId, lines) = await PriceCart(tenantId, cartJson, items.Length, token);
        var customerId = await FindOrCreateCustomer(tenantId, customerName, mobile, email, token);
        var posItems = lines.Select(line => new POSItemInput(line.ProductId, null, line.Quantity,
            line.UnitPrice, 0, 0, line.TaxPercentage)).ToArray();
        var orderRequest = new POSPostRequest(null, null, customerId, warehouseId, null,
            JsonSerializer.Serialize(posItems), "[]", 0, 0, "Storefront order awaiting Razorpay payment",
            "HELD", false, null, "STOREFRONT", tenantId, "STOREFRONT");
        var order = await pos.PostForTenant(orderRequest, tenantId, $"STOREFRONT:{tenantId:N}:{requestKey:N}", token);

        await UpdateOrderMetadata(tenantId, order.InvoiceId, address, token);
        var existing = await ExistingPayment(tenantId, order.InvoiceId, token);
        if (existing is not null && Uri.TryCreate(existing.Value.Url, UriKind.Absolute, out var existingUri)
            && existingUri.Scheme == Uri.UriSchemeHttps)
            return new(order.InvoiceId, order.InvoiceNumber, order.GrandTotal, "INR", existing.Value.PaymentId, existingUri.AbsoluteUri);

        var attempt = await payments.CreateAttemptForTenantAsync(tenantId,
            new(order.InvoiceId, PaymentProviders.Razorpay), "STOREFRONT", token);
        var checkoutUrl = attempt.PaymentAction ?? attempt.Payment.PaymentLink;
        if (string.IsNullOrWhiteSpace(checkoutUrl)
            || !Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var checkoutUri)
            || checkoutUri.Scheme != Uri.UriSchemeHttps)
            throw new BusinessRuleException("Razorpay did not return a checkout URL.");
        return new(order.InvoiceId, order.InvoiceNumber, attempt.Payment.Amount,
            attempt.Payment.Currency, attempt.Payment.PaymentId, checkoutUri.AbsoluteUri);
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

    private async Task UpdateOrderMetadata(Guid tenantId, Guid orderId, string address, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppCommerceOrders
            SET DeliveryAddress=@address,FulfillmentMethod=N'RETAILER_DELIVERY',PaymentType=N'ONLINE'
            WHERE TenantId=@tenant AND InvoiceId=@order;
            """, connection);
        command.Parameters.AddWithValue("@address", address);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@order", orderId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<(Guid PaymentId, string Url)?> ExistingPayment(Guid tenantId, Guid orderId,
        CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) PaymentId,PaymentLink FROM commerce.CommercePayments
            WHERE TenantId=@tenant AND InvoiceId=@order AND Provider=N'RAZORPAY'
              AND Status=N'PENDING' AND PaymentLink IS NOT NULL
            ORDER BY AttemptNumber DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@order", orderId);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private async Task<SqlConnection> Open(Guid tenantId, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection);
        context.Parameters.AddWithValue("@tenant", tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }

    private sealed record PricedLine(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal TaxPercentage);
    [GeneratedRegex("^[A-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant)] private static partial Regex StoreKeyPattern();
    [GeneratedRegex("[^0-9]+", RegexOptions.CultureInvariant)] private static partial Regex Digits();
}
