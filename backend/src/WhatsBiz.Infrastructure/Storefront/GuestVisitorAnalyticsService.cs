using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class GuestVisitorAnalyticsService(
    IConfiguration configuration, IFeatureService features, ICurrentUserService currentUser,
    IStorefrontService storefront, GuestVisitorAnalyticsRetentionHealth retentionHealth) : IGuestVisitorAnalyticsService
{
    public const string NoticeVersion = "guest-analytics-v1";
    private const string FeatureKey = FeatureKeys.GuestVisitorAnalytics;
    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    { "STORE_VISIT", "PAGE_VIEW", "PRODUCT_VIEW", "CATEGORY_VIEW", "SEARCH", "ADD_TO_CART", "REMOVE_FROM_CART", "CHECKOUT_STARTED", "FOREGROUND_ENGAGEMENT" };
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
    private bool CollectionEnabled => configuration.GetValue<bool>("GuestAnalytics:CollectionEnabled");
    private bool RetentionCleanupEnabled => configuration.GetValue<bool>("GuestAnalytics:RetentionCleanupEnabled");

    public async Task<GuestAnalyticsStatus> StatusAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (!CollectionEnabled || !RetentionCleanupEnabled || !retentionHealth.IsHealthy || tenant is null || !await features.IsEnabledAsync(tenant.Value, FeatureKey, token)) return new(false, NoticeVersion);
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("SELECT CONVERT(bit,CASE WHEN RetentionDays BETWEEN 1 AND 365 AND PolicyReviewedAtUtc IS NOT NULL THEN 1 ELSE 0 END) FROM commerce.GuestVisitorAnalyticsPolicies WHERE TenantId=@tenant", c);
        q.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenant.Value;
        return new(await q.ExecuteScalarAsync(token) is true, NoticeVersion);
    }

    public async Task SetConsentAsync(string storeKey, GuestAnalyticsConsentInput input, string? userAgent, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token) ?? throw new KeyNotFoundException("Storefront is unavailable.");
        if (!input.Granted) { await WithdrawAsync(storeKey, input.VisitorId, input.SessionId, token); return; }
        if (input.VisitorId == Guid.Empty || input.SessionId == Guid.Empty || input.NoticeVersion != NoticeVersion || !ValidViewport(input.ViewportCategory))
            throw new ArgumentException("The analytics consent details are invalid.");
        if (!(await StatusAsync(storeKey, token)).Available) throw new InvalidOperationException("Guest analytics is not enabled for this store.");
        var source = ValidSource(input.TrafficSource) ? input.TrafficSource!.ToUpperInvariant() : "OTHER";
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""
IF EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@session AND VisitorId=@visitor)
    UPDATE commerce.GuestVisitorAnalyticsSessions SET NoticeVersion=@notice,ConsentedAtUtc=SYSUTCDATETIME(),LastActivityAtUtc=SYSUTCDATETIME(),ViewportCategory=@viewport,TrafficSource=@source,CampaignSource=@campaignSource,CampaignMedium=@campaignMedium,CampaignName=@campaignName WHERE TenantId=@tenant AND SessionId=@session AND VisitorId=@visitor;
ELSE IF EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@session)
    THROW 82101,N'Analytics session identifier conflict.',1;
ELSE
    INSERT commerce.GuestVisitorAnalyticsSessions(TenantId,SessionId,VisitorId,NoticeVersion,DeviceCategory,BrowserCategory,OperatingSystemCategory,ViewportCategory,TrafficSource,CampaignSource,CampaignMedium,CampaignName)
    VALUES(@tenant,@session,@visitor,@notice,@device,@browser,@os,@viewport,@source,@campaignSource,@campaignMedium,@campaignName);
