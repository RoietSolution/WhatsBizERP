#pragma warning disable CA1711
namespace WhatsBiz.Domain.Commerce;

public sealed class CommerceCollection
{
    public Guid CollectionId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<CommerceCollectionProduct> Products { get; set; } = [];
}

public sealed class CommerceCollectionProduct
{
    public Guid CollectionProductId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid ProductId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public CommerceCollection Collection { get; set; } = null!;
    public WhatsBiz.Domain.Products.Product Product { get; set; } = null!;
}
