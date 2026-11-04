using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed record SearchAnalyticsFilterOption(Guid Id, string Name);
public sealed record SearchAnalyticsKeyword(string Keyword, long Searches, long ZeroResultSearches, DateTime LastSearchedAtUtc);
public sealed record SearchAnalyticsTrend(DateTime DateUtc, long Searches, long ZeroResultSearches);
public sealed record OwnerSearchAnalyticsResult(long TotalSearches, long ZeroResultSearches, int UniqueKeywords,
    int PageNumber, int PageSize, int TotalKeywordCount, IReadOnlyList<SearchAnalyticsKeyword> Keywords,
    IReadOnlyList<SearchAnalyticsTrend> Trends);
public sealed record OwnerSearchAnalyticsFilters(IReadOnlyList<SearchAnalyticsFilterOption> Cities,
    IReadOnlyList<SearchAnalyticsFilterOption> Retailers);

public sealed class OwnerSearchAnalyticsService(IConfiguration configuration, ICurrentUserService currentUser)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<OwnerSearchAnalyticsFilters> FiltersAsync(CancellationToken token)
    {
        EnsureOwner();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT c.CityId, c.MarketplaceName
            FROM commerce.Cities c
            JOIN commerce.PublicRouteRegistry r ON r.CityId=c.CityId AND r.EntityType='CITY' AND r.RouteStatus='ACTIVE'
            WHERE c.IsActive=1 AND c.IsPublished=1
            ORDER BY c.MarketplaceName;
            SELECT DISTINCT t.TenantId, COALESCE(NULLIF(LTRIM(RTRIM(t.DisplayName)),N''),t.Name)
            FROM commerce.ProductSearchEvents e JOIN core.Tenants t ON t.TenantId=e.TenantId
            WHERE t.IsActive=1 ORDER BY 2;
            """, connection);
        var cities = new List<SearchAnalyticsFilterOption>();
        var retailers = new List<SearchAnalyticsFilterOption>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) cities.Add(new(reader.GetGuid(0), reader.GetString(1)));
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) retailers.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return new(cities, retailers);
    }

    public async Task<OwnerSearchAnalyticsResult> SearchAsync(DateTimeOffset from, DateTimeOffset to,
        Guid? cityId, Guid? retailerTenantId, int page, int pageSize, CancellationToken token)
    {
        EnsureOwner();
        if (from >= to || to - from > TimeSpan.FromDays(366)) throw new ArgumentException("Choose a date range of up to 366 days.");
        if (cityId.HasValue && retailerTenantId.HasValue) throw new ArgumentException("Choose either a city or a retailer filter.");
        page = Math.Clamp(page, 1, 100_000);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var offset = checked((page - 1) * pageSize);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            CREATE TABLE #SearchEvents
            (
                NormalizedKeyword nvarchar(200) NOT NULL,
                IsZeroResult bit NOT NULL,
                CreatedAtUtc datetime2(0) NOT NULL
            );
            INSERT #SearchEvents(NormalizedKeyword,IsZeroResult,CreatedAtUtc)
            SELECT e.NormalizedKeyword,CONVERT(bit,CASE WHEN e.Outcome=N'NO_RESULTS' THEN 1 ELSE 0 END),e.CreatedAtUtc
            FROM commerce.ProductSearchEvents e
            WHERE @city IS NULL AND e.CreatedAtUtc>=@from AND e.CreatedAtUtc<@to
              AND (@retailer IS NULL OR e.TenantId=@retailer);
            INSERT #SearchEvents(NormalizedKeyword,IsZeroResult,CreatedAtUtc)
            SELECT e.NormalizedKeyword,CONVERT(bit,CASE WHEN e.ResultCount=0 THEN 1 ELSE 0 END),e.CreatedAtUtc
            FROM commerce.CityMarketplaceSearchEvents e
            WHERE @retailer IS NULL AND e.CreatedAtUtc>=@from AND e.CreatedAtUtc<@to
              AND (@city IS NULL OR e.CityId=@city);

            SELECT COUNT_BIG(1),COALESCE(SUM(CONVERT(bigint,IsZeroResult)),0)
            FROM #SearchEvents;
            SELECT NormalizedKeyword,COUNT_BIG(1),SUM(CONVERT(bigint,IsZeroResult)),MAX(CreatedAtUtc)
            FROM #SearchEvents GROUP BY NormalizedKeyword
            ORDER BY COUNT_BIG(1) DESC,NormalizedKeyword;
            SELECT CONVERT(date,CreatedAtUtc),COUNT_BIG(1),SUM(CONVERT(bigint,IsZeroResult))
            FROM #SearchEvents GROUP BY CONVERT(date,CreatedAtUtc) ORDER BY CONVERT(date,CreatedAtUtc);
            """, connection);
        command.Parameters.Add(new SqlParameter("@city", SqlDbType.UniqueIdentifier) { Value = (object?)cityId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@retailer", SqlDbType.UniqueIdentifier) { Value = (object?)retailerTenantId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime2) { Value = from.UtcDateTime });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime2) { Value = to.UtcDateTime });
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var total = reader.GetInt64(0);
        var zero = reader.GetInt64(1);
        await reader.NextResultAsync(token);
        var grouped = new Dictionary<string, (long Searches, long Zero, DateTime Last)>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
        {
            var safeKeyword = SearchTermPrivacy.ForStorage(reader.GetString(0));
            if (safeKeyword.Length == 0) continue;
            var searches = reader.GetInt64(1);
            var zeroResults = reader.GetInt64(2);
            var last = reader.GetDateTime(3);
            if (grouped.TryGetValue(safeKeyword, out var existing))
                grouped[safeKeyword] = (existing.Searches + searches, existing.Zero + zeroResults, existing.Last > last ? existing.Last : last);
            else
                grouped.Add(safeKeyword, (searches, zeroResults, last));
        }
        var allKeywords = grouped.Select(x => new SearchAnalyticsKeyword(x.Key, x.Value.Searches, x.Value.Zero, x.Value.Last))
            .OrderByDescending(x => x.Searches).ThenBy(x => x.Keyword, StringComparer.Ordinal).ToArray();
        var totalKeywords = allKeywords.Length;
        var keywords = allKeywords.Skip(offset).Take(pageSize).ToArray();
        var unique = totalKeywords;
        await reader.NextResultAsync(token);
        var trends = new List<SearchAnalyticsTrend>();
        while (await reader.ReadAsync(token)) trends.Add(new(reader.GetDateTime(0), reader.GetInt64(1), reader.GetInt64(2)));
        return new(total, zero, unique, page, pageSize, totalKeywords, keywords, trends);
    }

    private void EnsureOwner()
    {
        if (!currentUser.Roles.Contains("ApplicationOwner", StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Application Owner access required.");
    }
}