""", c);
        AddConsentParameters(q, tenant, input, source);
        var (device,browser,os) = Classify(userAgent);
        q.Parameters.Add("@device",SqlDbType.VarChar,12).Value=device; q.Parameters.Add("@browser",SqlDbType.VarChar,20).Value=browser; q.Parameters.Add("@os",SqlDbType.VarChar,20).Value=os;
        await q.ExecuteNonQueryAsync(token);
    }

    public async Task RecordAsync(string storeKey, GuestAnalyticsBatchInput input, CancellationToken token)
    {
        if (input.Events is null || input.Events.Count is < 1 or > 50 || input.SessionId == Guid.Empty) throw new ArgumentException("Submit between 1 and 50 valid events.");
        var tenant = await ResolveTenantAsync(storeKey, token) ?? throw new KeyNotFoundException("Storefront is unavailable.");
        if (!CollectionEnabled || !RetentionCleanupEnabled || !retentionHealth.IsHealthy || !await features.IsEnabledAsync(tenant, FeatureKey, token) || !await PolicyReady(tenant, token)) return;
        foreach (var e in input.Events)
        {
            if (e.EventId == Guid.Empty || !EventTypes.Contains(e.EventType) || e.ResultCount is < 0 or > 1_000_000 || e.EngagedSeconds is < 0 or > 120)
                throw new ArgumentException("An analytics event is invalid.");
            if ((e.EventType == "PRODUCT_VIEW") && (!e.ProductId.HasValue || await storefront.GetProductAsync(storeKey, e.ProductId.Value, token) is null))
                throw new ArgumentException("The viewed product is not publicly available in this store.");
            if (e.EventType == "CATEGORY_VIEW" && (!e.CategoryId.HasValue || !(await storefront.GetCategoriesAsync(storeKey, token) ?? []).Any(x => x.Id == e.CategoryId)))
                throw new ArgumentException("The viewed category is not available in this store.");
            if (e.EventType == "SEARCH" && (string.IsNullOrWhiteSpace(e.SearchTerm) || e.SearchTerm.Length > 200)) throw new ArgumentException("A valid search term is required.");
            if (e.EventType != "FOREGROUND_ENGAGEMENT" && e.EngagedSeconds.GetValueOrDefault() != 0) throw new ArgumentException("Engagement seconds are only valid for engagement events.");
        }
        foreach (var e in input.Events)
        {
            var searchTerm = e.EventType == "SEARCH" ? SearchTermPrivacy.ForStorage(e.SearchTerm!) : null;
            await RecordGuestEventAsync(tenant, input.SessionId, e, searchTerm, token);
        }
    }

    private async Task RecordGuestEventAsync(Guid tenant, Guid session, GuestAnalyticsEventInput input,
        string? searchTerm, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await using var command = new SqlCommand("""
IF EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@session AND NoticeVersion=@notice)
AND NOT EXISTS(SELECT 1 FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND ClientEventId=@event)
BEGIN
    INSERT commerce.AnalyticsEvents(TenantId,EventType,CustomerId,ProductId,MetadataJson,GuestSessionId,ClientEventId,CategoryId,NormalizedSearchTerm,ResultCount)
    VALUES(@tenant,@type,NULL,@product,NULL,@session,@event,@category,@term,@result);
    UPDATE commerce.GuestVisitorAnalyticsSessions
    SET LastActivityAtUtc=SYSUTCDATETIME(),EngagedSeconds=EngagedSeconds+@engaged
    WHERE TenantId=@tenant AND SessionId=@session;
