using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class CustomerDemandReportingService(IConfiguration configuration, CustomerDemandService requests) : ICustomerDemandService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
    public Task<CustomerDemandSummaryDto> RecordSearchAsync(string storeKey, string? sessionToken, StorefrontSearchEventInput input, CancellationToken token) => requests.RecordSearchAsync(storeKey, sessionToken, input, token);
    public Task<StorefrontAvailabilityRequestDto> RequestAvailabilityAsync(string storeKey, Guid productId, string? sessionToken, StorefrontAvailabilityRequestInput input, CancellationToken token) => requests.RequestAvailabilityAsync(storeKey, productId, sessionToken, input, token);
    public Task<StorefrontAvailabilityRequestDto?> GetAvailabilityAsync(string storeKey, Guid productId, string sessionToken, CancellationToken token) => requests.GetAvailabilityAsync(storeKey, productId, sessionToken, token);
    public Task<bool> CancelAvailabilityAsync(string storeKey, Guid productId, string sessionToken, CancellationToken token) => requests.CancelAvailabilityAsync(storeKey, productId, sessionToken, token);
    public Task<CustomerDemandMissingDto> RequestMissingProductAsync(string storeKey, string? sessionToken, StorefrontMissingProductRequestInput input, CancellationToken token) => requests.RequestMissingProductAsync(storeKey, sessionToken, input, token);

    public async Task<CustomerDemandSummaryDto> SummaryAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""SELECT COUNT(1),COUNT(DISTINCT NormalizedKeyword),SUM(CASE WHEN Outcome=N'RESULTS_AVAILABLE' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'RESULTS_OUT_OF_STOCK' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'NO_RESULTS' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'MIXED_RESULTS' THEN 1 ELSE 0 END) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to;SELECT TOP(50)NormalizedKeyword,COUNT(1),MAX(Outcome),COUNT(DISTINCT CustomerId),MAX(CreatedAtUtc) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to GROUP BY NormalizedKeyword ORDER BY COUNT(1) DESC;SELECT TOP(100)MIN(r.AvailabilityRequestId),r.ProductId,p.ProductName,p.ProductCode,ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=r.TenantId AND w.IsActive=1 AND w.IsDeleted=0 WHERE b.TenantId=r.TenantId AND b.ProductId=r.ProductId),0),COUNT(DISTINCT COALESCE(CONVERT(nvarchar(36),r.CustomerId),r.ContactMobile)),SUM(CASE WHEN r.RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING') THEN 1 ELSE 0 END),SUM(CASE WHEN r.RequestStatus=N'CANCELLED' THEN 1 ELSE 0 END),MAX(r.RequestedAtUtc) FROM commerce.ProductAvailabilityRequests r JOIN master.Products p ON p.ProductId=r.ProductId AND p.TenantId=r.TenantId WHERE r.TenantId=@tenant AND r.RequestedAtUtc>=@from AND r.RequestedAtUtc<@to GROUP BY r.ProductId,p.ProductName,p.ProductCode ORDER BY MAX(r.RequestedAtUtc) DESC;SELECT TOP(100)MissingProductRequestId,NormalizedKeyword,COUNT(1),COUNT(DISTINCT COALESCE(CONVERT(nvarchar(36),CustomerId),ContactMobile)),MAX(RequestStatus),MAX(CreatedAtUtc) FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to GROUP BY MissingProductRequestId,NormalizedKeyword ORDER BY MAX(CreatedAtUtc) DESC;SELECT CONVERT(date,CreatedAtUtc),COUNT(1),SUM(CASE WHEN Outcome=N'RESULTS_AVAILABLE' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'RESULTS_OUT_OF_STOCK' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'NO_RESULTS' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'MIXED_RESULTS' THEN 1 ELSE 0 END) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to GROUP BY CONVERT(date,CreatedAtUtc) ORDER BY CONVERT(date,CreatedAtUtc);""", c);
        q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@from", from.UtcDateTime); q.Parameters.AddWithValue("@to", until.UtcDateTime);
        await using var r = await q.ExecuteReaderAsync(token); await r.ReadAsync(token);
        var keywords = new List<CustomerDemandKeywordDto>(); var requests = new List<CustomerDemandRequestDto>(); var missing = new List<CustomerDemandMissingDto>(); var trends = new List<CustomerDemandTrendDto>();
        var summary = new CustomerDemandSummaryDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt32(2), r.IsDBNull(3) ? 0 : r.GetInt32(3), r.IsDBNull(4) ? 0 : r.GetInt32(4), r.IsDBNull(5) ? 0 : r.GetInt32(5), keywords, requests, missing, trends);
        await r.NextResultAsync(token); while (await r.ReadAsync(token)) keywords.Add(new(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetDateTime(4)));
        await r.NextResultAsync(token); while (await r.ReadAsync(token)) requests.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4), r.GetInt32(5), r.GetInt32(6) > 0 ? "ACTIVE" : "CANCELLED", r.GetDateTime(8), r.GetInt32(6), r.GetInt32(7)));
        await r.NextResultAsync(token); while (await r.ReadAsync(token)) missing.Add(new(r.GetGuid(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetDateTime(5)));
        await r.NextResultAsync(token); while (await r.ReadAsync(token)) trends.Add(new(r.GetDateTime(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
        return summary;
    }
}
