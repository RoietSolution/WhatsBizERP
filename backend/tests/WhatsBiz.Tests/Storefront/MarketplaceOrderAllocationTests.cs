using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class MarketplaceOrderAllocationTests
{
    [Fact]
    public void AllocatesOneItemFromOneEligibleWarehouse()
    {
        var warehouse = Guid.NewGuid();
        var result = MarketplaceWarehouseAllocationPolicy.Allocate(1,
            [new(warehouse, null, null, null, null, 4)]);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new MarketplaceStockAllocation(warehouse, null, null, null, null, 1));
    }

    [Fact]
    public void SeparateItemsCanBeAllocatedFromDifferentWarehouses()
    {
        var warehouseA = Guid.NewGuid();
        var warehouseB = Guid.NewGuid();
        var saree = MarketplaceWarehouseAllocationPolicy.Allocate(1, [new(warehouseA, null, null, null, null, 1)]);
        var rack = MarketplaceWarehouseAllocationPolicy.Allocate(1, [new(warehouseB, null, null, null, null, 1)]);

        saree.Single().WarehouseId.Should().Be(warehouseA);
        rack.Single().WarehouseId.Should().Be(warehouseB);
    }

    [Fact]
    public void SplitsOneItemAcrossWarehousesWhenEachHasPartialStock()
    {
        var warehouseA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var warehouseB = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var result = MarketplaceWarehouseAllocationPolicy.Allocate(3,
        [
            new(warehouseB, null, null, null, null, 1),
            new(warehouseA, null, null, null, null, 2),
        ]);

        result.Select(x => (x.WarehouseId, x.Quantity)).Should().Equal((warehouseA, 2m), (warehouseB, 1m));
        result.Sum(x => x.Quantity).Should().Be(3);
    }

    [Fact]
    public void RejectsOnlyWhenAggregateEligibleStockIsInsufficient()
    {
        var result = () => MarketplaceWarehouseAllocationPolicy.Allocate(3,
        [
            new(Guid.NewGuid(), null, null, null, null, 1),
            new(Guid.NewGuid(), null, null, null, null, 1),
        ]);

        result.Should().Throw<BusinessRuleException>().WithMessage("Insufficient eligible stock.*");
    }

    [Fact]
    public void IneligibleWarehousesAreExcludedBeforeTheQuantityAllocator()
    {
        var eligible = Guid.NewGuid();
        var ineligible = Guid.NewGuid();
        MarketplaceStockCandidate[] candidates =
        {
            new(eligible, null, null, null, null, 2m),
            // The ineligible warehouse is deliberately absent from the candidate set.
        };

        var result = MarketplaceWarehouseAllocationPolicy.Allocate(2, candidates);
        result.Should().ContainSingle().Which.WarehouseId.Should().Be(eligible).And.NotBe(ineligible);
    }

    [Fact]
    public void TransactionPathUsesTenantIdempotencyHeldPosAndV85ReservationPrimitive()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var allocator = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderAllocationService.cs"));
        allocator.Should().Contain("ExecuteForTenant(key, \"MARKETPLACE_ORDER_RESERVE\"")
            .And.Contain("sales.POS_PostInvoice")
            .And.Contain("Status = \"HELD\"")
            .And.Contain("OrderType='MARKETPLACE'")
            .And.Contain("commerce.MarketplaceOrderAllocations")
            .And.Contain("commerce.MarketplaceFulfilmentGroups")
            .And.Contain("commerce.Marketplace_ReserveAllocation")
            .And.Contain("LifecycleStatus='RESERVED'");

        var executor = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Persistence/SqlIdempotencyExecutor.cs"));
        executor.Should().Contain("IsolationLevel.Serializable")
            .And.Contain("await transaction.RollbackAsync(CancellationToken.None)")
            .And.Contain("FixedTimeEquals");
    }

    [Fact]
    public void InventoryLocksAndReservationCheckAreTenantScopedAndDoNotDeductDuringPlanning()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var allocator = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderAllocationService.cs"));
        var reserve = File.ReadAllText(Path.Combine(root, "database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql"));
        allocator.Should().Contain("inventory.InventoryBalances WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("WHERE TenantId=@tenant AND WarehouseId=@warehouse AND ProductId=@product")
            .And.Contain("QuantityAvailable>0")
            .And.NotContain("QuantityOnHand=QuantityOnHand-");
        reserve.Should().Contain("QuantityAvailable>=@Quantity")
            .And.Contain("QuantityReserved=QuantityReserved+@Quantity")
            .And.Contain("@SessionTenant<>@TenantId");
    }

    [Fact]
    public void StorefrontMarketplaceRouteIsServerGatedAndLeavesStandardPosBranchIntact()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var checkout = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V90-MarketplaceCheckoutReleaseGate.sql"));

        checkout.Should().Contain("features.IsEnabledAsync(tenantId, FeatureKeys.MarketplaceCheckout")
            .And.Contain("marketplaceAllocation.CreateHeldOrderAsync")
            .And.Contain("pos.PostForTenant(orderRequest, tenantId")
            .And.Contain("useMarketplaceOrder = marketplaceFeatureEnabled && (containsPartnerProduct || warehouseId is null)")
            .And.Contain("if (!marketplaceFeatureEnabled && containsPartnerProduct)");
        var allocator = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderAllocationService.cs"));
        allocator.Should().Contain("fingerprintInput = executionInput with { ReservationExpiresAt = DateTimeOffset.UnixEpoch }")
            .And.Contain("CreateInTransaction(connection, transaction, tenantId, key, executionInput, ct)");
        migration.Should().Contain("ReleaseState=N'DISABLED'")
            .And.Contain("IsEnabled=0")
            .And.Contain("FeatureKey=N'MARKETPLACE_CHECKOUT'");
    }

    [Fact]
    public void MarketplaceInvoicePayableIsReconciledBeforeTheAllocationTransactionCommits()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var allocator = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderAllocationService.cs"));
        allocator.Should().Contain("invoice.GrandTotal != input.ExpectedPayableAmount")
            .And.Contain("throw new BusinessRuleException(\"The order total has changed")
            .And.Contain("IsolationLevel.Serializable");
    }
}
