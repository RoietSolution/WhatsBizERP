namespace WhatsBiz.Domain.Products;

public sealed class RetailerShopifyProductSource
{
    public Guid SourceId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string SourceStore { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedOn { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RetailerShopifyProductImportHistory
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string SourceStore { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileHash { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTimeOffset ImportedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? ImportedBy { get; set; }
}

