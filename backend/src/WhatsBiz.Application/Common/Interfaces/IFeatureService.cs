namespace WhatsBiz.Application.Common.Interfaces;

using WhatsBiz.Application.Common.Features;

public interface IFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default);
    async Task<IReadOnlyCollection<Guid>> GetEnabledTenantIdsAsync(string featureKey, IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        var enabled = new List<Guid>();
        foreach (var tenantId in tenantIds)
            if (await IsEnabledAsync(tenantId, featureKey, cancellationToken)) enabled.Add(tenantId);
        return enabled;
    }
    Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, string? changedBy, CancellationToken cancellationToken = default);
    Task<TenantFeatureConfiguration> UpdateTenantDisplayNameAsync(Guid tenantId, string? displayName, string? changedBy, CancellationToken cancellationToken = default);
    void InvalidateTenant(Guid tenantId);
    void InvalidateAll();
}
