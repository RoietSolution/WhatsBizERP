using System.Reflection;
using System.Net;
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
            .Contain(["ChallengeId","MobileNumber","Otp","Name","Email","IsRegistration","Password","ConfirmPassword","ReplacePassword",
                "AcceptTerms","TermsPolicyId","TermsContentHash","AcknowledgePrivacy","PrivacyPolicyId","PrivacyContentHash"]);
        typeof(StorefrontOtpVerifyInput).GetProperties().Select(x=>x.Name).Should().NotContain(["TenantId","CustomerId"]);
    }

    [Fact]
    public void PasswordResetAndOtpLoginCannotCreateCustomersImplicitly()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerAuthenticationService.cs");
        source.Should().Contain("if (!input.IsRegistration) return null;")
            .And.Contain("SignInPasswordAsync")
            .And.Contain("MobileNormalized IS NULL AND RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(Mobile")
            .And.NotContain("CustomerExists(tenantId.Value, mobile, token)");
        source.IndexOf("if(await find.ExecuteScalarAsync(token) is Guid existing)",StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("ReadRequiredPolicy(connection, transaction, tenantId, \"CUSTOMER_TERMS\"",StringComparison.Ordinal));
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

    [Fact]
    public async Task TwoFactorOtpUsesTheErpGeneratedCodeOverHttps()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:Provider"] = "2Factor",
            ["StorefrontOtp:Endpoint"] = "https://2factor.in/API/V1/OTP/SEND",
            ["StorefrontOtp:ApiKey"] = "test-key",
            ["StorefrontOtp:TemplateName"] = "LOGIN_OTP"
        }).Build();
        var handler = new OtpRecordingHandler();
        var policy = new StorefrontOtpPolicy("Production", configuration);
        var sender = new CustomerOtpSender(policy, configuration, new OtpClientFactory(handler));

        await sender.SendAsync("9876543210", "123456", Guid.NewGuid(), CancellationToken.None);

        handler.Uri!.Scheme.Should().Be("https");
        handler.Uri.AbsolutePath.Should().Be("/API/V1/OTP/SEND");
        handler.ApiKey.Should().Be("test-key");
        handler.Method.Should().Be(HttpMethod.Post);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body);
        payload.RootElement.GetProperty("to").GetString().Should().Be("+919876543210");
        payload.RootElement.GetProperty("channel").GetString().Should().Be("SMS");
        payload.RootElement.GetProperty("template_name").GetString().Should().Be("LOGIN_OTP");
        payload.RootElement.GetProperty("var1").GetString().Should().Be("123456");
    }

    [Fact]
    public async Task TwoFactorLegacyAutogenTemplateSendsTheErpGeneratedCodeInsteadOfRequestingProviderGeneratedOtp()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:Provider"] = "2Factor",
            ["StorefrontOtp:Endpoint"] = "https://2factor.in/API/V1/:api_key/SMS/:phone_number/AUTOGEN/OTP1",
            ["StorefrontOtp:ApiKey"] = "test-key"
        }).Build();
        var handler = new OtpRecordingHandler();
        var policy = new StorefrontOtpPolicy("Production", configuration);
        var sender = new CustomerOtpSender(policy, configuration, new OtpClientFactory(handler));

        await sender.SendAsync("9876543210", "123456", Guid.NewGuid(), CancellationToken.None);

        handler.Uri!.Scheme.Should().Be("https");
        Uri.UnescapeDataString(handler.Uri.AbsolutePath).Should().Be("/API/V1/test-key/SMS/+919876543210/123456");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.Body.Should().BeEmpty();
        handler.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task TwoFactorOtpRejectsNonTwoFactorOrHttpEndpointBeforeSending()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontOtp:Provider"] = "2Factor",
            ["StorefrontOtp:Endpoint"] = "http://example.invalid/API/V1/OTP/SEND",
            ["StorefrontOtp:ApiKey"] = "test-key",
            ["StorefrontOtp:TemplateName"] = "LOGIN_OTP"
        }).Build();
        var handler = new OtpRecordingHandler();
        var policy = new StorefrontOtpPolicy("Production", configuration);
        var sender = new CustomerOtpSender(policy, configuration, new OtpClientFactory(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("9876543210", "123456", Guid.NewGuid(), CancellationToken.None));
        handler.Calls.Should().Be(0);
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

    private sealed class OtpRecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Body { get; private set; } = "";
        public string? ApiKey { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Method = request.Method;
            request.Headers.TryGetValues("X-API-Key", out var keyValues);
            ApiKey = keyValues?.SingleOrDefault();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"sent\",\"session_id\":\"test\"}") };
        }
    }
    private sealed class OtpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static string Read(string relative)=>File.ReadAllText(Path.Combine(Root(),relative.Replace('/',Path.DirectorySeparatorChar)));
    private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"database")))d=d.Parent;return d?.FullName??throw new InvalidOperationException();}
}
