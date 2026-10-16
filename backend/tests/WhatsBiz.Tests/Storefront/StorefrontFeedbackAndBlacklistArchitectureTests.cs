using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontFeedbackAndBlacklistArchitectureTests
{
    [Fact]
    public void V55DefinesTenantScopedFeedbackAndBlacklistSchema()
    {
        var migration = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "database/migrations/V55-StorefrontFeedbackAndCustomerBlacklist.sql")));
        migration.Should().Contain("TenantId UNIQUEIDENTIFIER NOT NULL")
            .And.Contain("UX_StorefrontCustomerPhoneBlacklist_ActivePhone")
            .And.Contain("WHERE IsActive=1")
            .And.Contain("STOREFRONT_FEEDBACK")
            .And.Contain("STOREFRONT_CUSTOMER_BLACKLIST");
    }

    [Fact]
    public void FeedbackAndBlacklistControllersUseIndependentFeatureAndManagementPermissions()
    {
        var feedback = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Api/Controllers/StorefrontFeedbackController.cs")));
        var blacklist = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Api/Controllers/StorefrontCustomerBlacklistController.cs")));
        feedback.Should().Contain("RequireFeature(FeatureKeys.StorefrontFeedback)").And.Contain("Permissions.Customer.FeedbackManage");
        blacklist.Should().Contain("RequireFeature(FeatureKeys.StorefrontCustomerBlacklist)").And.Contain("Permissions.Customer.BlacklistManage");
    }

    [Fact]
    public void FeedbackMenuAndRouteUseTheSameFeatureAndPermissionAsTheApi()
    {
        var sidebar = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "frontend/WhatsBiz.Web/src/app/shared/layout/sidebar/sidebar.component.ts")));
        var routes = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "frontend/WhatsBiz.Web/src/app/app.routes.ts")));
        sidebar.Should().Contain("route: '/storefront/feedback', permission: 'customer.feedback.manage', feature: 'STOREFRONT_FEEDBACK'");
        routes.Should().Contain("permission: 'customer.feedback.manage', feature: 'STOREFRONT_FEEDBACK'");
        sidebar.IndexOf("Storefront Orders", StringComparison.Ordinal).Should().BeLessThan(sidebar.IndexOf("Feedback & Complaints", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckoutChecksTenantBlacklistAfterResolvingVerifiedCustomerIdentity()
    {
        var checkout = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs")));
        checkout.Should().Contain("blacklist.IsBlockedAsync(tenantId, authenticated?.Mobile ?? mobile")
            .And.Contain("Orders are temporarily unavailable for this phone number");
    }
}
