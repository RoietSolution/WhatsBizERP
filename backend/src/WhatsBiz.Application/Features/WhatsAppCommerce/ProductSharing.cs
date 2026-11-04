using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhatsBiz.Application.Common.Exceptions;

namespace WhatsBiz.Application.Features.WhatsAppCommerce;

public sealed record ProductSharePreview(Guid ProductId, string ProductName, decimal SellingPrice,
    string PriceLabel, string Description, string StoreName, string ProductUrl, string? ImageUrl,
    string Message, string WhatsAppUrl, bool ApiSendAvailable, string? ApiSendUnavailableReason, string? LinkPreviewUrl = null);
public sealed record SendProductShareInput(Guid CustomerId, Guid RequestId, bool ConsentConfirmed, string? Comment = null);
public sealed record ProductShareSendResult(Guid RequestId, string Status, string? ProviderMessageId, string Message);
public sealed record WhatsAppSingleProductRequest(string ApiVersion, string PhoneNumberId, string AccessToken,
    string RecipientNumber, string Message, string ImageUrl, string ProductUrl, string UrlSuffix,
    string? TemplateName, string LanguageCode, IReadOnlyCollection<string> BodyParameters,
    string? WabaId = null, string? TemplateUrlPrefix = null);

public static partial class ProductSharingPolicy
{
    public static Uri HttpsRoot(string? configured)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var root) || root.Scheme != "https"
            || !string.IsNullOrEmpty(root.UserInfo) || !string.IsNullOrEmpty(root.Query)
            || !string.IsNullOrEmpty(root.Fragment) || root.HostNameType != UriHostNameType.Dns
            || root.IsLoopback)
            throw new BusinessRuleException("Configure a trusted public HTTPS storefront/API URL before sharing products.");
        return new Uri(root.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static string ProductUrl(string? origin, string storeKey, Guid productId)
    {
        if (!StoreKey().IsMatch(storeKey) || productId == Guid.Empty)
            throw new BusinessRuleException("A valid storefront product is required.");
        return new Uri(HttpsRoot(origin), $"{Uri.EscapeDataString(storeKey.ToLowerInvariant())}/products/{productId:D}").AbsoluteUri;
    }

    public static string PlainDescription(string? html)
    {
        var plain = Space().Replace(WebUtility.HtmlDecode(Tags().Replace(html ?? "", " ")), " ").Trim();
        return plain.Length <= 240 ? plain : plain[..237] + "...";
    }

    public static string PriceLabel(decimal price, decimal gst, string mode) =>
        price.ToString("C2", CultureInfo.GetCultureInfo("en-IN")) + (gst > 0 ? mode == "INCLUSIVE" ? " (incl. GST)" : " + GST" : "");

    public static string ImageVersion(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant();

    public static string PreviewVersion(string name, string price, string description, string store, string productUrl, string? imageVersion) =>
        ImageVersion(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { name, price, description, store, productUrl, imageVersion })));

    public static string PreviewUrl(string apiOrigin, string storeKey, Guid productId, string version)
    {
        _ = ProductUrl(apiOrigin, storeKey, productId); // Same tenant/key/root validation as deep links.
        return new Uri(HttpsRoot(apiOrigin), $"api/store/{Uri.EscapeDataString(storeKey.ToLowerInvariant())}/products/{productId:D}/share?v={Uri.EscapeDataString(version)}").AbsoluteUri;
    }

    public static string NormalizeComment(string? value)
    {
        if ((value?.Length ?? 0) > 1000) throw new BusinessRuleException("Comment must be 300 characters or fewer.");
        var text = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = string.Join("\n", text.Split('\n').Select(line => Space().Replace(Links().Replace(Tags().Replace(line, " "), ""), " ").Trim())).Trim();
        if (text.Length > 300) throw new BusinessRuleException("Comment must be 300 characters or fewer.");
        return text;
    }

    public static string Message(string name, string price, string description, string store, string link, string? comment = null) =>
        string.Join("\n", new[] { Links().Replace(name, "").Trim(), price, Links().Replace(PlainDescription(description), "").Trim(),
            NormalizeComment(comment), link }.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    public static string CommentForFormat(string? value, bool session)
    {
        var comment = NormalizeComment(value);
        if (!session && comment.Length > 0)
            throw new BusinessRuleException("This approved template has no comment parameter. Clear the comment to send it, or use manual sharing. Session image captions support comments.");
        return comment;
    }

    public static string Recipient(string? mobile)
    {
        if (!Phone().IsMatch(mobile ?? "")) throw new BusinessRuleException("Use an international WhatsApp number, including country code.");
        var digits = new string((mobile ?? "").Where(c => c is >= '0' and <= '9').ToArray());
        if (digits.Length is < 8 or > 15 || digits[0] == '0')
            throw new BusinessRuleException("The customer needs a valid international WhatsApp number, including country code.");
        return digits;
    }

    public static bool WithinSession(DateTimeOffset? lastInbound, DateTimeOffset now) =>
        lastInbound.HasValue && lastInbound <= now && lastInbound > now.AddHours(-24);

    public static string TemplateSuffix(string productUrl, string configuredPrefix)
    {
        _ = HttpsRoot(configuredPrefix);
        if (!configuredPrefix.EndsWith('/') || !productUrl.StartsWith(configuredPrefix, StringComparison.Ordinal) || productUrl.Length <= configuredPrefix.Length)
            throw new BusinessRuleException("The approved template URL prefix does not match this tenant's product link.");
        return productUrl[configuredPrefix.Length..];
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant)] private static partial Regex StoreKey();
    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)] private static partial Regex Tags();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex Space();
    [GeneratedRegex(@"^[+0-9 ()-]{8,30}$", RegexOptions.CultureInvariant)] private static partial Regex Phone();
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Links();
}
