using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class CustomerDemandSearchAnalyticsAuthorizationTests
{
    [Fact]
    public void SearchAnalyticsUsesTheSameFeatureAndPermissionAsCustomerDemand()
    {
        var controller = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Api/Controllers/CustomerDemandController.cs")));

        controller.Should().Contain("Route(\"api/customer-demand\"), RequireFeature(FeatureKeys.CustomerDemand)");
        controller.Should().Contain("[HttpGet, HasPermission(Permissions.Dashboard.Analytics)]");
        controller.Should().Contain("[HttpGet(\"search-analytics\"), HasPermission(Permissions.Dashboard.Analytics)]");
    }

    [Fact]
    public void SearchAnalyticsDoesNotCreateASecondPermissionOrFeatureRequirement()
    {
        var controller = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Api/Controllers/CustomerDemandController.cs")));

        controller.Should().NotContain("FeatureKeys.Analytics");
        controller.Should().NotContain("Permissions.CustomerDemand");
        controller.Should().NotContain("Permissions.Customer.View");
    }
}
