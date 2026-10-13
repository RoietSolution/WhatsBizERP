using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class CustomerDemandArchitectureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));

    [Fact]
    public void V52UsesTenantScopedFilteredDedupeIndexesAndSafeGuestIdentity()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/migrations/V52-CustomerDemand.sql"));
        sql.Should().Contain("UX_ProductAvailabilityRequests_Active");
        sql.Should().Contain("UX_MissingProductRequests_Active");
        sql.Should().Contain("WHERE RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING')");
        sql.Should().Contain("ISNULL(CONVERT(nvarchar(36),CustomerId),N'')");
        sql.Should().Contain("TenantId");
    }

    [Fact]
    public void PublicDemandEndpointsUseStoreKeyAndDoNotAcceptTenantId()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Api/Controllers/StoreController.cs"));
        controller.Should().Contain("search-events");
        controller.Should().Contain("availability-request");
        controller.Should().Contain("missing-product-requests");
        controller.Should().NotContain("Guid tenantId");
    }

    [Fact]
    public void DemandServiceUsesSellableStockAndTenantOwnership()
    {
        var service = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandServiceV52.cs"));
        service.Should().Contain("QuantityOnHand-b.QuantityReserved");
        service.Should().Contain("IsWhatsAppVisible=1");
        service.Should().Contain("TenantId=@tenant");
    }
}
