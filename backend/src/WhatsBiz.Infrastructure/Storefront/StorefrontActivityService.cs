using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontActivityService(IConfiguration configuration, IStorefrontCustomerService customers, ILogger<StorefrontActivityService> logger) : IStorefrontActivityService
{
    private static readonly Action<ILogger, Guid, Exception?> ReportSqlFailure = LoggerMessage.Define<Guid>(LogLevel.Error, new EventId(6101, nameof(ReportSqlFailure)), "Storefront activity SQL report failed for tenant {TenantId}.");
    private static readonly Action<ILogger, string, Guid?, Exception?> RecordSqlFailure = LoggerMessage.Define<string, Guid?>(LogLevel.Error, new EventId(6103, nameof(RecordSqlFailure)), "Storefront activity event {EventType} could not be recorded for tenant {TenantId}; tracking is best-effort.");
    private static readonly HashSet<string> Events = new(StringComparer.OrdinalIgnoreCase) { "STORE_VISIT", "CUSTOMER_SIGN_IN", "CUSTOMER_SIGN_OUT", "PRODUCT_VIEW", "CATEGORY_VIEW", "SEARCH", "ADD_TO_CART", "REMOVE_FROM_CART", "ADD_TO_WISHLIST", "REMOVE_FROM_WISHLIST", "CHECKOUT_STARTED", "ORDER_PLACED", "REFERRAL_OPENED", "REFERRAL_SHARED" };
    private static readonly Regex VisitorPattern = new("^[A-Za-z0-9_-]{16,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task RecordAsync(string storeKey, string? sessionToken, StorefrontActivityInput input, CancellationToken token)
    {
        if (!Events.Contains(input.EventType) || !VisitorPattern.IsMatch(input.VisitorSessionId)) return;
        Guid? tenantId = null;
        try
        {
            tenantId = await ResolveTenant(storeKey, token); if (tenantId is null) return;
            var customer = string.IsNullOrWhiteSpace(sessionToken) ? null : await customers.GetSessionAsync(storeKey, sessionToken, token);
            var metadata = string.IsNullOrWhiteSpace(input.SearchTerm) ? null : $"{{\"search\":{System.Text.Json.JsonSerializer.Serialize(input.SearchTerm[..Math.Min(100,input.SearchTerm.Length)])}}}";
            await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
            // AnalyticsEventId has a NEWSEQUENTIALID() DEFAULT. SQL Server does not
            // allow NEWSEQUENTIALID() to be called directly in an INSERT statement.
            await using var command = new SqlCommand("INSERT commerce.AnalyticsEvents(TenantId,EventType,CustomerId,ProductId,MetadataJson,VisitorSessionId) VALUES(@tenant,@type,@customer,@product,@metadata,@visitor);", connection);
            command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId.Value;
            command.Parameters.Add("@type", System.Data.SqlDbType.NVarChar, 60).Value = input.EventType.ToUpperInvariant();
            command.Parameters.Add("@customer", System.Data.SqlDbType.UniqueIdentifier).Value = customer?.Id ?? (object)DBNull.Value;
            command.Parameters.Add("@product", System.Data.SqlDbType.UniqueIdentifier).Value = input.ProductId ?? (object)DBNull.Value;
            command.Parameters.Add("@metadata", System.Data.SqlDbType.NVarChar, 4000).Value = (object?)metadata ?? DBNull.Value;
            command.Parameters.Add("@visitor", System.Data.SqlDbType.VarChar, 64).Value = input.VisitorSessionId;
            await command.ExecuteNonQueryAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Activity is best-effort for storefront browsing, but retain a
            // diagnostic trail rather than making persistence failures look
            // like genuine zero activity in the retailer dashboard.
            RecordSqlFailure(logger, input.EventType.ToUpperInvariant(), tenantId, exception);
        }
    }

    public async Task<StorefrontActivityReportDto> ReportAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset toDate, CancellationToken token)
    {
        from = from.ToUniversalTime(); toDate = toDate.ToUniversalTime();
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        const string sql = """
WITH FirstSignIn AS
(
    SELECT CustomerId, MIN(CreatedOn) AS FirstSignInOn
    FROM commerce.AnalyticsEvents
    WHERE TenantId=@tenant AND EventType=N'CUSTOMER_SIGN_IN' AND CustomerId IS NOT NULL
    GROUP BY CustomerId
), PeriodEvents AS
(
    SELECT EventType, CustomerId, VisitorSessionId, CreatedOn
    FROM commerce.AnalyticsEvents
    WHERE TenantId=@tenant AND CreatedOn>=@from AND CreatedOn<@to
)
SELECT
    CONVERT(int,COUNT(DISTINCT CASE WHEN e.EventType=N'STORE_VISIT' AND e.CreatedOn>=CONVERT(datetimeoffset,CONVERT(date,SYSUTCDATETIME())) THEN e.VisitorSessionId END)),
    CONVERT(int,COUNT(DISTINCT CASE WHEN e.EventType=N'STORE_VISIT' THEN e.VisitorSessionId END)),
    CONVERT(int,COUNT(DISTINCT CASE WHEN e.EventType=N'CUSTOMER_SIGN_IN' THEN e.CustomerId END)),
    CONVERT(int,COUNT(DISTINCT CASE WHEN e.EventType=N'CUSTOMER_SIGN_IN' AND fs.FirstSignInOn>=@from THEN e.CustomerId END)),
    CONVERT(int,COUNT(DISTINCT CASE WHEN e.EventType=N'CUSTOMER_SIGN_IN' AND fs.FirstSignInOn<@from THEN e.CustomerId END)),
    CONVERT(int,COALESCE(SUM(CASE WHEN e.EventType=N'PRODUCT_VIEW' THEN 1 ELSE 0 END),0)),
    CONVERT(int,COALESCE(SUM(CASE WHEN e.EventType=N'ADD_TO_CART' THEN 1 ELSE 0 END),0)),
    CONVERT(int,COALESCE(SUM(CASE WHEN e.EventType=N'CHECKOUT_STARTED' THEN 1 ELSE 0 END),0)),
    CONVERT(int,COALESCE(SUM(CASE WHEN e.EventType=N'ORDER_PLACED' THEN 1 ELSE 0 END),0))
FROM PeriodEvents e
LEFT JOIN FirstSignIn fs ON fs.CustomerId=e.CustomerId;

WITH PeriodEvents AS
(
    SELECT * FROM commerce.AnalyticsEvents
    WHERE TenantId=@tenant AND CreatedOn>=@from AND CreatedOn<@to
), ActivityGroups AS
(
    SELECT CustomerId, CASE WHEN CustomerId IS NULL THEN VisitorSessionId ELSE NULL END AS VisitorSessionId, MAX(CreatedOn) AS LastActivityOn,
           MAX(CASE WHEN EventType=N'STORE_VISIT' THEN CreatedOn END) AS LastVisitOn,
           MAX(CASE WHEN EventType=N'CUSTOMER_SIGN_IN' THEN CreatedOn END) AS LastSignInOn,
           CONVERT(int,COUNT(DISTINCT VisitorSessionId)) AS SessionCount,
           CONVERT(int,SUM(CASE WHEN EventType=N'ORDER_PLACED' THEN 1 ELSE 0 END)) AS OrderCount,
           MAX(EventType) AS LastEventType
    FROM PeriodEvents
    GROUP BY CustomerId, CASE WHEN CustomerId IS NULL THEN VisitorSessionId ELSE NULL END
)
SELECT TOP(100)
    CASE WHEN g.CustomerId IS NULL THEN CONCAT(N'Anonymous visitor ',LEFT(g.VisitorSessionId,8))
         ELSE COALESCE(c.CustomerName,N'Signed-in customer') END,
    CASE WHEN g.CustomerId IS NULL THEN NULL ELSE c.Mobile END,
    COALESCE(g.LastVisitOn,g.LastActivityOn), g.LastSignInOn, g.SessionCount,
    g.OrderCount, g.LastEventType
FROM ActivityGroups g
LEFT JOIN sales.Customers c ON c.TenantId=@tenant AND c.CustomerId=g.CustomerId
ORDER BY g.LastActivityOn DESC;
""";
        await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@from",from);command.Parameters.AddWithValue("@to",toDate);
        try
        {
            await using var reader=await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("The activity summary query returned no row.");
            var summary=new StorefrontActivitySummaryDto(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetInt32(8));
            var rows=new List<StorefrontActivityRowDto>(); await reader.NextResultAsync(token);
            while(await reader.ReadAsync(token)) rows.Add(new(reader.IsDBNull(1)?"Guest":"Signed-in customer",reader.IsDBNull(1)?null:reader.GetString(1),reader.GetDateTimeOffset(2),reader.IsDBNull(3)?null:reader.GetDateTimeOffset(3),reader.GetInt32(4),reader.GetInt32(5),reader.GetString(6)));
            return new(summary,rows);
        }
        catch (SqlException exception)
        {
            ReportSqlFailure(logger, tenantId, exception);
            throw new InvalidOperationException("Storefront activity report could not be loaded.", exception);
        }
    }

    public async Task<StorefrontBehaviorReportDto> BehaviorAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset toDate, CancellationToken token)
    {
        from = from.ToUniversalTime(); toDate = toDate.ToUniversalTime();
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        const string sql = """
SELECT TOP (500) e.EventType,e.CustomerId,e.VisitorSessionId,e.ProductId,p.ProductName,e.CreatedOn,
       CASE WHEN e.CustomerId IS NULL THEN CONCAT(N'Anonymous visitor ',LEFT(e.VisitorSessionId,8)) ELSE COALESCE(c.CustomerName,N'Signed-in customer') END
FROM commerce.AnalyticsEvents e
LEFT JOIN sales.Customers c ON c.TenantId=e.TenantId AND c.CustomerId=e.CustomerId
LEFT JOIN master.Products p ON p.TenantId=e.TenantId AND p.ProductId=e.ProductId
WHERE e.TenantId=@tenant AND e.CreatedOn>=@from AND e.CreatedOn<@to
ORDER BY e.CreatedOn DESC;
SELECT TOP (500) w.CustomerId,c.CustomerName,w.ProductId,p.ProductName,p.SellingPrice,
       CASE WHEN ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b JOIN inventory.Warehouses wh ON wh.WarehouseId=b.WarehouseId AND wh.TenantId=w.TenantId AND wh.IsActive=1 AND wh.IsDeleted=0 WHERE b.TenantId=w.TenantId AND b.ProductId=w.ProductId),0)>0 THEN N'IN_STOCK' ELSE N'OUT_OF_STOCK' END,
       w.CreatedAt
FROM commerce.StorefrontWishlistItems w
JOIN sales.Customers c ON c.TenantId=w.TenantId AND c.CustomerId=w.CustomerId
JOIN master.Products p ON p.TenantId=w.TenantId AND p.ProductId=w.ProductId
WHERE w.TenantId=@tenant AND w.CreatedAt>=@from AND w.CreatedAt<@to
ORDER BY w.CreatedAt DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@from", from); command.Parameters.AddWithValue("@to", toDate);
        var events = new List<StorefrontBehaviorEventDto>(); var wishlist = new List<StorefrontWishlistActivityDto>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) events.Add(new(reader.GetString(0), reader.GetString(6), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTimeOffset(5)));
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) wishlist.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetDecimal(4), reader.GetString(5), reader.GetDateTimeOffset(6)));
        return new(events, wishlist);
    }
    private async Task<Guid?> ResolveTenant(string storeKey,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;",c);q.Parameters.AddWithValue("@key",storeKey.Trim().ToUpperInvariant());return await q.ExecuteScalarAsync(token) is Guid id?id:null;}
}
