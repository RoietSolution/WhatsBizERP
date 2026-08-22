namespace WhatsBiz.Domain.Customers;

public sealed class CustomerGroup
{
    public Guid CustomerGroupId { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public string GroupCode { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public ICollection<Customer> Customers { get; set; } = [];
}
