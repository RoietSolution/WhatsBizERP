using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Features;

public sealed class FeatureService(ApplicationDbContext db, IMemoryCache cache) : IFeatureService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string GenerationKey = "features:generation";

    public async Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default)
        => (await GetEffectiveFeaturesAsync(tenantId, cancellationToken)).TryGetValue(featureKey, out var enabled) && enabled;

    public async Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var generation = cache.GetOrCreate(GenerationKey, entry =>
        {
            entry.Size = 1;
            return Guid.NewGuid();
        });
        var key = $"features:{generation}:{tenantId}";
        if (cache.TryGetValue(key, out IReadOnlyDictionary<string, bool>? found) && found is not null) return found;
        var rows = await db.Database.SqlQueryRaw<EffectiveFeatureRow>("""
            SELECT f.FeatureKey, CAST(CASE WHEN f.IsActive = 0 OR f.ReleaseState = 'DISABLED' THEN 0 WHEN o.FeatureId IS NOT NULL THEN o.IsEnabled ELSE ISNULL(pf.IsEnabled, 0) END AS bit) IsEnabled
            FROM core.Features f
            OUTER APPLY (SELECT TOP (1) tf.FeatureId,tf.IsEnabled FROM core.TenantFeatures tf WHERE tf.TenantId={0} AND tf.FeatureId=f.FeatureId AND tf.IsActive=1 AND (tf.StartDate IS NULL OR tf.StartDate<=SYSUTCDATETIME()) AND (tf.EndDate IS NULL OR tf.EndDate>=SYSUTCDATETIME()) ORDER BY tf.ModifiedOn DESC,tf.CreatedOn DESC) o
            OUTER APPLY (SELECT TOP (1) s.PlanId FROM core.Subscriptions s WHERE s.TenantId={0} AND s.IsActive=1 AND s.StartDate<=SYSUTCDATETIME() AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME()) ORDER BY s.StartDate DESC) sub
            LEFT JOIN core.PlanFeatures pf ON pf.PlanId=sub.PlanId AND pf.FeatureId=f.FeatureId
            WHERE f.FeatureKey IN ('WHATSAPP_COMMERCE','ADVANCED_WAREHOUSE','AI_ASSISTANT','INTEGRATIONS')
            """, tenantId).ToListAsync(cancellationToken);
        var result = FeatureKeys.All.ToDictionary(x => x, x => rows.FirstOrDefault(r => r.FeatureKey == x)?.IsEnabled ?? false, StringComparer.OrdinalIgnoreCase);
        cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration, Size = 1 });
        return result;
    }

    public void InvalidateTenant(Guid tenantId) => InvalidateAll();
    public void InvalidateAll() => cache.Set(GenerationKey, Guid.NewGuid(), new MemoryCacheEntryOptions { Size = 1 });
    private sealed class EffectiveFeatureRow { public string FeatureKey { get; set; } = string.Empty; public bool IsEnabled { get; set; } }
}
