namespace WhatsBiz.Application.Common.Interfaces;

public interface IFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    void InvalidateTenant(Guid tenantId);
    void InvalidateAll();
}
