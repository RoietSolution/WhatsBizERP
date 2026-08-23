namespace WhatsBiz.Application.Common.Features;

public sealed record FeatureAccessState(Guid FeatureId, string FeatureKey, string FeatureName, string FeatureType,
    string? ParentFeatureKey, string Version, int SortOrder, bool ConfiguredEnabled, bool EffectiveEnabled,
    string? DisabledReason, bool SubscriptionAllowed, bool GlobalAllowed, IReadOnlyCollection<string> Dependencies);
public sealed record TenantFeatureConfiguration(Guid TenantId, string TenantName, string? PlanKey, string? PlanName,
    IReadOnlyCollection<FeatureAccessState> Features);
public sealed record TenantFeatureUpdate(string FeatureKey, bool ConfiguredEnabled);
public sealed record FeatureTenantSummary(Guid TenantId, string TenantKey, string TenantName, string? PlanKey, string? PlanName);
