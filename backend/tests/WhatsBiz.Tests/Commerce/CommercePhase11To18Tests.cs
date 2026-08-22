using FluentAssertions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Tests.Commerce;

public sealed class CommercePhase11To18Tests
{
    [Fact]
    public void AnalyticsEventContractDoesNotAcceptTenantFromClient()
    {
        typeof(CommerceAnalyticsEventInput).GetProperties().Select(x => x.Name)
            .Should().NotContain("TenantId");
    }

    [Fact]
    public void AnalyticsEventContractSupportsTenantSafeCommerceReferences()
    {
        typeof(CommerceAnalyticsEventInput).GetProperties().Select(x => x.Name)
            .Should().Contain(["EventType", "CustomerId", "ProductId", "VariantId", "CollectionId", "Metadata"]);
    }
}
