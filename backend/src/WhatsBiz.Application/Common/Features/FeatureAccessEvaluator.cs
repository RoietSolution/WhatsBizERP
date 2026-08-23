namespace WhatsBiz.Application.Common.Features;

public sealed record FeatureEvaluationInput(Guid FeatureId, string FeatureKey, string FeatureName, string FeatureType,
    string? ParentFeatureKey, string Version, int SortOrder, bool ConfiguredEnabled, bool SubscriptionAllowed,
    bool GlobalAllowed, bool Available, IReadOnlyCollection<string> Dependencies);

public static class FeatureAccessEvaluator
{
    public static IReadOnlyCollection<FeatureAccessState> Evaluate(IReadOnlyCollection<FeatureEvaluationInput> inputs)
    {
        var source = inputs.ToDictionary(x => x.FeatureKey, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, FeatureAccessState>(StringComparer.OrdinalIgnoreCase);
        FeatureAccessState Resolve(string key, HashSet<string> path)
        {
            if (result.TryGetValue(key, out var existing)) return existing;
            if (!source.TryGetValue(key, out var item))
                return new(Guid.Empty,key,key,"MODULE",null,"",int.MaxValue,false,false,"DEPENDENCY_NOT_DEFINED",false,false,[]);
            if (!path.Add(key)) throw new InvalidOperationException($"Circular feature dependency detected at '{key}'.");
            var reason = !item.Available ? "FEATURE_UNAVAILABLE" : !item.GlobalAllowed ? "GLOBAL_FEATURE_DISABLED"
                : !item.SubscriptionAllowed ? "SUBSCRIPTION_NOT_ENTITLED" : !item.ConfiguredEnabled ? "TENANT_CONFIGURATION_DISABLED" : null;
            if (reason is null && item.ParentFeatureKey is not null && !Resolve(item.ParentFeatureKey, path).EffectiveEnabled) reason = "PARENT_VERSION_DISABLED";
            if (reason is null)
            {
                var missing = item.Dependencies.FirstOrDefault(x => !Resolve(x, path).EffectiveEnabled);
                if (missing is not null) reason = $"DEPENDENCY_DISABLED:{missing}";
            }
            path.Remove(key);
            return result[key] = new(item.FeatureId,item.FeatureKey,item.FeatureName,item.FeatureType,item.ParentFeatureKey,item.Version,item.SortOrder,
                item.ConfiguredEnabled,reason is null,reason,item.SubscriptionAllowed,item.GlobalAllowed,item.Dependencies);
        }
        foreach (var input in inputs) Resolve(input.FeatureKey, new(StringComparer.OrdinalIgnoreCase));
        return result.Values.OrderBy(x => x.Version).ThenBy(x => x.SortOrder).ToArray();
    }
}
