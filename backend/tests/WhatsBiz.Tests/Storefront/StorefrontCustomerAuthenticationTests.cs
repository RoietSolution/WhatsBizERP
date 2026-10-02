using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontCustomerAuthenticationTests
{
    [Theory]
    [InlineData("9876543210","9876543210")]
    [InlineData("+91 98765-43210","9876543210")]
    [InlineData("919876543210","9876543210")]
    public void IndianMobileNormalizationAcceptsSupportedFormats(string input,string expected)
        => StorefrontCustomerAuthenticationService.NormalizeMobile(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("5123456789")]
    [InlineData("98765432101")]
    public void IndianMobileNormalizationRejectsInvalidValues(string input)
        => StorefrontCustomerAuthenticationService.NormalizeMobile(input).Should().BeNull();

    [Fact]
    public void OtpHashIsSaltedAndDeterministicForVerification()
    {
        var firstSalt=Enumerable.Repeat((byte)1,16).ToArray();
        var secondSalt=Enumerable.Repeat((byte)2,16).ToArray();
        var first=StorefrontCustomerAuthenticationService.HashOtp("123456",firstSalt);
        StorefrontCustomerAuthenticationService.HashOtp("123456",firstSalt).Should().Equal(first);
        StorefrontCustomerAuthenticationService.HashOtp("123456",secondSalt).Should().NotEqual(first);
        first.Should().HaveCount(32);
    }

    [Fact]
    public void OtpResponsesNeverExposeOtp()
    {
        typeof(StorefrontOtpChallengeDto).GetProperties().Select(x=>x.Name).Should()
            .BeEquivalentTo(["ChallengeId","ExpiresAt","ResendAfterSeconds"]);
        typeof(StorefrontOtpChallengeDto).GetProperties().Select(x=>x.Name).Should().NotContain("Otp");
    }

    [Fact]
    public void OtpEndpointsHaveDedicatedRateLimits()
    {
        typeof(StoreController).GetMethod(nameof(StoreController.RequestCustomerOtp))!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("StorefrontOtpRequest");
        typeof(StoreController).GetMethod(nameof(StoreController.VerifyCustomerOtp))!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("StorefrontOtpVerify");
    }

    [Fact]
    public void OtpImplementationEnforcesExpiryAttemptsSingleUseAndTenantScope()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerAuthenticationService.cs");
        source.Should().Contain("ExpiryMinutes = 5").And.Contain("MaxAttempts = 5").And.Contain("CooldownSeconds = 45");
        source.Should().Contain("ConsumedAt IS NULL").And.Contain("TenantId=@tenant").And.Contain("MobileNumberNormalized=@mobile");
        source.Should().Contain("CryptographicOperations.FixedTimeEquals").And.Contain("RandomNumberGenerator.GetInt32");
        source.Should().Contain("IsolationLevel.Serializable").And.Contain("WITH(UPDLOCK,HOLDLOCK)");
        source.Should().NotContain("Random()");
    }

    [Fact]
    public void MigrationStoresOnlyOtpHashAndHasCustomerUniqueness()
    {
        var sql=Read("database/WhatsBiz.Database/Scripts/V39-StorefrontCustomerSessions.sql");
        sql.Should().Contain("OtpHash varbinary(32)").And.Contain("OtpSalt varbinary(16)");
        sql.Should().Contain("UX_Customers_TenantMobileNormalized");
        sql.Should().Contain("UNIQUE INDEX").And.Contain("TenantId,MobileNormalized");
        sql.Should().NotContain("PlaintextOtp");
    }

    [Fact]
    public void SessionIsBoundToStoreTenantAndCustomer()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        source.Should().Contain("payload.StoreKey").And.Contain("payload.TenantId").And.Contain("payload.CustomerId");
        source.Should().Contain("c.IsActive=1 AND c.IsDeleted=0");
    }

    [Fact]
    public void OrdersAndWishlistAuthorizeBySessionCustomerNotMobile()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        source.Should().Contain("i.TenantId=@tenant AND i.CustomerId=@customer");
        source.Should().Contain("TenantId=@tenant AND CustomerId=@customer");
        source.Should().NotContain("WHERE Mobile=");
    }

    [Fact]
    public void GuestCheckoutDoesNotIssueAuthenticatedSession()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs");
        source.Should().Contain("authenticated?.Id ?? await FindOrCreateCustomer");
        source.Should().Contain("CustomerMessage(method), null");
        source.Should().NotContain("IssueSessionAsync");
    }

    [Fact]
    public void OtpVerificationDerivesIdentityWithoutBrowserTenantOrCustomer()
    {
        typeof(StorefrontOtpVerifyInput).GetProperties().Select(x=>x.Name).Should()
            .BeEquivalentTo(["ChallengeId","MobileNumber","Otp","Name","Email"]);
        typeof(StorefrontOtpVerifyInput).GetProperties().Select(x=>x.Name).Should().NotContain(["TenantId","CustomerId"]);
    }

    [Fact]
    public async Task QaTestModeUsesDefaultCodeAndSkipsProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:QaTestModeEnabled"] = "true",
            ["StorefrontOtp:QaTestCode"] = "123456"
        }).Build();
        var policy = new StorefrontOtpPolicy("QA", configuration);
        policy.CreateCode().Should().Be("123456");
        policy.SkipDelivery.Should().BeTrue();
        var sender = new CustomerOtpSender(policy, configuration, new ThrowingClientFactory());
        await sender.SendAsync("9876543210", policy.CreateCode(), Guid.NewGuid(), CancellationToken.None);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void ExistingLocalTestModeStillUsesFixedCode(string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:DevelopmentCode"] = "123456"
        }).Build();
        var policy = new StorefrontOtpPolicy(environmentName, configuration);
        policy.CreateCode().Should().Be("123456");
        policy.SkipDelivery.Should().BeTrue();
    }
    [Fact]
    public void QaTestModeAcceptsOnlySixDigitOverride()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:QaTestModeEnabled"] = "true",
            ["StorefrontOtp:QaTestCode"] = "654321"
        }).Build();
        new StorefrontOtpPolicy("QA", configuration).CreateCode().Should().Be("654321");

        configuration["StorefrontOtp:QaTestCode"] = "not-six-digits";
        Action invalid = () => { _ = new StorefrontOtpPolicy("QA", configuration); };
        invalid.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ConfiguredCustomerFixedOtpWorksInProductionWithoutProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontCustomerAuth:FixedOtpEnabled"] = "true",
            ["StorefrontCustomerAuth:FixedOtp"] = "123456"
        }).Build();
        var policy = new StorefrontOtpPolicy("Production", configuration);
        policy.CreateCode().Should().Be("123456");
        policy.SkipDelivery.Should().BeTrue();
        var sender = new CustomerOtpSender(policy, configuration, new ThrowingClientFactory());
        await sender.SendAsync("9876543210", policy.CreateCode(), Guid.NewGuid(), CancellationToken.None);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Qa")]
    public void QaTestModeFailsStartupOutsideExactQa(string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:QaTestModeEnabled"] = "true"
        }).Build();
        Action startup = () => { _ = new StorefrontOtpPolicy(environmentName, configuration); };
        startup.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("QA")]
    [InlineData("Production")]
    public async Task WithoutQaTestModeRequiresProvider(string environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();
        var policy = new StorefrontOtpPolicy(environmentName, configuration);
        policy.SkipDelivery.Should().BeFalse();
        var sender = new CustomerOtpSender(policy, configuration, new ThrowingClientFactory());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("9876543210", policy.CreateCode(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void ChallengeVerificationRejectsWrongCodeExpiryAndReuse()
    {
        var now = DateTimeOffset.UtcNow;
        var salt = Enumerable.Repeat((byte)7, 16).ToArray();
        var expected = StorefrontCustomerAuthenticationService.HashOtp("123456", salt);
        StorefrontCustomerAuthenticationService.OtpMatches(expected, salt, "123456").Should().BeTrue();
        StorefrontCustomerAuthenticationService.OtpMatches(expected, salt, "000000").Should().BeFalse();
        StorefrontCustomerAuthenticationService.CanVerifyChallenge(now.AddMinutes(5), null, 0, now).Should().BeTrue();
        StorefrontCustomerAuthenticationService.CanVerifyChallenge(now, null, 0, now).Should().BeFalse();
        StorefrontCustomerAuthenticationService.CanVerifyChallenge(now.AddMinutes(5), now, 0, now).Should().BeFalse();
        StorefrontCustomerAuthenticationService.CanVerifyChallenge(now.AddMinutes(5), null, 5, now).Should().BeFalse();
    }

    private sealed class ThrowingClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The provider must not be called.");
    }

    private static string Read(string relative)=>File.ReadAllText(Path.Combine(Root(),relative.Replace('/',Path.DirectorySeparatorChar)));
    private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"database")))d=d.Parent;return d?.FullName??throw new InvalidOperationException();}
}
