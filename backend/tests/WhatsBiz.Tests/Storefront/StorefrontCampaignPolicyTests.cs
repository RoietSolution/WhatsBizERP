using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontCampaignPolicyTests
{
    private static SaveStorefrontCampaign Input(bool linked=false)=>new("Campaign","Headline","<p>Welcome</p>",true,
        "2026-10-22T14:00","2026-10-22T17:00","Asia/Kolkata","VISIT",1,ScheduleId:linked?Guid.NewGuid():null,OnlyWhenOrderingOpen:linked);
    private static StorefrontCampaign Campaign(bool linked=false){var settings=Input(linked);return new(Guid.NewGuid(),settings,StorefrontCampaignPolicy.ToUtc(settings.StartsAtLocal,settings.TimeZoneId),StorefrontCampaignPolicy.ToUtc(settings.EndsAtLocal,settings.TimeZoneId),null,DateTimeOffset.UtcNow);}
    [Theory]
    [InlineData("2026-10-22T08:29:59Z",false)]
    [InlineData("2026-10-22T08:30:00Z",true)]
    [InlineData("2026-10-22T09:30:00Z",true)]
    [InlineData("2026-10-22T11:30:00Z",false)]
    public void CampaignUsesInclusiveStartExclusiveEndAndMidWindowArrival(string instant,bool expected)
        =>StorefrontCampaignPolicy.Eligible(Campaign(),DateTimeOffset.Parse(instant,System.Globalization.CultureInfo.InvariantCulture),false).Should().Be(expected);
    [Fact]
    public void LinkedCampaignFailsClosedWhenScheduleIsDisabledClosedOrUnassigned()
    {var campaign=Campaign(true);var now=campaign.StartsAtUtc.AddMinutes(1);StorefrontCampaignPolicy.Eligible(campaign,now,false).Should().BeFalse();StorefrontCampaignPolicy.Eligible(campaign,now,true).Should().BeTrue();StorefrontCampaignPolicy.Eligible(campaign with{Settings=campaign.Settings with{IsActive=false}},now,true).Should().BeFalse();}
    [Theory]
    [InlineData("2026-03-08T02:30")]
    [InlineData("2026-11-01T01:30")]
    public void AmbiguousAndNonexistentDstLocalTimesAreRejected(string local)
    {var action=()=>StorefrontCampaignPolicy.ToUtc(local,"America/New_York");action.Should().Throw<BusinessRuleException>();}
    [Fact]
    public void OvernightPeriodAndDifferentTenantTimezonesAreIndependent()
    {var campaign=Campaign() with{StartsAtUtc=StorefrontCampaignPolicy.ToUtc("2026-10-22T22:00","Asia/Kolkata"),EndsAtUtc=StorefrontCampaignPolicy.ToUtc("2026-10-23T02:00","Asia/Kolkata")};StorefrontCampaignPolicy.Eligible(campaign,campaign.StartsAtUtc.AddHours(3),false).Should().BeTrue();StorefrontCampaignPolicy.ToUtc("2026-10-22T14:00","Europe/London").Should().NotBe(campaign.StartsAtUtc);}
    [Theory]
    [InlineData("<p onclick='alert(1)'>Hello<script>alert(1)</script></p>")]
    [InlineData("<img src=x onerror=alert(1)><iframe src='https://example.com'></iframe><svg onload=alert(1)></svg>")]
    [InlineData("<a href='javascript:alert(1)'>Unsafe</a><a href='data:text/html,test'>Data</a>")]
    [InlineData("<script src=x <p>Bad</p>")]
    [InlineData("<a href='jav&#x61;script:alert(1)' style='color:red'>Link</a>")]
    public void HtmlIsSanitizedOnInputAndOutput(string input)
    {var result=StorefrontCampaignPolicy.Sanitize(input);result.Should().NotContain("<script").And.NotContain("<iframe").And.NotContain("<img").And.NotContain("<svg").And.NotContain("onclick=").And.NotContain("onerror=").And.NotContain("href=\"javascript:").And.NotContain("href=\"data:");StorefrontCampaignPolicy.Sanitize(result).Should().Be(result);}
    [Fact]
    public void SafeFormattingAndLinksSurviveWithoutStylesOrHandlers()
    {var result=StorefrontCampaignPolicy.Sanitize("<h3>Welcome</h3><p style='color:red'><strong>Offer</strong> <a href='https://example.com' target='_blank'>Details</a></p>");result.Should().Contain("<strong>Offer</strong>").And.Contain("noopener noreferrer").And.NotContain("style=");}
}
