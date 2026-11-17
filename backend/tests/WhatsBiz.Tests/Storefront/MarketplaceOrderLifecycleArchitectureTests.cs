using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class MarketplaceOrderLifecycleArchitectureTests
{
    private static string Read(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../..", path)));

    [Fact]
    public void V87CompletionConsumesOnlyAllocatedReservedBalancesAndPostsTheParentOnce()
    {
        var migration = Read("database/migrations/V87-MarketplaceReservationLifecycle.sql");
        var completion = migration[(migration.IndexOf("CREATE OR ALTER PROCEDURE commerce.Marketplace_CompleteOrder", StringComparison.Ordinal))..
            migration.IndexOf("CREATE OR ALTER PROCEDURE commerce.Marketplace_ReleaseOrder", StringComparison.Ordinal)];
        completion.Should().Contain("SESSION_CONTEXT(N'TenantId')")
            .And.Contain("@OrderStatus IN('POSTED','PARTIALLY_FULFILLED','FULFILLED')")
            .And.Contain("@OrderStatus<>'CONFIRMED'")
            .And.Contain("@ExpiresAt<=@Now")
            .And.Contain("a.AllocatedQuantity")
            .And.Contain("r.ReservationStatus NOT IN('RESERVED','CONFIRMED')")
            .And.Contain("ORDER BY a.WarehouseId,i.ProductId")
            .And.Contain("QuantityOnHand=QuantityOnHand-@Qty,QuantityReserved=QuantityReserved-@Qty")
            .And.Contain("ReservationStatus='CONSUMED'")
            .And.Contain("LifecycleStatus='POSTED'")
            .And.Contain("finance.PostSource")
            .And.NotContain("sales.POS_TransitionHeldInvoice");
    }

    [Fact]
    public void V87ReleaseAndExpiryAreTenantScopedBoundedAndIdempotent()
    {
        var migration = Read("database/migrations/V87-MarketplaceReservationLifecycle.sql");
        migration.Should().Contain("CREATE OR ALTER PROCEDURE commerce.Marketplace_ReleaseOrder")
            .And.Contain("@OrderStatus IN('CANCELLED','EXPIRED') AND @InvoiceStatus='CANCELLED'")
            .And.Contain("QuantityReserved=QuantityReserved-@Qty")
            .And.Contain("ReservationStatus=CASE WHEN @ReleaseReason='EXPIRE'")
            .And.Contain("CREATE OR ALTER PROCEDURE commerce.Marketplace_ExpireReservationBatch")
            .And.Contain("@BatchSize NOT BETWEEN 1 AND 500")
            .And.Contain("TOP(@BatchSize)")
            .And.Contain("p.Status=N'PAID'")
            .And.Contain("d.LifecycleStatus='PAYMENT_PENDING' AND i.LifecycleStatus IN('CONFIRMED','CANCELLED','EXPIRED')")
            .And.Contain("READPAST");
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderLifecycleService.cs");
        service.Should().Contain("ExecuteForTenant(key, operation")
            .And.Contain("commerce.Marketplace_CompleteOrder")
            .And.Contain("commerce.Marketplace_ReleaseOrder")
            .And.Contain("commerce.Marketplace_ExpireReservationBatch");
    }

    [Fact]
    public void LegacyPosGuardAndOptionalExpiryWorkerRemainSafeByDefault()
    {
        var transition = Read("database/WhatsBiz.Database/StoredProcedures/POS_TransitionHeldInvoice.sql");
        transition.Should().Contain("@OrderType='MARKETPLACE'")
            .And.Contain("Marketplace invoices require the allocation-aware posting or cancellation path");
        var worker = Read("backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceReservationExpiryWorker.cs");
        worker.Should().Contain("Marketplace:ReservationExpiryWorkerEnabled")
            .And.Contain("false)")
            .And.Contain("Math.Clamp(configuration.GetValue(\"Marketplace:ReservationExpiryBatchSize\", 100), 1, 500)");
        var validator = Read("database/migrations/validators/V87-MarketplaceReservationLifecycle.validate.sql");
        validator.Should().Contain("Marketplace_CompleteOrder")
            .And.Contain("Marketplace_ReleaseOrder")
            .And.Contain("Marketplace_ExpireReservationBatch")
            .And.Contain("POS_TransitionHeldInvoice");
    }

    [Fact]
    public void ExpiryServiceReadsTheFinalCountAfterNestedReleaseResultSets()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderLifecycleService.cs");
        var start = service.IndexOf("public async Task<int> ExpireDueBatchAsync", StringComparison.Ordinal);
        var end = service.IndexOf("public async Task<int> ExpireDueAcrossTenantsAsync", start, StringComparison.Ordinal);
        var method = service[start..end];
        method.Should().Contain("ExpiredOrderCount")
            .And.Contain("GetName(ordinal)")
            .And.Contain("NextResultAsync(token)")
            .And.NotContain("ExecuteScalarAsync(token)");
    }

    [Fact]
    public void SqlCompletionFixtureCountsWarehouseHeadersSeparatelyFromAllocationLines()
    {
        var suite = Read("backend/tests/WhatsBiz.Tests/Integration/MarketplaceV85V91SqlIntegrationTests.cs");
        suite.Should().Contain("COUNT(DISTINCT t.TransactionId)=2 AND COUNT(*)=4 AND SUM(d.Quantity)=5")
            .And.Contain("WarehouseId=@warehouseA AND d.ProductId=@split")
            .And.Contain("WarehouseId=@warehouseB AND d.ProductId=@other")
            .And.Contain("SUM(ConsumedQuantity)=5 AND SUM(ReleasedQuantity)=0");
    }
}
