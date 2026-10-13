namespace WhatsBiz.Domain.Tenants;

public sealed class Tenant
{
    public Guid TenantId { get; set; }
    public string TenantKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
}
