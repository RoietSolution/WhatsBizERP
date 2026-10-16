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

    [Fact]
    public void AvailabilityRegistrationIsIdempotentAndReportsMissingSchemaSafely()
    {
        var service = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandServiceV52.cs"));
        service.Should().Contain("RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING')")
            .And.Contain("Notification requests are not available because the demand database migration is missing")
            .And.Contain("CustomerId=@customer")
            .And.Contain("var requestId=Guid.NewGuid()")
            .And.Contain("q.Parameters.AddWithValue(\"@id\",requestId)");
        var availabilityMethod = service[(service.IndexOf("RequestAvailabilityAsync", StringComparison.Ordinal))..service.IndexOf("CancelAvailabilityAsync", StringComparison.Ordinal)];
        availabilityMethod.Should().NotContain("NEWSEQUENTIALID()")
            .And.Contain("INSERT commerce.ProductAvailabilityRequests");
    }

    [Fact]
    public void AvailabilityStatusIsCustomerScopedForRefreshPersistence()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Api/Controllers/StoreController.cs"));
        var service = File.ReadAllText(Path.Combine(Root, "backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandServiceV52.cs"));
        controller.Should().Contain("[HttpGet(\"products/{productId:guid}/availability-request\")]")
            .And.Contain("if (tokenValue is null) return NotFound()");
        service.Should().Contain("CustomerId=@customer")
            .And.Contain("RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING')");
    }
}
