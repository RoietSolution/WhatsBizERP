using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class CityMarketplaceSearchAnalyticsTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));

    [Fact]
    public void CitySearchSchemaStoresOnlyCityContextAndPrivacyMinimizedAggregates()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/V78-CityMarketplaceSearchAnalytics.sql"));
        migration.Should().Contain("CityId uniqueidentifier NOT NULL")
            .And.Contain("SearchContext varchar(12) NOT NULL")
            .And.Contain("CHECK(SearchContext='CITY')")
            .And.Contain("NormalizedKeyword nvarchar(200) NOT NULL")
            .And.NotContain("CustomerId")
            .And.NotContain("AnonymousSessionId")
            .And.NotContain("OriginalKeyword");
        migration.Should().Contain("IX_ProductSearchEvents_OwnerDate").And.Contain("BEGIN TRANSACTION;").And.Contain("COMMIT TRANSACTION;");
    }

    [Fact]
    public void CitySearchIsRecordedOnlyAfterPublicCityResolutionAndOnceOnFirstPage()
    {
        var service = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Infrastructure/Storefront/PublicMarketplaceService.cs"));
        var resolve = service[service.IndexOf("public async Task<object?> Resolve", StringComparison.Ordinal)..service.IndexOf("public async Task<object> City(", StringComparison.Ordinal)];
        var cityProducts = service[service.IndexOf("public async Task<object> CityProducts", StringComparison.Ordinal)..service.IndexOf("private static string? NormalizeMarketplaceFacet", StringComparison.Ordinal)];

        resolve.Should().Contain("x.IsActive && x.IsPublished");
        cityProducts.Should().Contain("RecordCitySearch(cityId, searchEventId, term, total, page, ct)")
            .And.Contain("if (page != 1 || eventId is null")
            .And.Contain("SearchTermPrivacy.ForStorage(term)")
            .And.Contain("UPDLOCK,HOLDLOCK")
            .And.Contain("ResultCount");
    }

    [Fact]
    public void OwnerAnalyticsIsPlatformProtectedAndReturnsAggregatesOnly()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Api/Controllers/OwnerSearchAnalyticsController.cs"));
        var service = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Infrastructure/Storefront/OwnerSearchAnalyticsService.cs"));
        controller.Should().Contain("PlatformAuthorize").And.Contain("Permissions.Features.Manage");
        service.Should().Contain("EnsureOwner()")
            .And.Contain("COUNT_BIG(1)")
            .And.Contain("GROUP BY NormalizedKeyword")
            .And.Contain("SearchTermPrivacy.ForStorage(reader.GetString(0))")
            .And.Contain("grouped.TryGetValue(safeKeyword")
            .And.NotContain("CustomerId")
            .And.NotContain("AnonymousSessionId")
            .And.NotContain("OriginalKeyword");
    }

    [Fact]
    public void CityPageUsesStableSearchEventIdsAndNeverTracksPaginationRequests()
    {
        var page = File.ReadAllText(Path.Combine(Root, "frontend/KhataDhari.Customer/src/app/pages/city-marketplace.page.ts"));
        var api = File.ReadAllText(Path.Combine(Root, "frontend/KhataDhari.Customer/src/app/data/public-marketplace-api.service.ts"));
        page.Should().Contain("searchEventId=crypto.randomUUID()")
            .And.Contain("this.searchEventId=crypto.randomUUID()")
            .And.Contain("setTimeout(()=>void this.loadProducts(),250)");
        var nextPage = page[page.IndexOf("nextPage():void", StringComparison.Ordinal)..];
        nextPage.Should().NotContain("searchEventId");
        api.Should().Contain("searchEventId?:string").And.Contain("options.searchEventId");
    }
}
