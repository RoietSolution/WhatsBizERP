namespace WhatsBiz.Domain.Tenants;

public sealed class BusinessType
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; private set; } = "";
    public bool IsActive { get; set; } = true;
    public byte[] Version { get; set; } = [];
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
