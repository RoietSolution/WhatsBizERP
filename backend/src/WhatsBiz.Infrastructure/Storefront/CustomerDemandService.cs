using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class CustomerDemandService(IConfiguration configuration, IStorefrontCustomerService customers) : ICustomerDemandService
{
    private static readonly Regex MobilePattern = new("^[0-9+() -]{7,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<CustomerDemandSummaryDto> RecordSearchAsync(string storeKey, string? sessionToken, StorefrontSearchEventInput input, CancellationToken token)
    {
        var keyword = input.Keyword?.Trim() ?? string.Empty;
        if (keyword.Length is < 2 or > 200) throw new BusinessRuleException("Enter a search term between 2 and 200 characters.");
        var normalized = Normalize(keyword);
        var tenant = await TenantAsync(storeKey, token) ?? throw new EntityNotFoundException("Store was not found.");
        var customer = string.IsNullOrWhiteSpace(sessionToken) ? null : await customers.GetSessionAsync(storeKey, sessionToken, token);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
WITH Visible AS(
 SELECT p.ProductId,CASE WHEN ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0 WHERE b.TenantId=@tenant AND b.ProductId=p.ProductId),0)>0 THEN 1 ELSE 0 END InStock
 FROM master.Products p WHERE p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1 AND (p.ProductName LIKE N'%'+@keyword+N'%' OR p.ProductCode LIKE N'%'+@keyword+N'%'))
SELECT COUNT(1),COALESCE(SUM(InStock),0),COALESCE(SUM(CASE WHEN InStock=0 THEN 1 ELSE 0 END),0) FROM Visible;
""", connection);
        command.Parameters.AddWithValue("@tenant", tenant.Value); command.Parameters.AddWithValue("@keyword", keyword);
        int resultCount, inStock, outOfStock; await using (var reader = await command.ExecuteReaderAsync(token)) { await reader.ReadAsync(token); resultCount = reader.GetInt32(0); inStock = reader.GetInt32(1); outOfStock = reader.GetInt32(2); }
        var outcome = resultCount == 0 ? "NO_RESULTS" : inStock > 0 && outOfStock > 0 ? "MIXED_RESULTS" : inStock > 0 ? "RESULTS_AVAILABLE" : "RESULTS_OUT_OF_STOCK";
        await using var insert = new SqlCommand("INSERT commerce.ProductSearchEvents(TenantId,CustomerId,AnonymousSessionId,OriginalKeyword,NormalizedKeyword,ResultCount,InStockResultCount,OutOfStockResultCount,Outcome) VALUES(@tenant,@customer,@anonymous,@original,@normalized,@results,@instock,@out,@outcome);", connection);
        insert.Parameters.AddWithValue("@tenant", tenant.Value); insert.Parameters.AddWithValue("@customer", customer?.Id ?? (object)DBNull.Value); insert.Parameters.AddWithValue("@anonymous", (object?)NormalizeSession(input.AnonymousSessionId) ?? DBNull.Value); insert.Parameters.AddWithValue("@original", keyword); insert.Parameters.AddWithValue("@normalized", normalized); insert.Parameters.AddWithValue("@results", resultCount); insert.Parameters.AddWithValue("@instock", inStock); insert.Parameters.AddWithValue("@out", outOfStock); insert.Parameters.AddWithValue("@outcome", outcome); await insert.ExecuteNonQueryAsync(token);
        return new(resultCount, 1, outcome == "RESULTS_AVAILABLE" ? 1 : 0, outcome == "RESULTS_OUT_OF_STOCK" ? 1 : 0, outcome == "NO_RESULTS" ? 1 : 0, outcome == "MIXED_RESULTS" ? 1 : 0, [], [], []);
    }

    public async Task<StorefrontAvailabilityRequestDto> RequestAvailabilityAsync(string storeKey, Guid productId, string? sessionToken, StorefrontAvailabilityRequestInput input, CancellationToken token)
    {
        var tenant = await TenantAsync(storeKey, token) ?? throw new EntityNotFoundException("Store was not found.");
        var customer = string.IsNullOrWhiteSpace(sessionToken) ? null : await customers.GetSessionAsync(storeKey, sessionToken, token);
        var mobile = customer?.Mobile ?? input.Mobile?.Trim(); if (customer is null && (string.IsNullOrWhiteSpace(mobile) || !MobilePattern.IsMatch(mobile))) throw new BusinessRuleException("Enter a valid mobile number.");
        var channel = (input.PreferredChannel ?? "WHATSAPP").Trim().ToUpperInvariant(); if (channel is not ("WHATSAPP" or "SMS" or "EMAIL")) throw new BusinessRuleException("Choose a supported notification channel.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
IF NOT EXISTS(SELECT 1 FROM master.Products WHERE TenantId=@tenant AND ProductId=@product AND IsActive=1 AND IsDeleted=0 AND IsWhatsAppVisible=1) THROW 52001,'The product is not available for notification.',1;
IF EXISTS(SELECT 1 FROM master.Products p WHERE p.TenantId=@tenant AND p.ProductId=@product AND ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0 WHERE b.TenantId=@tenant AND b.ProductId=@product),0)>0) THROW 52002,'This product is already available.',1;
DECLARE @id uniqueidentifier=NEWID();
INSERT commerce.ProductAvailabilityRequests(AvailabilityRequestId,TenantId,ProductId,CustomerId,ContactMobile,PreferredChannel,ConsentGiven) VALUES(@id,@tenant,@product,@customer,@mobile,@channel,@consent);
SELECT @id,RequestStatus,RequestedAtUtc FROM commerce.ProductAvailabilityRequests WHERE AvailabilityRequestId=@id;
""", connection);
        command.Parameters.AddWithValue("@tenant", tenant.Value); command.Parameters.AddWithValue("@product", productId); command.Parameters.AddWithValue("@customer", customer?.Id ?? (object)DBNull.Value); command.Parameters.AddWithValue("@mobile", (object?)mobile ?? DBNull.Value); command.Parameters.AddWithValue("@channel", channel); command.Parameters.AddWithValue("@consent", input.ConsentGiven); try { await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) throw new BusinessRuleException("Notification request could not be created."); return new(reader.GetGuid(0), productId, reader.GetString(1), reader.GetDateTime(2)); } catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new BusinessRuleException("Notification Requested"); }
    }

    public async Task<bool> CancelAvailabilityAsync(string storeKey, Guid productId, string sessionToken, CancellationToken token)
    {
        var tenant = await TenantAsync(storeKey, token) ?? throw new EntityNotFoundException("Store was not found."); var customer = await customers.GetSessionAsync(storeKey, sessionToken, token); if (customer is null) throw new UnauthorizedAccessException("A customer session is required.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); await using var command = new SqlCommand("UPDATE commerce.ProductAvailabilityRequests SET RequestStatus=N'CANCELLED',CancelledAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND ProductId=@product AND CustomerId=@customer AND RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING');", connection); command.Parameters.AddWithValue("@tenant",tenant.Value);command.Parameters.AddWithValue("@product",productId);command.Parameters.AddWithValue("@customer",customer.Id); return await command.ExecuteNonQueryAsync(token)>0;
    }

    public async Task<CustomerDemandMissingDto> RequestMissingProductAsync(string storeKey, string? sessionToken, StorefrontMissingProductRequestInput input, CancellationToken token)
    {
        var keyword=input.Keyword?.Trim()??string.Empty;if(keyword.Length is < 2 or > 200)throw new BusinessRuleException("Enter a product name between 2 and 200 characters.");var tenant=await TenantAsync(storeKey,token)??throw new EntityNotFoundException("Store was not found.");var customer=string.IsNullOrWhiteSpace(sessionToken)?null:await customers.GetSessionAsync(storeKey,sessionToken,token);var mobile=customer?.Mobile??input.Mobile?.Trim();if(customer is null&&!string.IsNullOrWhiteSpace(mobile)&&!MobilePattern.IsMatch(mobile))throw new BusinessRuleException("Enter a valid mobile number.");await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("DECLARE @id uniqueidentifier=NEWID();INSERT commerce.MissingProductRequests(MissingProductRequestId,TenantId,SearchEventId,CustomerId,AnonymousSessionId,RequestedKeyword,NormalizedKeyword,ContactMobile,Remarks)VALUES(@id,@tenant,@event,@customer,@anonymous,@keyword,@normalized,@mobile,@remarks);SELECT @id,RequestStatus,CreatedAtUtc;",c);q.Parameters.AddWithValue("@tenant",tenant.Value);q.Parameters.AddWithValue("@event",(object?)input.SearchEventId??DBNull.Value);q.Parameters.AddWithValue("@customer",customer?.Id??(object)DBNull.Value);q.Parameters.AddWithValue("@anonymous",(object?)NormalizeSession(input.AnonymousSessionId)??DBNull.Value);q.Parameters.AddWithValue("@keyword",keyword);q.Parameters.AddWithValue("@normalized",Normalize(keyword));q.Parameters.AddWithValue("@mobile",(object?)mobile??DBNull.Value);q.Parameters.AddWithValue("@remarks",(object?)input.Remarks?.Trim()[..Math.Min(input.Remarks.Trim().Length,500)]??DBNull.Value);try{await using var r=await q.ExecuteReaderAsync(token);await r.ReadAsync(token);return new(r.GetGuid(0),keyword,1,customer is null?0:1,r.GetString(1),r.GetDateTime(2));}catch(SqlException ex)when(ex.Number is 2601 or 2627){throw new BusinessRuleException("This product request is already active.");}
    }

    public async Task<CustomerDemandSummaryDto> SummaryAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("""SELECT COUNT(1),COUNT(DISTINCT NormalizedKeyword),SUM(CASE WHEN Outcome=N'RESULTS_AVAILABLE' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'RESULTS_OUT_OF_STOCK' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'NO_RESULTS' THEN 1 ELSE 0 END),SUM(CASE WHEN Outcome=N'MIXED_RESULTS' THEN 1 ELSE 0 END) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to;SELECT TOP(50)NormalizedKeyword,COUNT(1),MAX(Outcome),COUNT(DISTINCT CustomerId),MAX(CreatedAtUtc) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to GROUP BY NormalizedKeyword ORDER BY COUNT(1) DESC;SELECT r.AvailabilityRequestId,r.ProductId,p.ProductName,p.ProductCode,ISNULL((SELECT SUM(b.QuantityOnHand-b.QuantityReserved) FROM inventory.InventoryBalances b WHERE b.TenantId=@tenant AND b.ProductId=r.ProductId),0),COUNT(DISTINCT r.ContactIdentity),r.RequestStatus,r.RequestedAtUtc FROM commerce.ProductAvailabilityRequests r JOIN master.Products p ON p.TenantId=r.TenantId AND p.ProductId=r.ProductId WHERE r.TenantId=@tenant AND r.RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING') GROUP BY r.AvailabilityRequestId,r.ProductId,p.ProductName,p.ProductCode,r.RequestStatus,r.RequestedAtUtc ORDER BY r.RequestedAtUtc DESC;SELECT MissingProductRequestId,NormalizedKeyword,1,0,RequestStatus,CreatedAtUtc FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND CreatedAtUtc>=@from AND CreatedAtUtc<@to ORDER BY CreatedAtUtc DESC;""",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@from",from.UtcDateTime);q.Parameters.AddWithValue("@to",to.UtcDateTime);await using var r=await q.ExecuteReaderAsync(token);await r.ReadAsync(token);var summary=new(r.GetInt32(0),r.GetInt32(1),r.IsDBNull(2)?0:r.GetInt32(2),r.IsDBNull(3)?0:r.GetInt32(3),r.IsDBNull(4)?0:r.GetInt32(4),r.IsDBNull(5)?0:r.GetInt32(5),new List<CustomerDemandKeywordDto>(),new List<CustomerDemandRequestDto>(),new List<CustomerDemandMissingDto>());var keys=(List<CustomerDemandKeywordDto>)summary.Keywords;await r.NextResultAsync(token);while(await r.ReadAsync(token))keys.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetInt32(3),r.GetDateTime(4)));var requests=(List<CustomerDemandRequestDto>)summary.AvailabilityRequests;await r.NextResultAsync(token);while(await r.ReadAsync(token))requests.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetDecimal(4),r.GetInt32(5),r.GetString(6),r.GetDateTime(7)));var missing=(List<CustomerDemandMissingDto>)summary.MissingRequests;await r.NextResultAsync(token);while(await r.ReadAsync(token))missing.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),r.GetDateTime(5)));return summary;
    }
    private async Task<Guid?> TenantAsync(string key,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;",c);q.Parameters.AddWithValue("@key",key.Trim().ToUpperInvariant());return await q.ExecuteScalarAsync(token) is Guid id?id:null;}
    private static string Normalize(string value)=>string.Join(' ',value.Trim().ToUpperInvariant().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    private static string? NormalizeSession(string? value)=>string.IsNullOrWhiteSpace(value)||value.Length>64?null:value.Trim();
}
