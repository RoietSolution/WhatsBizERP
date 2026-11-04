using System.Text.RegularExpressions;
using System.Text;

namespace WhatsBiz.Infrastructure.Storefront;

internal static partial class SearchTermPrivacy
{
    private const string RedactedTerm = "[REDACTED SEARCH]";

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d\s().-]{5,}\d)(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:address|flat|house|street|road|landmark|pincode|pin code|near me)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    internal static string Normalize(string value) => string.Join(' ', value.Normalize(NormalizationForm.FormC)
        .Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static string ForStorage(string value)
    {
        var normalized = Normalize(value);
        return EmailPattern().IsMatch(normalized) || PhonePattern().IsMatch(normalized) || AddressPattern().IsMatch(normalized)
            ? RedactedTerm
            : normalized;
    }
}
