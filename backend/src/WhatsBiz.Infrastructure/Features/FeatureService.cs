using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Features;

public sealed class FeatureService(ApplicationDbContext db, IMemoryCache cache, IOptions<GlobalFeatureOptions> globalOptions) : IFeatureService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private const string GenerationKey = "features:generation";
    private static readonly IReadOnlyDictionary<string, string[]> Dependencies = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [FeatureKeys.V2] = [FeatureKeys.V1],
        [FeatureKeys.Pos] = [FeatureKeys.Products, FeatureKeys.Inventory],
        [FeatureKeys.WhatsAppCommerce] = [FeatureKeys.Products, FeatureKeys.Inventory, FeatureKeys.Customers],
        [FeatureKeys.WhatsAppConfiguration] = [FeatureKeys.WhatsAppCommerce],
        [FeatureKeys.WhatsAppCommerceDemo] = [FeatureKeys.WhatsAppCommerce, FeatureKeys.Pos],
        [FeatureKeys.CommerceProductSearch] = [FeatureKeys.WhatsAppCommerce, FeatureKeys.Products, FeatureKeys.Inventory],
        [FeatureKeys.CommerceCollections] = [FeatureKeys.WhatsAppCommerce, FeatureKeys.Products],
        [FeatureKeys.CommerceOrders] = [FeatureKeys.WhatsAppCommerce, FeatureKeys.Pos],
        [FeatureKeys.CommerceAnalytics] = [FeatureKeys.WhatsAppCommerce],
        [FeatureKeys.MetaWhatsAppIntegration] = [FeatureKeys.WhatsAppConfiguration],
        [FeatureKeys.WebhookDiagnostics] = [FeatureKeys.MetaWhatsAppIntegration]
    };

    public async Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default)
        => (await GetEffectiveFeaturesAsync(tenantId, cancellationToken)).TryGetValue(featureKey, out var enabled) && enabled;

    public async Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var generation = cache.GetOrCreate(GenerationKey, e => { e.Size = 1; return Guid.NewGuid(); });
        var cacheKey = $"features:{generation}:{tenantId}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, bool>? found) && found is not null) return found;
        var configuration = await GetTenantConfigurationCoreAsync(tenantId, cancellationToken);
        var result = configuration.Features.ToDictionary(x => x.FeatureKey, x => x.EffectiveEnabled, StringComparer.OrdinalIgnoreCase);
        cache.Set(cacheKey, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration, Size = 1 });
        return result;
    }

    public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => GetTenantConfigurationCoreAsync(tenantId, cancellationToken);

    public async Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Database.SqlQueryRaw<FeatureTenantSummaryRow>("""
            SELECT t.TenantId,t.TenantKey,t.Name TenantName,p.PlanKey,p.Name PlanName
            FROM core.Tenants t OUTER APPLY
              (SELECT TOP(1) p.PlanKey,p.Name FROM core.Subscriptions s JOIN core.Plans p ON p.PlanId=s.PlanId
               WHERE s.TenantId=t.TenantId AND s.IsActive=1 AND s.StartDate<=SYSUTCDATETIME()
                 AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME()) ORDER BY s.StartDate DESC) p
            WHERE t.IsActive=1 ORDER BY t.Name
            """).ToListAsync(cancellationToken);
        return rows.Select(x => new FeatureTenantSummary(x.TenantId, x.TenantKey, x.TenantName, x.PlanKey, x.PlanName)).ToArray();
    }

    public async Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, string? changedBy, CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return await GetTenantConfigurationCoreAsync(tenantId, cancellationToken);
        var duplicate = updates.GroupBy(x => x.FeatureKey, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"Feature '{duplicate.Key}' was supplied more than once.");
        var current = await GetTenantConfigurationCoreAsync(tenantId, cancellationToken);
        var available = current.Features.ToDictionary(x => x.FeatureKey, StringComparer.OrdinalIgnoreCase);
        foreach (var update in updates)
        {
            if (!available.TryGetValue(update.FeatureKey, out var feature)) throw new ArgumentException($"Unknown feature '{update.FeatureKey}'.");
            if (update.ConfiguredEnabled && !feature.SubscriptionAllowed) throw new InvalidOperationException($"Feature '{update.FeatureKey}' is not included in the active subscription.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var update in updates)
            await db.Database.ExecuteSqlRawAsync("""
                MERGE core.TenantFeatures AS target
                USING (SELECT {0} TenantId,f.FeatureId FROM core.Features f WHERE f.FeatureKey={1}) source
                ON target.TenantId=source.TenantId AND target.FeatureId=source.FeatureId
                WHEN MATCHED THEN UPDATE SET IsEnabled={2},IsActive=1,StartDate=NULL,EndDate=NULL,Reason=N'Feature administration',ModifiedOn=SYSUTCDATETIME(),ModifiedBy={3}
                WHEN NOT MATCHED THEN INSERT(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
                     VALUES(NEWID(),source.TenantId,source.FeatureId,{2},N'Feature administration',1,{3});
                """, [tenantId, update.FeatureKey, update.ConfiguredEnabled, changedBy ?? "system"], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InvalidateTenant(tenantId);
        return await GetTenantConfigurationCoreAsync(tenantId, cancellationToken);
    }

    public void InvalidateTenant(Guid tenantId) => InvalidateAll();
    public void InvalidateAll() => cache.Set(GenerationKey, Guid.NewGuid(), new MemoryCacheEntryOptions { Size = 1 });

    private async Task<TenantFeatureConfiguration> GetTenantConfigurationCoreAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<FeatureRow>("""
            SELECT f.FeatureId,f.FeatureKey,f.Name FeatureName,f.FeatureType,parent.FeatureKey ParentFeatureKey,f.Version,f.SortOrder,
                   CAST(ISNULL(tf.IsEnabled,0) AS bit) ConfiguredEnabled,CAST(ISNULL(planf.IsEnabled,0) AS bit) SubscriptionAllowed,
                   t.Name TenantName,subscriptionPlan.PlanKey,subscriptionPlan.Name PlanName,f.IsActive,f.ReleaseState
            FROM core.Features f JOIN core.Tenants t ON t.TenantId={0} AND t.IsActive=1
            LEFT JOIN core.Features parent ON parent.FeatureId=f.ParentFeatureId
            OUTER APPLY (SELECT TOP(1) s.PlanId,p.PlanKey,p.Name FROM core.Subscriptions s JOIN core.Plans p ON p.PlanId=s.PlanId AND p.IsActive=1
                         WHERE s.TenantId=t.TenantId AND s.IsActive=1 AND s.StartDate<=SYSUTCDATETIME()
                           AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME()) ORDER BY s.StartDate DESC) subscriptionPlan
            LEFT JOIN core.PlanFeatures planf ON planf.PlanId=subscriptionPlan.PlanId AND planf.FeatureId=f.FeatureId
            OUTER APPLY (SELECT TOP(1) tf.IsEnabled FROM core.TenantFeatures tf
                         WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId AND tf.IsActive=1
                           AND (tf.StartDate IS NULL OR tf.StartDate<=SYSUTCDATETIME()) AND (tf.EndDate IS NULL OR tf.EndDate>=SYSUTCDATETIME())
                         ORDER BY tf.ModifiedOn DESC,tf.CreatedOn DESC) tf
            WHERE f.IsActive=1 ORDER BY f.Version,f.SortOrder,f.Name
            """, tenantId).ToListAsync(cancellationToken);
        if (rows.Count == 0) throw new KeyNotFoundException("Tenant was not found or has no active feature definitions.");
        var options = globalOptions.Value;
        var disabled = new HashSet<string>(options.DisabledFeatures, StringComparer.OrdinalIgnoreCase);
        var inputs = rows.Select(row => new FeatureEvaluationInput(row.FeatureId,row.FeatureKey,row.FeatureName,row.FeatureType,row.ParentFeatureKey,row.Version,row.SortOrder,
            row.ConfiguredEnabled,row.SubscriptionAllowed,(row.Version.Equals("V1", StringComparison.OrdinalIgnoreCase) ? options.V1 : options.V2) && !disabled.Contains(row.FeatureKey),
            row.IsActive && row.ReleaseState != "DISABLED",Dependencies.TryGetValue(row.FeatureKey, out var deps) ? deps : [])).ToArray();
        var states = FeatureAccessEvaluator.Evaluate(inputs);
        var first = rows[0];
        return new(tenantId, first.TenantName, first.PlanKey, first.PlanName, states);
    }

    private sealed class FeatureRow
    {
        public Guid FeatureId { get; set; } public string FeatureKey { get; set; } = ""; public string FeatureName { get; set; } = "";
        public string FeatureType { get; set; } = ""; public string? ParentFeatureKey { get; set; } public string Version { get; set; } = ""; public int SortOrder { get; set; }
        public bool ConfiguredEnabled { get; set; } public bool SubscriptionAllowed { get; set; } public string TenantName { get; set; } = "";
        public string? PlanKey { get; set; } public string? PlanName { get; set; } public bool IsActive { get; set; } public string ReleaseState { get; set; } = "";
    }
    private sealed class FeatureTenantSummaryRow { public Guid TenantId { get; set; } public string TenantKey { get; set; } = ""; public string TenantName { get; set; } = ""; public string? PlanKey { get; set; } public string? PlanName { get; set; } }
}
