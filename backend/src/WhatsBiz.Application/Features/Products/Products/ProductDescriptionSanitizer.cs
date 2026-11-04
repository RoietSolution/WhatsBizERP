using System.Text.RegularExpressions;

namespace WhatsBiz.Application.Features.Products.Products;

public static class ProductDescriptionSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase) { "p", "br", "strong", "b", "em", "i", "u", "h2", "h3", "h4", "ul", "ol", "li", "a", "table", "thead", "tbody", "tr", "th", "td" };
    private static readonly Regex DangerousBlocks = new("<!--.*?-->|<\\s*(script|style|iframe|object|embed|form|svg|math)[^>]*>.*?<\\s*/\\s*\\1\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new("<(?<close>/?)\\s*(?<tag>[a-z0-9]+)(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attributes = new("(?<name>[a-zA-Z][a-zA-Z0-9:-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.Compiled);

    public static string? Sanitize(string? html, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var withoutDangerous = DangerousBlocks.Replace(html, string.Empty);
        var clean = Tags.Replace(withoutDangerous, match =>
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            if (!AllowedTags.Contains(tag)) return string.Empty;
            if (match.Groups["close"].Value == "/") return $"</{tag}>";
            if (tag == "br") return "<br>";
            if (tag != "a") return $"<{tag}>";
            var attributes = Attributes.Matches(match.Groups["attrs"].Value).Cast<Match>().ToArray();
            var href = attributes.FirstOrDefault(x => x.Groups["name"].Value.Equals("href", StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value;
            var target = attributes.FirstOrDefault(x => x.Groups["name"].Value.Equals("target", StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value;
            var safeHref = Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? $" href=\"{System.Net.WebUtility.HtmlEncode(uri.AbsoluteUri)}\"" : string.Empty;
            var safeTarget = string.Equals(target, "_blank", StringComparison.Ordinal) ? " target=\"_blank\" rel=\"noopener noreferrer\"" : string.Empty;
            return $"<a{safeHref}{safeTarget}>";
        }).Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}
