using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontAvailabilityArchitectureTests
{
    [Fact]
    public void V67DefinesTenantScopedSchedulesAndAssignments()
    {
        var migration = File.ReadAllText(Path.Combine(Root(), "database/migrations/V67-StorefrontScheduledAvailability.sql"));
        migration.Should().Contain("commerce.StorefrontAvailabilitySchedules")
            .And.Contain("commerce.StorefrontAvailabilityScheduleProducts")
            .And.Contain("commerce.StorefrontAvailabilityScheduleCategories")
            .And.Contain("TenantId")
            .And.Contain("WeekdayMask")
            .And.Contain("TimeZoneId")
            .And.Contain("OrderStartTime")
            .And.Contain("OrderEndTime")
            .And.Contain("DeliveryStartTime")
            .And.Contain("DeliveryEndTime")
            .And.Contain("IF OBJECT_ID");
    }

    [Fact]
    public void ScheduleServiceUsesBookingWindowForOrderEligibilityAndCarriesDeliveryWindowSeparately()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontAvailabilityService.cs"));
        source.Should().Contain("OrderStartTime").And.Contain("DeliveryStartTime")
            .And.Contain("ORDERING_OPEN").And.Contain("ORDERING_CLOSED")
            .And.Contain("EnsureOrderEligibleAsync");
        source.IndexOf("OrderStartTime", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("ORDERING_OPEN", StringComparison.Ordinal));
    }

    [Fact]
    public void ScheduleAdministrationIsTenantAuthorizedAndStorefrontAnnouncementIsPublic()
    {
        typeof(StorefrontAdministrationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        var store = File.ReadAllText(Path.Combine(Root(), "backend/src/WhatsBiz.Api/Controllers/StoreController.cs"));
        store.Should().Contain("availability-announcements");
    }

    [Fact]
    public void CheckoutRevalidatesScheduleEligibilityBeforePosting()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs"));
        source.Should().Contain("EnsureOrderEligibleAsync");
        source.IndexOf("EnsureOrderEligibleAsync", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("PostForTenant", StringComparison.Ordinal));
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
