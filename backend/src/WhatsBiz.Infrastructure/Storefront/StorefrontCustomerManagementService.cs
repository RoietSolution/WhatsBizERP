using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontCustomerManagementService(IConfiguration configuration) : IStorefrontCustomerManagementService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontCustomerManagementPageDto> SearchAsync(Guid tenantId, string? search, string sortBy, bool descending, int pageNumber, int pageSize, CancellationToken token)
    {
        pageNumber = Math.Clamp(pageNumber, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var order = sortBy.ToLowerInvariant() switch
        {
            "registeredon" => "RegisteredOn",
            "lastsuccessfullogin" => "LastSuccessfulLogin",
            "lastvisit" => "LastVisit",
            "totalorders" => "TotalOrders",
            "totalspent" => "TotalSpent",
            _ => "CustomerName"
        };
        var direction = descending ? "DESC" : "ASC";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        const string sourceFilter = "c.TenantId=@tenant AND c.IsDeleted=0 AND (c.CreatedBy IN(N'STOREFRONT',N'STOREFRONT_OTP') OR EXISTS (SELECT 1 FROM commerce.AnalyticsEvents ae WHERE ae.TenantId=c.TenantId AND ae.CustomerId=c.CustomerId AND ae.EventType=N'CUSTOMER_SIGN_IN'))";
        var countSql = $"SELECT COUNT(1) FROM sales.Customers c WHERE {sourceFilter} AND (@search IS NULL OR c.CustomerName LIKE N'%'+@search+N'%' OR c.Mobile LIKE N'%'+@search+N'%');";
        await using var count = new SqlCommand(countSql, connection); Add(count, "@tenant", tenantId); Add(count, "@search", search);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        var sql = $"""
WITH CustomerData AS
(
    SELECT c.CustomerId,c.CustomerName,c.Mobile,c.CreatedOn,c.IsActive,
           (SELECT MAX(a.CreatedOn) FROM commerce.AnalyticsEvents a WHERE a.TenantId=c.TenantId AND a.CustomerId=c.CustomerId AND a.EventType=N'CUSTOMER_SIGN_IN') AS LastSuccessfulLogin,
           (SELECT MAX(a.CreatedOn) FROM commerce.AnalyticsEvents a WHERE a.TenantId=c.TenantId AND a.CustomerId=c.CustomerId AND a.EventType=N'STORE_VISIT') AS LastVisit,
           (SELECT COUNT(1) FROM sales.SalesInvoices i JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT' WHERE i.TenantId=c.TenantId AND i.CustomerId=c.CustomerId AND i.Status NOT IN(N'CANCELLED',N'VOID')) AS TotalOrders,
           COALESCE((SELECT SUM(i.GrandTotal) FROM sales.SalesInvoices i JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT' WHERE i.TenantId=c.TenantId AND i.CustomerId=c.CustomerId AND i.Status NOT IN(N'CANCELLED',N'VOID')),0) AS TotalSpent
    FROM sales.Customers c
    WHERE {sourceFilter} AND (@search IS NULL OR c.CustomerName LIKE N'%'+@search+N'%' OR c.Mobile LIKE N'%'+@search+N'%')
)
SELECT CustomerId,CustomerName,Mobile,CreatedOn,LastSuccessfulLogin,LastVisit,TotalOrders,TotalSpent,IsActive
FROM CustomerData ORDER BY {order} {direction}, CustomerId OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
""";
        await using var command = new SqlCommand(sql, connection); Add(command, "@tenant", tenantId); Add(command, "@search", search); Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
        var rows = new List<StorefrontCustomerManagementRowDto>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(ReadRow(reader));
        return new(rows, total, pageNumber, pageSize);
    }

    public async Task<StorefrontCustomerManagementDetailDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        const string sql = """
SELECT c.CustomerId,c.CustomerName,c.Mobile,c.CreatedOn,
 (SELECT MAX(a.CreatedOn) FROM commerce.AnalyticsEvents a WHERE a.TenantId=c.TenantId AND a.CustomerId=c.CustomerId AND a.EventType=N'CUSTOMER_SIGN_IN'),
 (SELECT MAX(a.CreatedOn) FROM commerce.AnalyticsEvents a WHERE a.TenantId=c.TenantId AND a.CustomerId=c.CustomerId AND a.EventType=N'STORE_VISIT'),
 (SELECT COUNT(1) FROM sales.SalesInvoices i JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT' WHERE i.TenantId=c.TenantId AND i.CustomerId=c.CustomerId AND i.Status NOT IN(N'CANCELLED',N'VOID')),
 COALESCE((SELECT SUM(i.GrandTotal) FROM sales.SalesInvoices i JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT' WHERE i.TenantId=c.TenantId AND i.CustomerId=c.CustomerId AND i.Status NOT IN(N'CANCELLED',N'VOID')),0),c.IsActive
FROM sales.Customers c
WHERE c.TenantId=@tenant AND c.CustomerId=@customer AND c.IsDeleted=0
  AND (c.CreatedBy IN(N'STOREFRONT',N'STOREFRONT_OTP') OR EXISTS(SELECT 1 FROM commerce.AnalyticsEvents a WHERE a.TenantId=c.TenantId AND a.CustomerId=c.CustomerId AND a.EventType=N'CUSTOMER_SIGN_IN'));
SELECT TOP(100)i.InvoiceNumber,i.InvoiceDate,i.Status,i.GrandTotal FROM sales.SalesInvoices i JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT' WHERE i.TenantId=@tenant AND i.CustomerId=@customer ORDER BY i.InvoiceDate DESC;
SELECT TOP(100)a.EventType,a.CreatedOn,a.ProductId,p.ProductName FROM commerce.AnalyticsEvents a LEFT JOIN master.Products p ON p.TenantId=a.TenantId AND p.ProductId=a.ProductId WHERE a.TenantId=@tenant AND a.CustomerId=@customer ORDER BY a.CreatedOn DESC;
SELECT TOP(100)r.ReturnRequestId,r.InvoiceId,r.InvoiceItemId,i.InvoiceNumber,p.ProductName,p.ProductCode,r.Quantity,r.Reason,r.Explanation,r.Status,r.RequestedAtUtc,r.DecidedAtUtc,r.DecisionNote
FROM commerce.StorefrontReturnRequests r JOIN sales.SalesInvoices i ON i.TenantId=r.TenantId AND i.InvoiceId=r.InvoiceId JOIN sales.SalesInvoiceItems x ON x.InvoiceId=r.InvoiceId AND x.InvoiceItemId=r.InvoiceItemId JOIN master.Products p ON p.TenantId=r.TenantId AND p.ProductId=x.ProductId
WHERE r.TenantId=@tenant AND r.CustomerId=@customer ORDER BY r.RequestedAtUtc DESC;
SELECT TOP(100)OriginalKeyword,Outcome,CreatedAtUtc FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CustomerId=@customer ORDER BY CreatedAtUtc DESC;
SELECT TOP(100)w.ProductId,p.ProductName,p.SellingPrice,w.CreatedAt FROM commerce.StorefrontWishlistItems w JOIN master.Products p ON p.TenantId=w.TenantId AND p.ProductId=w.ProductId WHERE w.TenantId=@tenant AND w.CustomerId=@customer ORDER BY w.CreatedAt DESC;
""";
        await using var command = new SqlCommand(sql, connection); Add(command, "@tenant", tenantId); Add(command, "@customer", customerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var customer = ReadRow(reader);
        var orders = new List<StorefrontCustomerManagementOrderDto>(); await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) orders.Add(new(reader.GetString(0), reader.GetDateTimeOffset(1), reader.GetString(2), reader.GetDecimal(3)));
        var activity = new List<StorefrontCustomerManagementActivityDto>(); await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) activity.Add(new(reader.GetString(0), reader.GetDateTimeOffset(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        var returns = new List<StorefrontReturnRequestDto>(); await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) returns.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetDecimal(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetDateTimeOffset(10), reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
        var searches = new List<StorefrontCustomerManagementSearchDto>(); await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) searches.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        var wishlist = new List<StorefrontCustomerManagementWishlistDto>(); await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) wishlist.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDateTimeOffset(3)));
        return new(customer, orders, activity, returns, searches, wishlist);
    }

    private static StorefrontCustomerManagementRowDto ReadRow(SqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTimeOffset(3), reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4), reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetBoolean(8));
    private static void Add(SqlCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
