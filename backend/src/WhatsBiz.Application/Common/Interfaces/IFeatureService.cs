namespace WhatsBiz.Application.Common.Interfaces;

using WhatsBiz.Application.Common.Features;

public interface IFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, string? changedBy, CancellationToken cancellationToken = default);
    void InvalidateTenant(Guid tenantId);
    void InvalidateAll();
}
