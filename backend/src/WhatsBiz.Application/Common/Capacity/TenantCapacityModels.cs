namespace WhatsBiz.Application.Common.Capacity;

public static class TenantResourceTypes
{
    public const string Users = "USERS";
    public const string Branches = "BRANCHES";
    public static readonly IReadOnlyCollection<string> All = [Users, Branches];
}

public sealed record TenantResourceCapacity(
    string ResourceType,
    int Current,
    int? Limit,
    bool Configured,
    bool Unlimited,
    bool OverLimit,
    bool CanCreate,
    string Source);

public sealed record TenantCapacitySummary(
    Guid TenantId,
    string TenantName,
    TenantResourceCapacity Users,
    TenantResourceCapacity Branches);

public sealed record TenantResourceLimitInput(bool Configured, bool Unlimited, int? Limit);
public sealed record UpdateTenantCapacityInput(TenantResourceLimitInput Users, TenantResourceLimitInput Branches);
