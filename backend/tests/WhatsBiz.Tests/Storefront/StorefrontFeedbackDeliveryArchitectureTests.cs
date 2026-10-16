using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontFeedbackDeliveryArchitectureTests
{
    private static string Root(string relative) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", relative));

    [Fact]
    public void V57AddsRecoverableLeasesRetrySchedulingAndProcessingState()
    {
        var migration = File.ReadAllText(Root("database/migrations/V57-StorefrontFeedbackDeliveryWorker.sql"));
        migration.Should().Contain("LeaseUntilUtc").And.Contain("NextAttemptAtUtc")
            .And.Contain("Status IN(N'PENDING',N'PROCESSING',N'SENT',N'FAILED',N'SKIPPED')")
            .And.Contain("IX_StorefrontFeedbackNotificationDeliveries_Ready");
    }

    [Fact]
    public void DeliveryWorkerUsesAtomicClaimsAndBoundedRetrySignals()
    {
        var worker = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Notifications/StorefrontFeedbackNotificationWorker.cs"));
        worker.Should().Contain("UPDLOCK, READPAST, ROWLOCK")
            .And.Contain("LeaseUntilUtc<SYSUTCDATETIME()")
            .And.Contain("item.AttemptCount >= settings.MaxAttempts")
            .And.Contain("WHERE DeliveryId=@id AND Status=N'PROCESSING'");
    }

    [Fact]
    public void FeedbackEventsEnqueueEachChannelIdempotentlyAndTenantScoped()
    {
        var service = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontFeedbackService.cs"));
        service.Should().Contain("QueueExternalDeliveriesAsync")
            .And.Contain("NOT EXISTS(SELECT 1 FROM commerce.StorefrontFeedbackNotificationDeliveries d WHERE d.NotificationId=n.NotificationId AND d.Channel=N'EMAIL')")
            .And.Contain("n.TenantId=@tenant AND n.TicketId=@ticket");
    }

    [Fact]
    public void ExternalDeliveryIsSafeWhenProvidersAreDisabledOrUnconfigured()
    {
        var worker = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Notifications/StorefrontFeedbackNotificationWorker.cs"));
        worker.Should().Contain("new(\"SKIPPED\"")
            .And.Contain("WhatsApp complaint delivery requires explicit customer opt-in")
            .And.Contain("Email complaint notifications are disabled")
            .And.Contain("SMTP email configuration is incomplete");
    }
}
