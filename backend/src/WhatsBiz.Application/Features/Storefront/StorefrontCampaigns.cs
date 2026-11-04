using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Application.Features.Storefront;

public sealed record SaveStorefrontCampaign(string Name, string Headline, string HtmlContent, bool IsActive,
    string StartsAtLocal, string EndsAtLocal, string TimeZoneId, string Frequency, int Priority,
    string? CtaText = null, string? DestinationType = null, Guid? DestinationId = null,
    Guid? ScheduleId = null, bool OnlyWhenOrderingOpen = false);
public sealed record StorefrontCampaign(Guid CampaignId, SaveStorefrontCampaign Settings, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, Guid? MediaId, DateTimeOffset UpdatedAtUtc);
public sealed record PublicStorefrontCampaign(Guid CampaignId, string Headline, string HtmlContent, string Frequency,
    int Priority, string? ImageUrl, string? CtaText, string? Destination, string LocalDay, DateTimeOffset EndsAtUtc);
public sealed record StorefrontCampaignFeed(IReadOnlyCollection<PublicStorefrontCampaign> Campaigns,
    DateTimeOffset ServerNowUtc, DateTimeOffset RefreshAtUtc);
public interface IStorefrontCampaignService
{
    Task<IReadOnlyCollection<StorefrontCampaign>> ListAsync(Guid tenant, CancellationToken token);
    Task<StorefrontCampaign> SaveAsync(Guid tenant, Guid? id, SaveStorefrontCampaign input, CancellationToken token);
    Task UploadAsync(Guid tenant, Guid id, string fileName, Stream content, CancellationToken token);
    Task RemoveImageAsync(Guid tenant, Guid id, CancellationToken token);
    Task<StorefrontImage?> GetAdminImageAsync(Guid tenant, Guid id, CancellationToken token)
        => Task.FromResult<StorefrontImage?>(null);
    Task<StorefrontCampaignFeed?> FeedAsync(string storeKey, CancellationToken token);
    Task<StorefrontImage?> ImageAsync(string storeKey, Guid id, Guid media, CancellationToken token);
}

public static class StorefrontCampaignPolicy
{
    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new BusinessRuleException("Choose a supported tenant timezone."); }
        catch (InvalidTimeZoneException) { throw new BusinessRuleException("Choose a supported tenant timezone."); }
    }
    public static DateTimeOffset ToUtc(string local, string timeZone)
    {
        if (!DateTime.TryParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BusinessRuleException("Enter a valid campaign date and time.");
        date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        var zone = Zone(timeZone);
        if (zone.IsInvalidTime(date) || zone.IsAmbiguousTime(date))
            throw new BusinessRuleException("This local time is ambiguous or does not exist. Choose another time.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date, zone));
    }
    public static bool Eligible(StorefrontCampaign campaign, DateTimeOffset now, bool scheduleOpen)
        => campaign.Settings.IsActive && now >= campaign.StartsAtUtc && now < campaign.EndsAtUtc
        && (!campaign.Settings.OnlyWhenOrderingOpen || scheduleOpen);

    public static string Sanitize(string? html)
    {
        if (html?.Length > 30000) throw new BusinessRuleException("Campaign content is too long.");
        var cleaned = ProductDescriptionSanitizer.Sanitize(html, 30000) ?? "";
        // Re-encode all text outside the canonical tags emitted by the existing editor sanitizer.
        // This also closes its malformed/unclosed-tag edge cases. No styles, event handlers or embeds survive.
        var result = new StringBuilder(); var cursor = 0;
        foreach (Match tag in Regex.Matches(cleaned, "</?(?:p|br|strong|b|em|i|u|h2|h3|h4|ul|ol|li|table|thead|tbody|tr|th|td)>|<a(?: href=\"[^\"]*\")?(?: target=\"_blank\" rel=\"noopener noreferrer\")?>|</a>", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            result.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(cleaned[cursor..tag.Index])));
            result.Append(tag.Value); cursor = tag.Index + tag.Length;
        }
        result.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(cleaned[cursor..])));
        return result.ToString();
    }
}
