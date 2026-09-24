using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed partial class MetaCloudApiWhatsAppProvider
{
    public async Task<MetaCatalogDiscoveryResult> DiscoverCatalogsAsync(
        MetaCatalogDiscoveryRequest request, CancellationToken token)
    {
        var errors = new List<MetaCatalogDiscoveryError>();
        var version = Uri.EscapeDataString(request.ApiVersion);
        var waba = Uri.EscapeDataString(request.WhatsAppBusinessAccountId);

        var wabaResult = await ReadGraphAsync("WABA_DETAILS",
            $"{BaseUrl()}/{version}/{waba}?fields=id,name,owner_business_info,on_behalf_of_business_info",
            request.AccessToken, token);
        if (!wabaResult.Succeeded)
        {
            if (wabaResult.Error is not null) errors.Add(wabaResult.Error);
            return new(request.WhatsAppBusinessAccountId, null, null, [], null, null,
                new("UNKNOWN", "UNKNOWN"), PermissionFailure(errors)
                    ? "BUSINESS_PERMISSION_MISSING" : "UNKNOWN_META_ERROR", errors);
        }

        if (!TryObjectId(wabaResult.Root, out var returnedWaba)
            || !string.Equals(returnedWaba, request.WhatsAppBusinessAccountId, StringComparison.Ordinal))
        {
            errors.Add(Malformed("WABA_DETAILS"));
            return new(request.WhatsAppBusinessAccountId, null, null, [], null, null,
                new("UNKNOWN", "UNKNOWN"), "UNKNOWN_META_ERROR", errors);
        }

        var (businessId, businessName) = Business(wabaResult.Root);
        var permissions = await ReadGraphAsync("TOKEN_PERMISSIONS",
            $"{BaseUrl()}/{version}/me/permissions", request.AccessToken, token);
        if (!permissions.Succeeded && permissions.Error is not null) errors.Add(permissions.Error);
        var granted = permissions.Succeeded ? GrantedPermissions(permissions.Root, errors) : null;

        EdgeReadResult owned = EdgeReadResult.NotAttempted;
        EdgeReadResult accessible = EdgeReadResult.NotAttempted;
        if (!string.IsNullOrWhiteSpace(businessId))
        {
            var escapedBusiness = Uri.EscapeDataString(businessId);
            owned = await ReadCatalogEdgeAsync("OWNED_CATALOGS",
                $"{BaseUrl()}/{version}/{escapedBusiness}/owned_product_catalogs?fields=id,name,vertical&limit=100",
                request.AccessToken, token);
            accessible = await ReadCatalogEdgeAsync("ACCESSIBLE_CATALOGS",
                $"{BaseUrl()}/{version}/{escapedBusiness}/client_product_catalogs?fields=id,name,vertical&limit=100",
                request.AccessToken, token);
            AddError(owned, errors);
            AddError(accessible, errors);
        }

        var linked = await ReadCatalogEdgeAsync("WABA_LINKED_CATALOGS",
            $"{BaseUrl()}/{version}/{waba}/product_catalogs?fields=id,name,vertical&limit=100",
            request.AccessToken, token);
        AddError(linked, errors);

        var catalogs = MergeCatalogs(owned, accessible, linked);
        int? eligibleCount = linked.Succeeded ? catalogs.Count(x => x.WhatsAppEligible == true) : null;
        var candidate = eligibleCount == 1
            ? catalogs.Single(x => x.WhatsAppEligible == true).CatalogId
            : null;

        var discovery = owned.Succeeded || accessible.Succeeded || linked.Succeeded
            ? "GRANTED"
            : PermissionFailure(errors) ? "DENIED" : "UNKNOWN";
        var management = granted is null
            ? "UNKNOWN"
            : granted.Contains("catalog_management")
                ? "PERMISSION_GRANTED_ASSET_ACCESS_UNVERIFIED"
                : "PERMISSION_NOT_GRANTED";
        var diagnostic = Diagnostic(eligibleCount, catalogs.Count, errors,
            owned.Succeeded || accessible.Succeeded);

        return new(request.WhatsAppBusinessAccountId, businessId, businessName, catalogs,
            eligibleCount, candidate, new(discovery, management), diagnostic, errors);
    }

    private async Task<EdgeReadResult> ReadCatalogEdgeAsync(string operation, string firstUrl,
        string accessToken, CancellationToken token)
    {
        var items = new Dictionary<string, CatalogNode>(StringComparer.Ordinal);
        var url = firstUrl;
        for (var page = 0; page < 10; page++)
        {
            var result = await ReadGraphAsync(operation, url, accessToken, token);
            if (!result.Succeeded) return new(false, items.Values.ToArray(), result.Error);
            if (!result.Root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new(false, items.Values.ToArray(), Malformed(operation));
            foreach (var node in data.EnumerateArray())
            {
                if (!TryObjectId(node, out var id)) continue;
                items[id] = new(id, String(node, "name"));
            }
            var cursor = AfterCursor(result.Root);
            if (string.IsNullOrWhiteSpace(cursor)) return new(true, items.Values.ToArray(), null);
            url = firstUrl + "&after=" + Uri.EscapeDataString(cursor);
        }
        return new(false, items.Values.ToArray(),
            new(operation, 200, null, null, null, "Meta catalog pagination exceeded the diagnostic safety limit."));
    }

    private async Task<GraphReadResult> ReadGraphAsync(string operation, string url,
        string accessToken, CancellationToken token)
    {
        try
        {
            using var request = Create(HttpMethod.Get, url, accessToken);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                MetaProviderLogs.RequestRejected(logger, operation, (int)response.StatusCode);
                return new(false, default, SafeMetaError(operation, response.StatusCode, body));
            }
            using var document = JsonDocument.Parse(body);
            return new(true, document.RootElement.Clone(), null);
        }
        catch (HttpRequestException)
        {
            return new(false, default, new(operation, null, null, null, null,
                "Meta could not be reached. Check network connectivity and try again."));
        }
        catch (JsonException)
        {
            return new(false, default, Malformed(operation));
        }
    }

    internal static MetaCatalogDiscoveryError SafeMetaError(string operation, HttpStatusCode status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
                return new(operation, (int)status, Integer(error, "code"), Integer(error, "error_subcode"),
                    SafeType(String(error, "type")), SanitizeMetaMessage(String(error, "message")));
        }
        catch (JsonException) { }
        return new(operation, (int)status, null, null, null,
            $"Meta Graph request failed (HTTP {(int)status}).");
    }

    internal static string SanitizeMetaMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Meta rejected the request.";
        var safe = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        safe = Regex.Replace(safe, @"(?i)(access[_ -]?token|bearer)\s*[:=]?\s*[^\s,;]+", "$1 [REDACTED]");
        safe = Regex.Replace(safe, @"(?i)\bEAA[A-Za-z0-9_-]{12,}\b", "[REDACTED_TOKEN]");
        return safe[..Math.Min(safe.Length, 240)];
    }

    private static IReadOnlyCollection<MetaCatalogDiscoveryCatalog> MergeCatalogs(
        EdgeReadResult owned, EdgeReadResult accessible, EdgeReadResult linked)
    {
        var names = owned.Items.Concat(accessible.Items).Concat(linked.Items)
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                StringComparer.Ordinal);
        var ownedIds = owned.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var accessibleIds = accessible.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var linkedIds = linked.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return names.OrderBy(x => x.Value ?? x.Key, StringComparer.OrdinalIgnoreCase).Select(x =>
        {
            var isOwned = ownedIds.Contains(x.Key);
            var isAccessible = accessibleIds.Contains(x.Key);
            var isLinked = linkedIds.Contains(x.Key);
            var relationship = isLinked
                ? isOwned ? "OWNED_AND_WABA_LINKED" : isAccessible ? "ACCESSIBLE_AND_WABA_LINKED" : "WABA_LINKED"
                : isOwned ? "OWNED" : "ACCESSIBLE";
            return new MetaCatalogDiscoveryCatalog(x.Key, x.Value, relationship,
                linked.Succeeded ? isLinked : null);
        }).ToArray();
    }

    private static string Diagnostic(int? eligibleCount, int catalogCount,
        IReadOnlyCollection<MetaCatalogDiscoveryError> errors, bool businessDiscoverySucceeded)
    {
        if (eligibleCount == 1) return "ONE_ELIGIBLE_CATALOG";
        if (eligibleCount > 1) return "MULTIPLE_CATALOGS";
        if (eligibleCount == 0)
            return catalogCount > 0 ? "WABA_CATALOG_NOT_LINKED"
                : businessDiscoverySucceeded ? "NO_CATALOG"
                : PermissionFailure(errors) ? "CATALOG_PERMISSION_MISSING" : "UNKNOWN_META_ERROR";
        return PermissionFailure(errors) ? "CATALOG_PERMISSION_MISSING" : "UNKNOWN_META_ERROR";
    }

    private static bool PermissionFailure(IEnumerable<MetaCatalogDiscoveryError> errors) =>
        errors.Any(x => x.MetaCode is 10 or 200 ||
            x.SafeMessage.Contains("permission", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string>? GrantedPermissions(JsonElement root,
        ICollection<MetaCatalogDiscoveryError> errors)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Malformed("TOKEN_PERMISSIONS"));
            return null;
        }
        return data.EnumerateArray()
            .Where(x => string.Equals(String(x, "status"), "granted", StringComparison.OrdinalIgnoreCase))
            .Select(x => String(x, "permission"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static (string? Id, string? Name) Business(JsonElement waba)
    {
        foreach (var property in new[] { "owner_business_info", "on_behalf_of_business_info" })
            if (waba.TryGetProperty(property, out var business) && business.ValueKind == JsonValueKind.Object
                && TryObjectId(business, out var id)) return (id, String(business, "name"));
        return (null, null);
    }

    private static string? AfterCursor(JsonElement root) =>
        root.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object
        && paging.TryGetProperty("cursors", out var cursors) && cursors.ValueKind == JsonValueKind.Object
        ? String(cursors, "after") : null;
    private static bool TryObjectId(JsonElement element, out string id)
    {
        id = String(element, "id") ?? string.Empty;
        return id.Length > 0;
    }
    private static int? Integer(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) ? number : null;
    private static string? SafeType(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : Regex.IsMatch(value, @"^[A-Za-z0-9_.-]{1,80}$", RegexOptions.CultureInvariant) ? value : "REDACTED";
    private static MetaCatalogDiscoveryError Malformed(string operation) =>
        new(operation, 200, null, null, null, "Meta returned an unexpected catalog discovery response.");
    private static void AddError(EdgeReadResult result, ICollection<MetaCatalogDiscoveryError> errors)
    { if (result.Error is not null) errors.Add(result.Error); }

    private sealed record GraphReadResult(bool Succeeded, JsonElement Root, MetaCatalogDiscoveryError? Error);
    private sealed record CatalogNode(string Id, string? Name);
    private sealed record EdgeReadResult(bool Succeeded, IReadOnlyCollection<CatalogNode> Items,
        MetaCatalogDiscoveryError? Error)
    { public static EdgeReadResult NotAttempted { get; } = new(false, [], null); }
}
