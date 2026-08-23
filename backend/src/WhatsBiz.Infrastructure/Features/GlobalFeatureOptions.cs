namespace WhatsBiz.Infrastructure.Features;
public sealed class GlobalFeatureOptions
{
    public const string SectionName = "GlobalFeatures";
    public bool V1 { get; init; } = true;
    public bool V2 { get; init; } = true;
    public IReadOnlyCollection<string> DisabledFeatures { get; init; } = [];
}
