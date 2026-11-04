using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class CustomerDemandService(IConfiguration configuration, IStorefrontCustomerService customers, IFeatureService features, GuestVisitorAnalyticsRetentionHealth retentionHealth) : ICustomerDemandService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
    private static string Norm(string value) => SearchTermPrivacy.Normalize(value);
    private static bool ValidMobile(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 7 and <= 30 && value.Trim().All(c => char.IsDigit(c) || "+()- ".Contains(c));
    private async Task<Guid?> Tenant(string key, CancellationToken token) { await using var c=new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1",c); q.Parameters.AddWithValue("@key",key.Trim().ToUpperInvariant()); return await q.ExecuteScalarAsync(token) is Guid id?id:null; }
    public async Task<CustomerDemandSummaryDto> RecordSearchAsync(string storeKey,string? sessionToken,StorefrontSearchEventInput input,CancellationToken token)
    {
        var keyword=input.Keyword?.Trim()??"";
        if(keyword.Length is <2 or >200)throw new BusinessRuleException("Enter a search term between 2 and 200 characters.");
        var tenant=await Tenant(storeKey,token)??throw new EntityNotFoundException("Store was not found.");
        // Customer/session identifiers and the unfiltered query are deliberately not persisted.
        var analyticsTerm=SearchTermPrivacy.ForStorage(keyword);
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        await using var q=new SqlCommand("""WITH V AS(SELECT CASE WHEN ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0 WHERE b.TenantId=@tenant AND b.ProductId=p.ProductId),0)>0 THEN 1 ELSE 0 END InStock FROM master.Products p WHERE p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1 AND (p.ProductName LIKE N'%'+@keyword+N'%' OR p.ProductCode LIKE N'%'+@keyword+N'%')) SELECT COUNT(1),COALESCE(SUM(InStock),0),COALESCE(SUM(CASE WHEN InStock=0 THEN 1 ELSE 0 END),0) FROM V;""",c);
        q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@keyword",keyword);
        int total,available,outStock;await using(var r=await q.ExecuteReaderAsync(token)){await r.ReadAsync(token);total=r.GetInt32(0);available=r.GetInt32(1);outStock=r.GetInt32(2);}
        var outcome=total==0?"NO_RESULTS":available>0&&outStock>0?"MIXED_RESULTS":available>0?"RESULTS_AVAILABLE":"RESULTS_OUT_OF_STOCK";
        var sessionId=Guid.TryParse(input.AnonymousSessionId,out var parsed)?parsed:(Guid?)null;
        var consented=sessionId.HasValue&&configuration.GetValue<bool>("GuestAnalytics:CollectionEnabled")&&configuration.GetValue<bool>("GuestAnalytics:RetentionCleanupEnabled")&&retentionHealth.IsHealthy&&await features.IsEnabledAsync(tenant,FeatureKeys.GuestVisitorAnalytics,token);
        if(consented){await using var consent=new SqlCommand("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@session AND NoticeVersion=N'guest-analytics-v1') THEN 1 ELSE 0 END)",c);consent.Parameters.AddWithValue("@tenant",tenant);consent.Parameters.AddWithValue("@session",sessionId!.Value);consented=await consent.ExecuteScalarAsync(token) is true;}
        if(consented){await using var insert=new SqlCommand("INSERT commerce.ProductSearchEvents(SearchEventId,TenantId,CustomerId,AnonymousSessionId,OriginalKeyword,NormalizedKeyword,ResultCount,InStockResultCount,OutOfStockResultCount,Outcome,GuestSessionId) VALUES(@id,@tenant,NULL,NULL,@term,@term,@total,@available,@out,@outcome,@session)",c);insert.Parameters.AddWithValue("@id",Guid.NewGuid());insert.Parameters.AddWithValue("@tenant",tenant);insert.Parameters.AddWithValue("@session",sessionId!.Value);insert.Parameters.AddWithValue("@term",analyticsTerm);insert.Parameters.AddWithValue("@total",total);insert.Parameters.AddWithValue("@available",available);insert.Parameters.AddWithValue("@out",outStock);insert.Parameters.AddWithValue("@outcome",outcome);await insert.ExecuteNonQueryAsync(token);}
        return new(total,1,outcome=="RESULTS_AVAILABLE"?1:0,outcome=="RESULTS_OUT_OF_STOCK"?1:0,outcome=="NO_RESULTS"?1:0,outcome=="MIXED_RESULTS"?1:0,[],[],[]);
    }
    public async Task<StorefrontAvailabilityRequestDto> RequestAvailabilityAsync(string storeKey,Guid productId,string? sessionToken,StorefrontAvailabilityRequestInput input,CancellationToken token)
    {
        var tenant=await Tenant(storeKey,token)??throw new EntityNotFoundException("Store was not found.");
        var customer=string.IsNullOrWhiteSpace(sessionToken)?null:await customers.GetSessionAsync(storeKey,sessionToken,token);
        var mobile=customer?.Mobile??input.Mobile?.Trim();
        if(customer is null&&!ValidMobile(mobile))throw new BusinessRuleException("Enter a valid mobile number.");
        var requestId=Guid.NewGuid();
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        await using var q=new SqlCommand("""
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM master.Products WHERE TenantId=@tenant AND ProductId=@product AND IsActive=1 AND IsDeleted=0 AND IsWhatsAppVisible=1) THROW 52001,'The product is not available for notification.',1;
IF EXISTS(SELECT 1 FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0 WHERE b.TenantId=@tenant AND b.ProductId=@product AND b.QuantityOnHand-b.QuantityReserved>0) THROW 52002,'This product is already available.',1;
IF EXISTS(SELECT 1 FROM commerce.ProductAvailabilityRequests WHERE TenantId=@tenant AND ProductId=@product AND RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING') AND ((@customer IS NOT NULL AND CustomerId=@customer) OR (@customer IS NULL AND CustomerId IS NULL AND ContactMobile=@mobile)))
BEGIN
    SELECT TOP(1) AvailabilityRequestId,RequestStatus,RequestedAtUtc FROM commerce.ProductAvailabilityRequests WHERE TenantId=@tenant AND ProductId=@product AND RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING') AND ((@customer IS NOT NULL AND CustomerId=@customer) OR (@customer IS NULL AND CustomerId IS NULL AND ContactMobile=@mobile)) ORDER BY RequestedAtUtc DESC;
    RETURN;
END;
INSERT commerce.ProductAvailabilityRequests(AvailabilityRequestId,TenantId,ProductId,CustomerId,ContactMobile,PreferredChannel,ConsentGiven) VALUES(@id,@tenant,@product,@customer,@mobile,N'WHATSAPP',@consent);
SELECT @id,RequestStatus,RequestedAtUtc FROM commerce.ProductAvailabilityRequests WHERE AvailabilityRequestId=@id;
""",c);
        q.Parameters.AddWithValue("@id",requestId);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@product",productId);q.Parameters.AddWithValue("@customer",customer?.Id??(object)DBNull.Value);q.Parameters.AddWithValue("@mobile",(object?)mobile??DBNull.Value);q.Parameters.AddWithValue("@consent",input.ConsentGiven);
        try{await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new BusinessRuleException("Notification request could not be created.");return new(r.GetGuid(0),productId,r.GetString(1),r.GetDateTime(2));}
        catch(SqlException ex)when(ex.Number is 2601 or 2627){throw new BusinessRuleException("Notification Requested");}
        catch(SqlException ex)when(ex.Number is 207 or 208){throw new BusinessRuleException("Notification requests are not available because the demand database migration is missing. Ask the retailer to contact support.");}
    }
    public async Task<StorefrontAvailabilityRequestDto?> GetAvailabilityAsync(string storeKey,Guid productId,string sessionToken,CancellationToken token)
    {
        var tenant=await Tenant(storeKey,token);
        var customer=tenant is null?null:await customers.GetSessionAsync(storeKey,sessionToken,token);
        if(tenant is null||customer is null)return null;
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        await using var q=new SqlCommand("SELECT TOP(1) AvailabilityRequestId,ProductId,RequestStatus,RequestedAtUtc FROM commerce.ProductAvailabilityRequests WHERE TenantId=@tenant AND ProductId=@product AND CustomerId=@customer AND RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING') ORDER BY RequestedAtUtc DESC",c);
        q.Parameters.AddWithValue("@tenant",tenant.Value);q.Parameters.AddWithValue("@product",productId);q.Parameters.AddWithValue("@customer",customer.Id);
        await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetDateTime(3)):null;
    }
    public async Task<bool> CancelAvailabilityAsync(string storeKey,Guid productId,string sessionToken,CancellationToken token){var tenant=await Tenant(storeKey,token)??throw new EntityNotFoundException("Store was not found.");var customer=await customers.GetSessionAsync(storeKey,sessionToken,token)??throw new System.UnauthorizedAccessException("A customer session is required.");await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("UPDATE commerce.ProductAvailabilityRequests SET RequestStatus=N'CANCELLED',CancelledAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND ProductId=@product AND CustomerId=@customer AND RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING')",c);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@product",productId);q.Parameters.AddWithValue("@customer",customer.Id);return await q.ExecuteNonQueryAsync(token)>0;}
    public async Task<CustomerDemandMissingDto> RequestMissingProductAsync(string storeKey,string? sessionToken,StorefrontMissingProductRequestInput input,CancellationToken token){var keyword=input.Keyword?.Trim()??"";if(keyword.Length is <2 or >200)throw new BusinessRuleException("Enter a product name between 2 and 200 characters.");var tenant=await Tenant(storeKey,token)??throw new EntityNotFoundException("Store was not found.");var customer=string.IsNullOrWhiteSpace(sessionToken)?null:await customers.GetSessionAsync(storeKey,sessionToken,token);if(customer is null&&!string.IsNullOrWhiteSpace(input.Mobile)&&!ValidMobile(input.Mobile))throw new BusinessRuleException("Enter a valid mobile number.");await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("INSERT commerce.MissingProductRequests(MissingProductRequestId,TenantId,SearchEventId,CustomerId,AnonymousSessionId,RequestedKeyword,NormalizedKeyword,ContactMobile,Remarks)VALUES(NEWSEQUENTIALID(),@tenant,@event,@customer,@anonymous,@keyword,@normalized,@mobile,@remarks);SELECT TOP(1)MissingProductRequestId,RequestStatus,CreatedAtUtc FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND NormalizedKeyword=@normalized ORDER BY CreatedAtUtc DESC",c);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@event",(object?)input.SearchEventId??DBNull.Value);q.Parameters.AddWithValue("@customer",customer?.Id??(object)DBNull.Value);q.Parameters.AddWithValue("@anonymous",(object?)input.AnonymousSessionId??DBNull.Value);q.Parameters.AddWithValue("@keyword",keyword);q.Parameters.AddWithValue("@normalized",Norm(keyword));q.Parameters.AddWithValue("@mobile",(object?)input.Mobile??DBNull.Value);q.Parameters.AddWithValue("@remarks",(object?)input.Remarks??DBNull.Value);try{await using var r=await q.ExecuteReaderAsync(token);await r.ReadAsync(token);return new(r.GetGuid(0),keyword,1,customer is null?0:1,r.GetString(1),r.GetDateTime(2));}catch(SqlException ex)when(ex.Number is 2601 or 2627){throw new BusinessRuleException("This product request is already active.");}}
    public async Task<CustomerDemandSummaryDto> SummaryAsync(Guid tenantId,DateTimeOffset from,DateTimeOffset until,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT COUNT(1),COUNT(DISTINCT NormalizedKeyword),SUM(CASE WHEN Outcome=N'RESULTS_AVAILABLE' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'RESULTS_OUT_OF_STOCK' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'NO_RESULTS' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'MIXED_RESULTS' THEN 1 ELSE 0 END) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to;SELECT TOP(50)NormalizedKeyword,COUNT(1),MAX(Outcome),COUNT(DISTINCT CustomerId),MAX(CreatedAtUtc) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to GROUP BY NormalizedKeyword ORDER BY COUNT(1) DESC;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@from",from.UtcDateTime);q.Parameters.AddWithValue("@to",until.UtcDateTime);await using var r=await q.ExecuteReaderAsync(token);await r.ReadAsync(token);var keys=new List<CustomerDemandKeywordDto>();var summary=new CustomerDemandSummaryDto(r.GetInt32(0),r.GetInt32(1),r.IsDBNull(2)?0:r.GetInt32(2),r.IsDBNull(3)?0:r.GetInt32(3),r.IsDBNull(4)?0:r.GetInt32(4),r.IsDBNull(5)?0:r.GetInt32(5),keys,[],[]);await r.NextResultAsync(token);while(await r.ReadAsync(token))keys.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetInt32(3),r.GetDateTime(4)));return summary;}
}
