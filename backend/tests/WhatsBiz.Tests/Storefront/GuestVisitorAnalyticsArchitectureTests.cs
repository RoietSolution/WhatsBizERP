using FluentAssertions;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class GuestVisitorAnalyticsArchitectureTests
{
    private static string Source(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", path)));

    [Fact]
    public void GuestCollectionRequiresExplicitGlobalGateFeatureAndTenantConsent()
    {
        var service = Source("backend/src/WhatsBiz.Infrastructure/Storefront/GuestVisitorAnalyticsService.cs");
        var activity = Source("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontActivityService.cs");

        service.Should().Contain("configuration.GetValue<bool>(\"GuestAnalytics:CollectionEnabled\")")
            .And.Contain("configuration.GetValue<bool>(\"GuestAnalytics:RetentionCleanupEnabled\")")
            .And.Contain("FeatureKey, token")
            .And.Contain("PolicyReady(tenant, token)")
            .And.Contain("NoticeVersion=@notice")
            .And.Contain("NOT EXISTS(SELECT 1 FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND ClientEventId=@event)")
            .And.Contain("VALUES(@tenant,@type,NULL,@product,NULL,@session,@event,@category,@term,@result)")
            .And.Contain("SearchTermPrivacy.ForStorage(e.SearchTerm!)");
        activity.Should().Contain("customers.GetSessionAsync(storeKey, sessionToken, token)")
            .And.Contain("VALUES(@tenant,@type,@customer,@product,@metadata,NULL)");
        activity.Split("HashSet<string> Events", StringSplitOptions.None)[1].Split("};", StringSplitOptions.None)[0]
            .Should().NotContain("ORDER_PLACED");
    }

    [Fact]
    public void PublicAnalyticsDoesNotAcceptTenantIdentifiersOrExposeGuestIdentity()
    {
        var service = Source("backend/src/WhatsBiz.Infrastructure/Storefront/GuestVisitorAnalyticsService.cs");
        var controller = Source("backend/src/WhatsBiz.Api/Controllers/GuestVisitorAnalyticsController.cs");
        var activity = Source("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontActivityService.cs");
        var migration = Source("database/migrations/V82-GuestVisitorAnalytics.sql");

        service.Should().Contain("WHERE TenantKey=@key AND IsActive=1")
            .And.Contain("currentUser.Roles.Contains(\"ApplicationOwner\"")
            .And.Contain("UPDATE requestRow SET SearchEventId=NULL")
            .And.Contain("transaction.CommitAsync(token)")
            .And.Contain("SYSUTCDATETIME()")
            .And.Contain("input.Events.Count is < 1 or > 50");
        activity.Should().Contain("CustomerId IS NOT NULL AND GuestSessionId IS NULL")
            .And.Contain("e.CustomerId IS NOT NULL AND e.GuestSessionId IS NULL");
        controller.Should().Contain("RequireFeature(FeatureKeys.GuestVisitorAnalytics)")
            .And.Contain("HasPermission(Permissions.Dashboard.Analytics)")
            .And.Contain("PlatformAuthorize");
        migration.Should().Contain("TenantId uniqueidentifier NOT NULL")
            .And.Contain("ON DELETE CASCADE")
            .And.Contain("ClientEventId")
            .And.Contain("TenantId,ClientEventId");
    }

    [Fact]
    public void RetentionCleanupIsBoundedReviewedAndDisabledByDefault()
    {
        var worker = Source("backend/src/WhatsBiz.Infrastructure/Storefront/GuestVisitorAnalyticsRetentionWorker.cs");
        worker.Should().Contain("GuestAnalytics:RetentionCleanupEnabled")
            .And.Contain("PolicyReviewedAtUtc IS NOT NULL AND p.PolicyReviewedBy IS NOT NULL")
            .And.Contain("health.MarkHealthy()")
            .And.Contain("health.MarkUnhealthy()")
            .And.Contain("TOP(@batch)")
            .And.Contain("READPAST")
            .And.Contain("UPDATE requestRow SET SearchEventId=NULL")
            .And.Contain("transaction.RollbackAsync")
            .And.NotContain("VisitorId");
        Source("backend/src/WhatsBiz.Infrastructure/Storefront/GuestVisitorAnalyticsService.cs")
            .Should().Contain("!retentionHealth.IsHealthy");
        Source("backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandServiceV52.cs")
            .Should().Contain("retentionHealth.IsHealthy");
    }

    [Fact]
    public void RetentionHealthFailsClosedAndCanRecoverAfterSuccessfulCleanup()
    {
        var health = new GuestVisitorAnalyticsRetentionHealth();
        health.IsHealthy.Should().BeFalse();
        health.MarkHealthy();
        health.IsHealthy.Should().BeTrue();
        health.MarkUnhealthy();
        health.IsHealthy.Should().BeFalse();
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Tablet) Chrome/120.0", "TABLET")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Mobile) Chrome/120.0", "MOBILE")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0", "DESKTOP")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) Safari/604.1", "TABLET")]
    public void DeviceClassificationUsesOnlyCoarseUserAgentCategories(string userAgent, string expected)
    {
        GuestVisitorAnalyticsService.Classify(userAgent).Device.Should().Be(expected);
    }

    [Fact]
    public void CustomerClientOnlyTracksAfterOptInAndUsesBatchedAnonymousEvents()
    {
        var client = Source("frontend/KhataDhari.Customer/src/app/storefront-activity.service.ts");
        var featureKeys = Source("frontend/WhatsBiz.Web/src/app/core/features/feature-keys.ts");

        client.Should().Contain("if (!this.hasConsent(storeKey)) return")
            .And.Contain("granted: true")
            .And.Contain("sessionId, events: batch")
            .And.Contain("FOREGROUND_ENGAGEMENT");
        featureKeys.Should().Contain("GuestVisitorAnalytics: 'GUEST_VISITOR_ANALYTICS'");
    }
}

