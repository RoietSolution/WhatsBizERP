using FluentAssertions;
using WhatsBiz.Application.Features.Referrals;

namespace WhatsBiz.Tests.Loyalty;

public sealed class CustomerReferralRewardTests
{
    [Fact]
    public void ReferralCodesAreServerGeneratedShareableAndUnambiguous()
    {
        var codes=Enumerable.Range(0,1000).Select(_=>ReferralCodeGenerator.Create()).ToArray();
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(x=>x.Length==8&&x.All(c=>"ABCDEFGHJKLMNPQRSTUVWXYZ23456789".Contains(c)));
        ReferralCodeGenerator.Normalize(" kd7xm42 ").Should().Be("KD7XM42");
    }

    [Theory]
    [InlineData(5)] [InlineData(21)]
    public void ReferralCodeLengthIsBounded(int length)=>FluentActions.Invoking(()=>ReferralCodeGenerator.Create(length)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void ReferralMigrationContainsSecurityLifecycleAndLedgerInvariants()
    {
        var sql=File.ReadAllText(Find("database","WhatsBiz.Database","Scripts","V15-CustomerReferralRewards.sql"));
        sql.Should().Contain("UQ_CustomerReferralCodes_Code").And.Contain("COLLATE Latin1_General_100_CI_AS");
        sql.Should().Contain("UQ_CustomerReferrals_OneReferrer").And.Contain("CK_CustomerReferrals_Different");
        sql.Should().Contain("PENDING").And.Contain("QUALIFIED").And.Contain("REWARDED").And.Contain("REVERSED");
        sql.Should().Contain("sp_getapplock").And.Contain("EventKey").And.Contain("CustomerRewardLots");
        sql.Should().Contain("TenantId=@TenantId").And.Contain("Self-referrals are not allowed");
        sql.Should().Contain("MaximumRewardedReferralsPerCustomerMonth").And.Contain("MaximumCoinsPerCustomerMonth");
        sql.Should().Contain("ExpireCustomerRewards").And.Contain("N'EXPIRY'");
        sql.ToUpperInvariant().Should().NotContain("RETAILERREFERRAL");
    }

    [Fact]
    public void FeatureIsHierarchicalAndOrderLifecycleInvokesReferralEvaluation()
    {
        File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Features","FeatureService.cs")).Should().Contain("CustomerReferralRewards").And.Contain("FeatureKeys.Customers");
        File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Loyalty","LoyaltyService.cs")).Should().Contain("referrals.EvaluateOrderAsync").And.Contain("referrals.ReverseOrderAsync");
        File.ReadAllText(Find("frontend","WhatsBiz.Web","src","app","app.routes.ts")).Should().Contain("path: 'ref/:code'");
    }

    private static string Find(params string[] parts)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"backend")))directory=directory.Parent;
        return Path.Combine([directory?.FullName??throw new DirectoryNotFoundException(),..parts]);
    }
}
