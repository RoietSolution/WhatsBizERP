using System.Reflection;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontBookingClockTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void OnlyEnabledAssignedSchedulesRestrictProducts(bool enabled, bool assigned, bool expected)
    {
        var product=Guid.NewGuid();var type=typeof(StorefrontAvailabilityService);
        var rowType=type.GetNestedType("Row",BindingFlags.NonPublic)!;
        var row=Activator.CreateInstance(rowType,Guid.NewGuid(),"Test",enabled,TimeSpan.FromHours(14),TimeSpan.FromHours(17),TimeSpan.FromHours(15),TimeSpan.FromHours(17),(byte)127,"Asia/Kolkata",null,null,false,assigned?new List<Guid>{product}:new List<Guid>(),new List<Guid>())!;
        type.GetMethod("MatchesEnabledSchedule",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[row,product]).Should().Be(expected);
    }

    [Fact]
    public void AnnouncementHasCanonicalServerTimeAndItsOwnNextOpening()
    {
        var instant=DateTimeOffset.Parse("2026-10-21T08:00:00Z",CultureInfo.InvariantCulture);
        var service=new StorefrontAvailabilityService(new ConfigurationBuilder().Build(),new FixedClock(instant));
        var type=typeof(StorefrontAvailabilityService);var rowType=type.GetNestedType("Row",BindingFlags.NonPublic)!;
        var product=Guid.NewGuid();
        var row=Activator.CreateInstance(rowType,Guid.NewGuid(),"Evening",true,TimeSpan.FromHours(14),TimeSpan.FromHours(17),TimeSpan.FromHours(15),TimeSpan.FromHours(17),(byte)127,"Asia/Kolkata",null,null,true,new List<Guid>{product,product},new List<Guid>())!;
        var dto=(StorefrontAvailabilityAnnouncementDto)type.GetMethod("ToAnnouncement",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[row])!;
        dto.Status.Should().Be("ORDERING_CLOSED");
        dto.ServerNowUtc.Should().Be(instant);
        dto.NextOrderOpenAtUtc.Should().Be(instant.AddMinutes(30));
        dto.NextOrderCloseAtUtc.Should().BeNull();
        dto.TimeZoneId.Should().Be("Asia/Kolkata");
        dto.ProductIds.Should().Equal(product);
    }

    [Fact]
    public void SavedAssignmentsDoNotPromoteCategoryCoverageToDirectProducts()
    {
        var direct=Guid.NewGuid();var inherited=Guid.NewGuid();var category=Guid.NewGuid();
        var type=typeof(StorefrontAvailabilityService);
        var rowType=type.GetNestedType("Row",BindingFlags.NonPublic)!;
        var row=Activator.CreateInstance(rowType,Guid.NewGuid(),"Test",true,TimeSpan.FromHours(14),TimeSpan.FromHours(17),TimeSpan.FromHours(15),TimeSpan.FromHours(17),(byte)127,"Asia/Kolkata",null,null,false,new List<Guid>{direct,inherited},new List<Guid>{category,category})!;
        ((HashSet<Guid>)rowType.GetProperty("DirectProductIds")!.GetValue(row)!).Add(direct);
        ((HashSet<Guid>)rowType.GetProperty("CategoryCoveredProductIds")!.GetValue(row)!).Add(inherited);
        var dto=(StorefrontAvailabilityScheduleDto)type.GetMethod("ToDto",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[row])!;
        dto.ProductIds.Should().Equal(direct);
        dto.CategoryIds.Should().Equal(category);
        dto.CategoryCoveredProductIds.Should().Equal(inherited);
    }

    [Theory]
    [InlineData("2026-10-21T08:29:59Z", false, "2026-10-21T08:30:00Z")]
    [InlineData("2026-10-21T08:30:00Z", true, "2026-10-21T11:30:00Z")]
    [InlineData("2026-10-21T09:00:00Z", true, "2026-10-21T11:30:00Z")]
    [InlineData("2026-10-21T11:29:59Z", true, "2026-10-21T11:30:00Z")]
    [InlineData("2026-10-21T11:30:00Z", false, "2026-10-22T08:30:00Z")]
    public void BookingUsesTenantTimeAndExclusiveCutoff(string instant, bool open, string transition)
        => AssertWindow(instant, "14:00", "17:00", 127, "Asia/Kolkata", open, transition);

    [Theory]
    [InlineData("2026-10-20T18:00:00Z", true, "2026-10-20T20:30:00Z")]
    [InlineData("2026-10-20T20:30:00Z", false, "2026-10-27T16:30:00Z")]
    public void OvernightUsesOpeningWeekday(string instant, bool open, string transition)
        => AssertWindow(instant, "22:00", "02:00", 4, "Asia/Kolkata", open, transition);

    [Fact]
    public void WindowsTimezoneIdentifierUsesTheSameInstant()
        => AssertWindow("2026-10-21T09:00:00Z", "14:00", "17:00", 127, "India Standard Time", true, "2026-10-21T11:30:00Z");

    [Theory]
    [InlineData("2026-03-29T11:00:00Z", false, "2026-03-29T13:00:00Z")]
    [InlineData("2026-10-25T11:00:00Z", false, "2026-10-25T14:00:00Z")]
    public void NextOpeningUsesTheDateSpecificDaylightSavingOffset(string instant, bool open, string transition)
        => AssertWindow(instant, "14:00", "17:00", 127, "Europe/London", open, transition);

    private static void AssertWindow(string instant, string start, string end, byte mask, string timezone, bool open, string transition)
    {
        var service = new StorefrontAvailabilityService(new ConfigurationBuilder().Build(), new FixedClock(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
        var type = typeof(StorefrontAvailabilityService);
        var rowType = type.GetNestedType("Row", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType, Guid.NewGuid(), "Test", true, TimeSpan.Parse(start, CultureInfo.InvariantCulture), TimeSpan.Parse(end, CultureInfo.InvariantCulture),
            TimeSpan.Parse("15:00", CultureInfo.InvariantCulture), TimeSpan.Parse("17:00", CultureInfo.InvariantCulture), mask, timezone, null, null, false, new List<Guid>(), new List<Guid>());
        var snapshot = type.GetMethod("ResolveSchedule", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [row])!;
        snapshot.GetType().GetProperty("IsOpen")!.GetValue(snapshot).Should().Be(open);
        var field = open ? "NextCloseAtUtc" : "NextOpenAtUtc";
        snapshot.GetType().GetProperty(field)!.GetValue(snapshot).Should().Be(DateTimeOffset.Parse(transition, CultureInfo.InvariantCulture));
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
