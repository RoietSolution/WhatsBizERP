using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontCustomerService(
    IConfiguration configuration,
    IDataProtectionProvider dataProtection,
    IStorefrontService storefront) : IStorefrontCustomerService
{
    private static readonly Regex StoreKeyPattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IDataProtector protector = dataProtection.CreateProtector("WhatsBiz.Storefront.CustomerSession.v1");
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<string> IssueSessionAsync(string storeKey, Guid tenantId, Guid customerId, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@customer", customerId);
        if ((int)(await command.ExecuteScalarAsync(token) ?? 0) != 1) throw new InvalidOperationException("Customer is unavailable.");
        var payload = new SessionPayload(storeKey.Trim().ToUpperInvariant(), tenantId, customerId, DateTimeOffset.UtcNow.AddDays(90));
        return protector.Protect(JsonSerializer.Serialize(payload));
    }

    public async Task<StorefrontCustomerDto?> GetSessionAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("SELECT CustomerId,CustomerName,Email,Mobile FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }

    public async Task<IReadOnlyCollection<StorefrontCustomerOrderDto>?> GetOrdersAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT TOP(100) i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.Status,i.GrandTotal,
              CASE WHEN d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL THEN N'READY_FOR_PICKUP' ELSE d.DeliveryStatus END,w.TrackingNumber,COALESCE(cp.Provider,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),
              (SELECT COUNT(1) FROM sales.SalesInvoiceItems x WHERE x.InvoiceId=i.InvoiceId)
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            OUTER APPLY(SELECT TOP(1) p.Provider,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            WHERE i.TenantId=@tenant AND i.CustomerId=@customer
            ORDER BY i.InvoiceDate DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        var result = new List<StorefrontCustomerOrderDto>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var invoiceStatus = reader.GetString(3); var delivery = reader.IsDBNull(5) ? null : reader.GetString(5);
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), StorefrontOrderPresentation.Status(invoiceStatus, delivery),
                reader.GetDecimal(4), delivery, reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetInt32(9)));
        }
        return result;
    }

    public async Task<StorefrontCustomerOrderDto?> GetOrderAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null || orderId == Guid.Empty) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT i.InvoiceNumber,i.InvoiceDate,i.Status,i.GrandTotal,CASE WHEN d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL THEN N'READY_FOR_PICKUP' ELSE d.DeliveryStatus END,w.TrackingNumber,
              COALESCE(cp.Provider,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),d.ReadyAt,d.OutForDeliveryAt,d.DeliveredAt,d.FailedAt,d.UpdatedAt,i.DeliveryCharge,i.PromotionDiscountAmount,i.AppliedPromotionName,i.Subtotal+i.TaxAmount
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            OUTER APPLY(SELECT TOP(1) p.Provider,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND i.InvoiceId=@order;
            """, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId); command.Parameters.AddWithValue("@customer", session.CustomerId); command.Parameters.AddWithValue("@order", orderId);
        string number, invoiceStatus, paymentMethod, paymentStatus; string? delivery, tracking; DateTimeOffset placed; decimal total;
        DateTimeOffset? packed, outAt, delivered, failed, cancelled; decimal deliveryCharge,promotionDiscount,merchandiseAmount; string? promotionName;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return null;
            number=reader.GetString(0); placed=reader.GetDateTimeOffset(1); invoiceStatus=reader.GetString(2); total=reader.GetDecimal(3);
            delivery=reader.IsDBNull(4)?null:reader.GetString(4); tracking=reader.IsDBNull(5)?null:reader.GetString(5); paymentMethod=reader.GetString(6); paymentStatus=reader.GetString(7);
            packed=reader.IsDBNull(8)?null:reader.GetDateTimeOffset(8); outAt=reader.IsDBNull(9)?null:reader.GetDateTimeOffset(9); delivered=reader.IsDBNull(10)?null:reader.GetDateTimeOffset(10); failed=reader.IsDBNull(11)?null:reader.GetDateTimeOffset(11);
            cancelled=StorefrontOrderPresentation.Status(invoiceStatus,delivery)=="Cancelled"&&!reader.IsDBNull(12)?reader.GetDateTimeOffset(12):null;
            deliveryCharge=reader.GetDecimal(13);promotionDiscount=reader.GetDecimal(14);promotionName=reader.IsDBNull(15)?null:reader.GetString(15);merchandiseAmount=reader.GetDecimal(16);
        }
        var lines = new List<StorefrontCustomerOrderLineDto>();
        await using (var items = new SqlCommand("""SELECT x.ProductId,p.ProductName,p.PackSize,x.Quantity,x.LineTotal FROM sales.SalesInvoiceItems x JOIN master.Products p ON p.ProductId=x.ProductId AND p.TenantId=@tenant WHERE x.InvoiceId=@order ORDER BY p.ProductName;""", connection))
        {
            items.Parameters.AddWithValue("@tenant",session.TenantId);items.Parameters.AddWithValue("@order",orderId);
            await using var reader=await items.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))lines.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetDecimal(3),reader.GetDecimal(4)));
        }
        var status=StorefrontOrderPresentation.Status(invoiceStatus,delivery);
        return new(orderId,number,placed,status,total,delivery,tracking,paymentMethod,paymentStatus,lines.Count,lines,
            StorefrontOrderPresentation.Timeline(invoiceStatus,delivery,placed,packed,outAt,delivered,failed,cancelled),
            deliveryCharge,promotionDiscount,promotionName,merchandiseAmount);
    }

    public async Task<IReadOnlyCollection<StorefrontProductDto>?> GetWishlistAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("SELECT ProductId FROM commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer ORDER BY CreatedAt DESC;", connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) ids.Add(reader.GetGuid(0));
        var products = new List<StorefrontProductDto>();
        foreach (var id in ids)
        {
            var product = await storefront.GetProductAsync(storeKey, id, token);
            if (product is not null) products.Add(product);
        }
        return products;
    }

    public Task<bool> AddWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token)
        => ChangeWishlist(storeKey, sessionToken, productId, true, token);
    public Task<bool> RemoveWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token)
        => ChangeWishlist(storeKey, sessionToken, productId, false, token);

    public async Task<bool> MergeWishlistAsync(string storeKey, string sessionToken, IReadOnlyCollection<Guid> productIds, CancellationToken token)
    {
        if (productIds.Count > 200 || await GetSessionAsync(storeKey, sessionToken, token) is null) return false;
        foreach (var productId in productIds.Distinct())
            await ChangeWishlist(storeKey, sessionToken, productId, true, token);
        return true;
    }

    private async Task<bool> ChangeWishlist(string storeKey, string sessionToken, Guid productId, bool add, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null || productId == Guid.Empty) return false;
        if (add && await storefront.GetProductAsync(storeKey, productId, token) is null) return false;
        await using var connection = await Open(session.TenantId, token);
        var sql = add
            ? "IF NOT EXISTS(SELECT 1 FROM commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer AND ProductId=@product) INSERT commerce.StorefrontWishlistItems(TenantId,CustomerId,ProductId) VALUES(@tenant,@customer,@product);"
            : "DELETE commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer AND ProductId=@product;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        command.Parameters.AddWithValue("@product", productId);
        await command.ExecuteNonQueryAsync(token);
        return true;
    }

    private async Task<SessionPayload?> Resolve(string storeKey, string tokenValue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tokenValue) || tokenValue.Length > 4096) return null;
        SessionPayload? payload;
        try { payload = JsonSerializer.Deserialize<SessionPayload>(protector.Unprotect(tokenValue)); }
        catch { return null; }
        if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var normalized = storeKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !StoreKeyPattern.IsMatch(normalized) || !string.Equals(payload.StoreKey, normalized, StringComparison.Ordinal)) return null;
        await using var connection = await Open(payload.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT COUNT(1) FROM core.Tenants t
            JOIN sales.Customers c ON c.TenantId=t.TenantId AND c.CustomerId=@customer AND c.IsActive=1 AND c.IsDeleted=0
            WHERE t.TenantId=@tenant AND t.TenantKey=@storeKey AND t.IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@tenant", payload.TenantId);
        command.Parameters.AddWithValue("@customer", payload.CustomerId);
        command.Parameters.AddWithValue("@storeKey", normalized);
        return (int)(await command.ExecuteScalarAsync(token) ?? 0) == 1 ? payload : null;
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

    private sealed record SessionPayload(string StoreKey, Guid TenantId, Guid CustomerId, DateTimeOffset ExpiresAt);
}