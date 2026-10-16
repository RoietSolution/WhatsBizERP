using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class AutomatedStorefrontNotificationArchitectureTests
{
    private static string Root(string relative) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", relative));

    [Fact]
    public void V59CreatesTenantScopedIdempotentQueueAndConfiguration()
    {
        var migration = File.ReadAllText(Root("database/migrations/V59-AutomatedStorefrontNotifications.sql"));
        migration.Should().Contain("StorefrontAutomatedNotificationDeliveries")
            .And.Contain("NewOrderEmailNotificationsEnabled")
            .And.Contain("RestockWhatsAppNotificationsEnabled")
            .And.Contain("UX_StorefrontAutomatedNotificationDeliveries_Event")
            .And.Contain("TenantId,EventType,Channel,EntityId");
    }

    [Fact]
    public void WorkerReconcilesSellableStockWithLeasedIdempotentDelivery()
    {
        var worker = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Notifications/StorefrontAutomatedNotificationWorker.cs"));
        worker.Should().Contain("SUM(b.QuantityOnHand-b.QuantityReserved)>0")
            .And.Contain("RequestStatus=N'NOTIFICATION_PENDING'")
            .And.Contain("UPDLOCK,READPAST,ROWLOCK")
            .And.Contain("LeaseUntilUtc<SYSUTCDATETIME()")
            .And.Contain("WHERE DeliveryId=@id AND Status=N'PROCESSING'");
    }

    [Fact]
    public void MockRestockDoesNotClaimExternalNotificationSuccess()
    {
        var worker = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Notifications/StorefrontAutomatedNotificationWorker.cs"));
        worker.Should().Contain("MOCK_NOT_DELIVERED")
            .And.Contain("result.MarkRequestNotified")
            .And.Contain("RequestStatus=N'NOTIFIED'");
    }

    [Fact]
    public void StorefrontOrdersQueueAfterTheCommittedOrderBoundaryAndDoNotFailTheOrder()
    {
        var checkout = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs"));
        checkout.Should().Contain("QueueOrderNotificationSafely")
            .And.Contain("notification queueing failed")
            .And.Contain("STOREFRONT:");
    }
}