END;
""", connection, transaction);
            command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenant;
            command.Parameters.Add("@session", SqlDbType.UniqueIdentifier).Value = session;
            command.Parameters.Add("@notice", SqlDbType.NVarChar, 40).Value = NoticeVersion;
            command.Parameters.Add("@event", SqlDbType.UniqueIdentifier).Value = input.EventId;
            command.Parameters.Add("@type", SqlDbType.NVarChar, 60).Value = input.EventType;
            command.Parameters.Add("@product", SqlDbType.UniqueIdentifier).Value = (object?)input.ProductId ?? DBNull.Value;
            command.Parameters.Add("@category", SqlDbType.UniqueIdentifier).Value = (object?)input.CategoryId ?? DBNull.Value;
            command.Parameters.Add("@term", SqlDbType.NVarChar, 200).Value = (object?)searchTerm ?? DBNull.Value;
            command.Parameters.Add("@result", SqlDbType.Int).Value = (object?)input.ResultCount ?? DBNull.Value;
            command.Parameters.Add("@engaged", SqlDbType.Int).Value = input.EventType == "FOREGROUND_ENGAGEMENT" ? input.EngagedSeconds.GetValueOrDefault() : 0;
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    public async Task WithdrawAsync(string storeKey, Guid visitorId, Guid sessionId, CancellationToken token)
    {
        if (visitorId == Guid.Empty || sessionId == Guid.Empty) return;
        var tenant = await ResolveTenantAsync(storeKey, token); if (tenant is null) return;
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        using var transaction = c.BeginTransaction();
        try
        {
            const string ownership = "EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions currentSession WHERE currentSession.TenantId=@tenant AND currentSession.SessionId=@session AND currentSession.VisitorId=@visitor)";
            await using (var detach = new SqlCommand($"UPDATE requestRow SET SearchEventId=NULL FROM commerce.MissingProductRequests requestRow JOIN commerce.ProductSearchEvents searchEvent ON searchEvent.SearchEventId=requestRow.SearchEventId AND searchEvent.TenantId=requestRow.TenantId JOIN commerce.GuestVisitorAnalyticsSessions guestSession ON guestSession.TenantId=searchEvent.TenantId AND guestSession.SessionId=searchEvent.GuestSessionId WHERE requestRow.TenantId=@tenant AND guestSession.VisitorId=@visitor AND {ownership};", c, transaction))
            {
                AddWithdrawParameters(detach, tenant.Value, visitorId, sessionId);
                await detach.ExecuteNonQueryAsync(token);
            }
            await using (var remove = new SqlCommand($"DELETE FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND VisitorId=@visitor AND {ownership};", c, transaction))
            {
                AddWithdrawParameters(remove, tenant.Value, visitorId, sessionId);
                await remove.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(token); throw; }
    }

    public async Task<GuestAnalyticsSummary> RetailerReportAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        if (from >= until || until - from > TimeSpan.FromDays(93)) throw new ArgumentException("Choose a date range of up to 93 days.");
        return await ReportAsync([tenantId], from, until, token);
    }

    public async Task<GuestAnalyticsSummary> OwnerReportAsync(Guid? tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        EnsureOwner();
        if (from >= until || until - from > TimeSpan.FromDays(93)) throw new ArgumentException("Choose a date range of up to 93 days.");
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        var ids = new List<Guid>();
        await using (var q = new SqlCommand("SELECT TenantId FROM core.Tenants WHERE IsActive=1", c))
        await using (var r = await q.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) ids.Add(r.GetGuid(0));
        var enabled = new List<Guid>();
        foreach (var id in ids)
            if ((!tenantId.HasValue || tenantId == id) && await features.IsEnabledAsync(id, FeatureKey, token) && await PolicyReady(id, token)) enabled.Add(id);
        return await ReportAsync(enabled, from, until, token);
    }

    public async Task<IReadOnlyList<GuestAnalyticsTenantOption>> OwnerFiltersAsync(CancellationToken token)
    {
        EnsureOwner();
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        await using var q=new SqlCommand("SELECT TenantId,COALESCE(NULLIF(LTRIM(RTRIM(DisplayName)),N''),Name) FROM core.Tenants WHERE IsActive=1 ORDER BY 2",c);
        var rows=new List<GuestAnalyticsTenantOption>();await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetGuid(0),r.GetString(1)));return rows;
    }

    public async Task SetRetentionPolicyAsync(Guid tenantId, GuestAnalyticsRetentionInput input, CancellationToken token)
    {
        EnsureOwner();
        if (!input.PolicyReviewed || input.RetentionDays is < 1 or > 365) throw new ArgumentException("A reviewed retention period from 1 to 365 days is required.");
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""
MERGE commerce.GuestVisitorAnalyticsPolicies AS target
USING (SELECT @tenant TenantId) AS source ON target.TenantId=source.TenantId
WHEN MATCHED THEN UPDATE SET RetentionDays=@days,PolicyReviewedAtUtc=SYSUTCDATETIME(),PolicyReviewedBy=@actor,ModifiedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(TenantId,RetentionDays,PolicyReviewedAtUtc,PolicyReviewedBy) VALUES(@tenant,@days,SYSUTCDATETIME(),@actor);
""",c);
        q.Parameters.Add("@tenant",SqlDbType.UniqueIdentifier).Value=tenantId; q.Parameters.Add("@days",SqlDbType.Int).Value=input.RetentionDays;
        q.Parameters.Add("@actor",SqlDbType.NVarChar,256).Value=currentUser.Username??"application-owner";
        await q.ExecuteNonQueryAsync(token);
    }

    private async Task<GuestAnalyticsSummary> ReportAsync(IReadOnlyCollection<Guid> tenantIds, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        if (tenantIds.Count == 0) return Empty();
        var csv = string.Join(',', tenantIds);
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""
CREATE TABLE #scope(TenantId uniqueidentifier PRIMARY KEY);
INSERT #scope SELECT TRY_CONVERT(uniqueidentifier,value) FROM STRING_SPLIT(@ids,',') WHERE TRY_CONVERT(uniqueidentifier,value) IS NOT NULL;
SELECT COUNT_BIG(DISTINCT s.VisitorId),COUNT_BIG(1),COALESCE(SUM(CONVERT(bigint,s.EngagedSeconds)),0)
FROM commerce.GuestVisitorAnalyticsSessions s JOIN #scope x ON x.TenantId=s.TenantId
JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId AND p.RetentionDays IS NOT NULL AND p.PolicyReviewedAtUtc IS NOT NULL
WHERE s.StartedAtUtc>=@from AND s.StartedAtUtc<@to AND s.StartedAtUtc>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME());
SELECT e.EventType,COUNT_BIG(1) FROM commerce.AnalyticsEvents e JOIN #scope x ON x.TenantId=e.TenantId JOIN commerce.GuestVisitorAnalyticsSessions s ON s.TenantId=e.TenantId AND s.SessionId=e.GuestSessionId
JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=e.TenantId AND p.RetentionDays IS NOT NULL
WHERE e.CreatedOn>=@from AND e.CreatedOn<@to AND e.CreatedOn>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME())
GROUP BY e.EventType;
SELECT s.DeviceCategory,COUNT_BIG(1) FROM commerce.GuestVisitorAnalyticsSessions s JOIN #scope x ON x.TenantId=s.TenantId JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId AND p.RetentionDays IS NOT NULL WHERE s.StartedAtUtc>=@from AND s.StartedAtUtc<@to AND s.StartedAtUtc>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME()) GROUP BY s.DeviceCategory;
SELECT s.BrowserCategory,COUNT_BIG(1) FROM commerce.GuestVisitorAnalyticsSessions s JOIN #scope x ON x.TenantId=s.TenantId JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId AND p.RetentionDays IS NOT NULL WHERE s.StartedAtUtc>=@from AND s.StartedAtUtc<@to AND s.StartedAtUtc>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME()) GROUP BY s.BrowserCategory;
SELECT s.OperatingSystemCategory,COUNT_BIG(1) FROM commerce.GuestVisitorAnalyticsSessions s JOIN #scope x ON x.TenantId=s.TenantId JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId AND p.RetentionDays IS NOT NULL WHERE s.StartedAtUtc>=@from AND s.StartedAtUtc<@to AND s.StartedAtUtc>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME()) GROUP BY s.OperatingSystemCategory;
SELECT s.TrafficSource,COUNT_BIG(1) FROM commerce.GuestVisitorAnalyticsSessions s JOIN #scope x ON x.TenantId=s.TenantId JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId AND p.RetentionDays IS NOT NULL WHERE s.StartedAtUtc>=@from AND s.StartedAtUtc<@to AND s.StartedAtUtc>=DATEADD(day,-p.RetentionDays,SYSUTCDATETIME()) GROUP BY s.TrafficSource;
SELECT TOP(10) e.ProductId,p.ProductName,COUNT_BIG(1) FROM commerce.AnalyticsEvents e JOIN #scope x ON x.TenantId=e.TenantId JOIN master.Products p ON p.TenantId=e.TenantId AND p.ProductId=e.ProductId JOIN commerce.GuestVisitorAnalyticsSessions s ON s.TenantId=e.TenantId AND s.SessionId=e.GuestSessionId JOIN commerce.GuestVisitorAnalyticsPolicies gp ON gp.TenantId=e.TenantId AND gp.RetentionDays IS NOT NULL WHERE e.EventType=N'PRODUCT_VIEW' AND e.CreatedOn>=@from AND e.CreatedOn<@to AND e.CreatedOn>=DATEADD(day,-gp.RetentionDays,SYSUTCDATETIME()) GROUP BY e.ProductId,p.ProductName ORDER BY COUNT_BIG(1) DESC;
""",c);
        q.Parameters.Add("@ids",SqlDbType.NVarChar,-1).Value=csv; q.Parameters.Add("@from",SqlDbType.DateTime2).Value=from.UtcDateTime; q.Parameters.Add("@to",SqlDbType.DateTime2).Value=to.UtcDateTime;
        long visitors,sessions,engaged;var counts=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        var devices=new Dictionary<string,long>();var browsers=new Dictionary<string,long>();var systems=new Dictionary<string,long>();var sources=new Dictionary<string,long>();var products=new List<GuestAnalyticsProduct>();
        await using var r=await q.ExecuteReaderAsync(token); await r.ReadAsync(token); visitors=r.GetInt64(0);sessions=r.GetInt64(1);engaged=r.GetInt64(2);
        await r.NextResultAsync(token); while(await r.ReadAsync(token)) counts[r.GetString(0)]=r.GetInt64(1);
        await ReadMap(r,devices,token);await ReadMap(r,browsers,token);await ReadMap(r,systems,token);await ReadMap(r,sources,token);
        await r.NextResultAsync(token);while(await r.ReadAsync(token))products.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt64(2)));
        // Order conversion is deliberately unreported until a server-verified,
        // consent-bound checkout attribution hook is available.
        return new(visitors,sessions,engaged,Get("STORE_VISIT")+Get("PAGE_VIEW"),Get("PRODUCT_VIEW"),Get("CATEGORY_VIEW"),Get("SEARCH"),Get("ADD_TO_CART"),Get("CHECKOUT_STARTED"),null,devices,browsers,systems,sources,products);
        long Get(string key)=>counts.GetValueOrDefault(key);
    }

    private static async Task ReadMap(SqlDataReader r,Dictionary<string,long> map,CancellationToken token){await r.NextResultAsync(token);while(await r.ReadAsync(token))map[r.GetString(0)]=r.GetInt64(1);}
    private async Task<bool> PolicyReady(Guid tenant,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT CONVERT(bit,CASE WHEN RetentionDays BETWEEN 1 AND 365 AND PolicyReviewedAtUtc IS NOT NULL THEN 1 ELSE 0 END) FROM commerce.GuestVisitorAnalyticsPolicies WHERE TenantId=@tenant",c);q.Parameters.Add("@tenant",SqlDbType.UniqueIdentifier).Value=tenant;return await q.ExecuteScalarAsync(token) is true;}
    private async Task<Guid?> ResolveTenantAsync(string key,CancellationToken token){if(string.IsNullOrWhiteSpace(key)||key.Length>100)return null;await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1",c);q.Parameters.Add("@key",SqlDbType.NVarChar,100).Value=key.Trim().ToUpperInvariant();return await q.ExecuteScalarAsync(token) is Guid id?id:null;}
    private void EnsureOwner(){if(!currentUser.Roles.Contains("ApplicationOwner",StringComparer.Ordinal))throw new UnauthorizedAccessException("Application Owner access required.");}
    private static bool ValidViewport(string value)=>value is "SMALL" or "MEDIUM" or "LARGE" or "UNKNOWN";
    private static bool ValidSource(string? value)=>value is "DIRECT" or "SEARCH" or "SOCIAL" or "REFERRAL" or "CAMPAIGN" or "OTHER";
    private static string? SafeCampaign(string? value,int max){if(string.IsNullOrWhiteSpace(value))return null;var safe=SearchTermPrivacy.ForStorage(value);safe=Regex.Replace(safe,@"[^A-Z0-9 _.-]"," ");safe=Regex.Replace(safe,@"\s+"," ").Trim();return safe.Length==0?null:safe[..Math.Min(safe.Length,max)];}
    internal static (string Device,string Browser,string Os) Classify(string? ua){var u=ua??"";var android=u.Contains("Android",StringComparison.OrdinalIgnoreCase);var mobile=u.Contains("Mobile",StringComparison.OrdinalIgnoreCase);var device=u.Contains("iPad",StringComparison.OrdinalIgnoreCase)||android&&!mobile?"TABLET":mobile||android?"MOBILE":"DESKTOP";var browser=u.Contains("Edg/",StringComparison.OrdinalIgnoreCase)?"EDGE":u.Contains("Firefox/",StringComparison.OrdinalIgnoreCase)?"FIREFOX":u.Contains("Chrome/",StringComparison.OrdinalIgnoreCase)?"CHROME":u.Contains("Safari/",StringComparison.OrdinalIgnoreCase)?"SAFARI":"OTHER";var os=android?"ANDROID":u.Contains("iPhone",StringComparison.OrdinalIgnoreCase)||u.Contains("iPad",StringComparison.OrdinalIgnoreCase)?"IOS":u.Contains("Windows",StringComparison.OrdinalIgnoreCase)?"WINDOWS":u.Contains("Mac OS",StringComparison.OrdinalIgnoreCase)?"MACOS":u.Contains("Linux",StringComparison.OrdinalIgnoreCase)?"LINUX":"OTHER";return(device,browser,os);}
    private static void AddConsentParameters(SqlCommand q,Guid tenant,GuestAnalyticsConsentInput i,string source){q.Parameters.Add("@tenant",SqlDbType.UniqueIdentifier).Value=tenant;q.Parameters.Add("@session",SqlDbType.UniqueIdentifier).Value=i.SessionId;q.Parameters.Add("@visitor",SqlDbType.UniqueIdentifier).Value=i.VisitorId;q.Parameters.Add("@notice",SqlDbType.NVarChar,40).Value=i.NoticeVersion;q.Parameters.Add("@viewport",SqlDbType.VarChar,12).Value=i.ViewportCategory;q.Parameters.Add("@source",SqlDbType.VarChar,16).Value=source;q.Parameters.Add("@campaignSource",SqlDbType.NVarChar,100).Value=(object?)SafeCampaign(i.CampaignSource,100)??DBNull.Value;q.Parameters.Add("@campaignMedium",SqlDbType.NVarChar,100).Value=(object?)SafeCampaign(i.CampaignMedium,100)??DBNull.Value;q.Parameters.Add("@campaignName",SqlDbType.NVarChar,120).Value=(object?)SafeCampaign(i.CampaignName,120)??DBNull.Value;}
    private static void AddWithdrawParameters(SqlCommand q,Guid tenant,Guid visitor,Guid session){q.Parameters.Add("@tenant",SqlDbType.UniqueIdentifier).Value=tenant;q.Parameters.Add("@visitor",SqlDbType.UniqueIdentifier).Value=visitor;q.Parameters.Add("@session",SqlDbType.UniqueIdentifier).Value=session;}
    private static GuestAnalyticsSummary Empty()=>new(0,0,0,0,0,0,0,0,0,null,new Dictionary<string,long>(),new Dictionary<string,long>(),new Dictionary<string,long>(),new Dictionary<string,long>(),[]);
}
