using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class CustomerDemandManagementArchitectureTests
{
    [Fact]
    public void MissingProductUpdatesAreTenantScopedAndTransitionChecked()
    {
        var service = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandManagementService.cs")));
        service.Should().Contain("TenantId=@tenant");
        service.Should().Contain("RequestStatus=@current");
        service.Should().Contain("cannot move");
    }

    [Fact]
    public void DemandManagementEndpointRequiresAnalyticsPermission()
    {
        var controller = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "backend/src/WhatsBiz.Api/Controllers/CustomerDemandController.cs")));
        controller.Should().Contain("HttpPatch(\"missing/{requestId:guid}\")");
        controller.Should().Contain("HasPermission(Permissions.Dashboard.Analytics)");
    }
}
