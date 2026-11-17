namespace WhatsBiz.Domain.Inventory;

public sealed class ShopifyInventoryImportHistory
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string SourceStore { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileHash { get; set; } = string.Empty;
    public string WarehouseMappingsJson { get; set; } = "{}";
    public string ResultsJson { get; set; } = "[]";
    public int ImportedRows { get; set; }
    public int SkippedRows { get; set; }
    public int FailedRows { get; set; }
    public DateTimeOffset ImportedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? ImportedBy { get; set; }
}
