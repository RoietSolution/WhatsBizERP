namespace WhatsBiz.Infrastructure.Storefront;

public sealed class ProductPublication
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string Status { get; set; } = "DRAFT";
    public byte[] ContentHash { get; set; } = [];
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Reason { get; set; }
    public string? Reviewer { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class PublicCatalogAudit
{
    public long AuditId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProductId { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? Reason { get; set; }
    public string? ContentSnapshot { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class StoreDirectoryEntry
{
    public Guid TenantId { get; set; }
    public string BusinessType { get; set; } = "";
    public string? BusinessTypeCode { get; set; }
    public string ShortDescription { get; set; } = "";
    public bool OptedIn { get; set; }
    public bool OwnerApproved { get; set; }
    public byte[] Version { get; set; } = [];
}

public static class PublicCatalogPolicy
{
    public static string EffectiveStatus(string? status, byte[]? reviewed, byte[] current) =>
        status is null ? "DRAFT" : status is "APPROVED" or "LEGACY" or "PENDING_APPROVAL"
            && (reviewed is null || !reviewed.SequenceEqual(current)) ? "CHANGES_PENDING" : status;

    public static string Transition(string status, string action, bool owner, string? reason)
    {
        if (!owner && action != "SUBMIT") throw new UnauthorizedAccessException("Only the Application Owner may review products.");
        return action switch
        {
            "SUBMIT" when status is "DRAFT" or "REJECTED" or "CHANGES_PENDING" or "LEGACY" => "PENDING_APPROVAL",
            "SUBMIT" when owner && status == "SUSPENDED" => "PENDING_APPROVAL",
            "APPROVE" when owner && status == "PENDING_APPROVAL" => "APPROVED",
            "REJECT" when owner && status == "PENDING_APPROVAL" && !string.IsNullOrWhiteSpace(reason) => "REJECTED",
            "SUSPEND" when owner && status is "APPROVED" or "LEGACY" or "CHANGES_PENDING" && !string.IsNullOrWhiteSpace(reason) => "SUSPENDED",
            _ => throw new WhatsBiz.Application.Common.Exceptions.BusinessRuleException("This action is not valid for the current approval status; rejection/suspension requires a reason.")
        };
    }
}

public static class PublicCatalogBatchPolicy
{
    public const int MaximumItems = 100;
    private static readonly HashSet<string> RetailerSubmissionStates = new(StringComparer.Ordinal)
        { "DRAFT", "REJECTED", "CHANGES_PENDING", "LEGACY" };

    public static bool HasValidIds(IReadOnlyCollection<Guid>? ids) => ids is { Count: > 0 and <= MaximumItems }
        && ids.All(id => id != Guid.Empty)
        && ids.Distinct().Count() == ids.Count;

    public static string SubmissionStatus(bool active, string status) => !active
        ? "INACTIVE"
        : status == "PENDING_APPROVAL" ? "ALREADY_PENDING"
        : RetailerSubmissionStates.Contains(status) ? "ELIGIBLE"
        : "INELIGIBLE";
}
