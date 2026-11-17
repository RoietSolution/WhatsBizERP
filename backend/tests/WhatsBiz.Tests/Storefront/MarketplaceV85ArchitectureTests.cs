using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class MarketplaceV85ArchitectureTests
{
    private static string Read(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../..", path)));

    [Fact]
    public void V85IsAdditiveAndKeepsLegacyInvoicesClassifiedAsStandard()
    {
        var migration = Read("database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql");
        migration.Should().Contain("ADD OrderType varchar(20) NOT NULL")
            .And.Contain("DEFAULT('STANDARD') WITH VALUES")
            .And.Contain("OrderType IN('STANDARD','MARKETPLACE')")
            .And.Contain("MarketplaceOrders is the explicit discriminator")
            .And.Contain("CREATE OR ALTER PROCEDURE sales.POS_TransitionHeldInvoice")
            .And.Contain("Marketplace invoices require the allocation-aware posting or cancellation path")
            .And.Contain("CREATE OR ALTER PROCEDURE commerce.Marketplace_ReserveAllocation");
        migration.Should().Contain("TR_MarketplaceInvoices_CompatibilityGuard")
            .And.Contain("TR_MarketplaceInvoiceItems_AllocationGuard")
            .And.Contain("Marketplace invoice state must be changed through its allocation-aware order state");

        var validator = Read("database/migrations/validators/V85-MarketplaceMultiWarehouseFoundation.validate.sql");
        validator.Should().Contain("is_disabled=0 AND is_not_trusted=0")
            .And.Contain("CK_MarketplaceReservations_State")
            .And.Contain("UQ_MarketplaceCodCollections_TenantOrder");
    }

    [Fact]
    public void V85AllocationAndReservationKeysAreTenantScopedAndRetrySafe()
    {
        var migration = Read("database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql");
        migration.Should().Contain("FK_MarketplaceAllocations_Warehouse FOREIGN KEY(TenantId,WarehouseId)")
            .And.Contain("FK_MarketplaceAllocations_Partner FOREIGN KEY(TenantId,PartnerSellerId)")
            .And.Contain("FK_MarketplaceAllocations_Supplier FOREIGN KEY(TenantId,SupplierId)")
            .And.Contain("UX_MarketplaceAllocations_Idempotency")
            .And.Contain("UX_MarketplaceReservations_Idempotency")
            .And.Contain("UQ_MarketplaceReservations_Allocation")
            .And.Contain("QuantityReserved=QuantityReserved+@Quantity")
            .And.Contain("QuantityAvailable>=@Quantity")
            .And.Contain("SESSION_CONTEXT(N'TenantId')");
    }

    [Fact]
    public void V85FulfilmentsAndCodCollectionAreBoundToTheirParentOrder()
    {
        var migration = Read("database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql");
        migration.Should().Contain("UQ_MarketplaceFulfilmentGroups_Number UNIQUE(TenantId,OrderId,GroupNumber)")
            .And.Contain("UQ_MarketplaceFulfilmentGroupItems_Allocation UNIQUE(TenantId,AllocationId)")
            .And.Contain("FK_MarketplaceFulfilmentGroupItems_Group")
            .And.Contain("FK_MarketplaceFulfilmentGroupItems_Allocation")
            .And.Contain("CK_MarketplaceFulfilmentGroups_Status")
            .And.Contain("Dispatched or delivered fulfilment groups require consumed inventory allocations")
            .And.Contain("V85 cancelled or expired marketplace orders must release all remaining reservations")
            .And.Contain("UQ_MarketplaceCodCollections_TenantOrder UNIQUE(TenantId,OrderId)")
            .And.Contain("CollectedAmount=ExpectedAmount")
            .And.Contain("parent invoice amount")
            .And.Contain("CollectionIdempotencyKey");
    }

    [Fact]
    public void LegacyPosAndStorefrontCheckoutRemainGatedFromMarketplaceProcessing()
    {
        var transition = Read("database/WhatsBiz.Database/StoredProcedures/POS_TransitionHeldInvoice.sql");
        transition.Should().Contain("@OrderType='MARKETPLACE'")
            .And.Contain("Marketplace invoices require the allocation-aware posting or cancellation path");
        var checkout = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs");
        checkout.Should().Contain("NO_COMMON_FULFILLMENT_WAREHOUSE")
            .And.Contain("RejectPartnerSellerCheckout");
    }
}
