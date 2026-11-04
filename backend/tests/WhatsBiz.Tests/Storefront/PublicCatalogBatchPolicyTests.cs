using FluentAssertions;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class PublicCatalogBatchPolicyTests
{
    [Fact]
    public void BatchBoundAcceptsOneHundredUniqueIdsAndRejectsLargerOrDuplicateBatches()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        PublicCatalogBatchPolicy.HasValidIds(ids).Should().BeTrue();
        PublicCatalogBatchPolicy.HasValidIds(ids.Append(Guid.NewGuid()).ToArray()).Should().BeFalse();
        PublicCatalogBatchPolicy.HasValidIds(ids.Append(ids[0]).ToArray()).Should().BeFalse();
        PublicCatalogBatchPolicy.HasValidIds([Guid.Empty]).Should().BeFalse();
        PublicCatalogBatchPolicy.HasValidIds([]).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "DRAFT", "ELIGIBLE")]
    [InlineData(true, "REJECTED", "ELIGIBLE")]
    [InlineData(true, "CHANGES_PENDING", "ELIGIBLE")]
    [InlineData(true, "LEGACY", "ELIGIBLE")]
    [InlineData(true, "PENDING_APPROVAL", "ALREADY_PENDING")]
    [InlineData(true, "APPROVED", "INELIGIBLE")]
    [InlineData(true, "SUSPENDED", "INELIGIBLE")]
    [InlineData(false, "DRAFT", "INACTIVE")]
    public void RetailerSubmissionUsesExistingApprovalStatesAndLegacyPolicy(bool active, string status, string expected)
        => PublicCatalogBatchPolicy.SubmissionStatus(active, status).Should().Be(expected);
}
