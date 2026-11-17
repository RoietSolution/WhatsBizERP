using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace WhatsBiz.Api.Controllers;

/// <summary>Canonicalizes retailer-entered Shopify source labels while preserving existing domain keys.</summary>
public static class ShopifySourceIdentity
{
    public static bool TryNormalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255) return false;

        var trimmed = value.Trim().Normalize(NormalizationForm.FormC);
        if (trimmed.Any(char.IsControl)) return false;

        if (trimmed.Contains('@'))
        {
            try
            {
                var email = new MailAddress(trimmed);
                if (!email.Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) return false;
                canonical = email.Address.ToLowerInvariant();
                return canonical.Length <= 255;
            }
            catch (FormatException) { return false; }
        }

        // Preserve the historic canonical key for accepted Shopify domains, including
        // optional https:// and case normalization.
        if (!trimmed.Any(char.IsWhiteSpace) && (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.Contains('.')))
        {
            var raw = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                uri.UserInfo.Length > 0 || uri.Port != 443 || uri.AbsolutePath != "/" ||
                !string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment) ||
                IPAddress.TryParse(uri.IdnHost, out _)) return false;
            canonical = uri.IdnHost.ToLowerInvariant();
            return canonical.Contains('.') && canonical.Length <= 255;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal)) return false;
        var label = Regex.Replace(trimmed, @"\s+", " ").Trim();
        if (label.Length == 0 || label.Length > 255) return false;
        canonical = label.ToLowerInvariant();
        return true;
    }
}
