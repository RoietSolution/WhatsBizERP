using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppFoundationTests
{
    [Fact]
    public void WebhookSignatureAcceptsOnlyMatchingHmac()
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\"}");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), body)).ToLowerInvariant();

        WhatsAppService.ValidSignature(signature, body, "secret").Should().BeTrue();
        WhatsAppService.ValidSignature(signature, body, "other").Should().BeFalse();
        WhatsAppService.ValidSignature("sha256=invalid", body, "secret").Should().BeFalse();
        WhatsAppService.ValidSignature(null, body, "secret").Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(WhatsAppController.Get))]
    [InlineData(nameof(WhatsAppController.Save))]
    [InlineData(nameof(WhatsAppController.Validate))]
    [InlineData(nameof(WhatsAppController.SendTestMessage))]
    [InlineData(nameof(WhatsAppController.Diagnostics))]
    public void ConfigurationEndpointsRequireApplicationOwnerAndExplicitTenant(string methodName)
    {
        var method = typeof(WhatsAppController).GetMethod(methodName)!;
        method.GetCustomAttributes<PlatformAuthorizeAttribute>().Should().ContainSingle();
        method.GetParameters().Should().Contain(parameter => parameter.Name == "tenantId" && parameter.ParameterType == typeof(Guid));
        method.GetCustomAttributes<HasPermissionAttribute>().Single().Policy
            .Should().Be(PermissionPolicyProvider.Prefix + Permissions.Features.Manage);
    }

    [Fact]
    public void SafeConfigurationContractHasNoSecretValues()
    {
        var names = typeof(WhatsAppConfigurationDto).GetProperties().Select(x => x.Name).ToArray();
        names.Should().NotContain("AccessToken").And.NotContain("WebhookVerifyToken").And.NotContain("AppSecret");
    }

    [Fact]
    public void LiveRetailerCanBePreparedWithoutMetaConnectionDetails()
    {
        var input = new SaveWhatsAppConfigurationInput(WhatsAppProviderModes.Live, null, null, null,
            string.Empty, null, true, null, null, null);

        var act = () => WhatsAppService.ValidateInput(input, usesSharedPlatformCredentials: true);

        act.Should().NotThrow();
        WhatsAppService.ResolveSaveStatus(isLive: true, enabled: true, hasLiveConnection: false, remainsConnected: false)
            .Should().Be(WhatsAppConnectionStatuses.NotConfigured);
    }

    [Fact]
    public void LivePreparationCannotBeMarkedConnectedBeforeMetaValidation()
    {
        WhatsAppService.ResolveSaveStatus(isLive: true, enabled: true, hasLiveConnection: true, remainsConnected: false)
            .Should().Be(WhatsAppConnectionStatuses.Configured);
        WhatsAppService.ResolveSaveStatus(isLive: true, enabled: true, hasLiveConnection: false, remainsConnected: true)
            .Should().Be(WhatsAppConnectionStatuses.Connected);
        WhatsAppService.ResolveSaveStatus(isLive: true, enabled: false, hasLiveConnection: true, remainsConnected: true)
            .Should().Be(WhatsAppConnectionStatuses.Disabled);
    }

    [Fact]
    public void MetaTestStillRequiresManualConnectionDetailsWhileMockDoesNot()
    {
        var metaTest = new SaveWhatsAppConfigurationInput(WhatsAppProviderModes.MetaTest, null, null, null,
            string.Empty, null, true, null, null, null);
        var mock = metaTest with { ProviderMode = WhatsAppProviderModes.Mock };

        var metaTestAct = () => WhatsAppService.ValidateInput(metaTest, usesSharedPlatformCredentials: true);
        var mockAct = () => WhatsAppService.ValidateInput(mock, usesSharedPlatformCredentials: false);

        metaTestAct.Should().Throw<BusinessRuleException>();
        mockAct.Should().NotThrow();
        WhatsAppService.ResolveSaveStatus(isLive: false, enabled: true, hasLiveConnection: false, remainsConnected: false)
            .Should().Be(WhatsAppConnectionStatuses.Configured);
    }

    [Fact]
    public void ModeChangesDoNotReuseTestCredentialsAsLiveCredentials()
    {
        const string protectedTestToken = "protected-meta-test-token";

        WhatsAppService.ExistingAccessTokenForMode(WhatsAppProviderModes.Live, WhatsAppProviderModes.MetaTest, protectedTestToken)
            .Should().BeNull();
        WhatsAppService.ExistingAccessTokenForMode(WhatsAppProviderModes.Live, WhatsAppProviderModes.Live, protectedTestToken)
            .Should().Be(protectedTestToken);
        WhatsAppService.ExistingAccessTokenForMode(WhatsAppProviderModes.MetaTest, WhatsAppProviderModes.MetaTest, protectedTestToken)
            .Should().Be(protectedTestToken);
    }

    [Fact]
    public void SavingUnchangedEnabledLiveConnectionPreservesItsValidatedState()
    {
        WhatsAppService.IsExistingLiveConnectionUnchanged(
            WhatsAppProviderModes.Live, true, WhatsAppProviderModes.Live, true, WhatsAppConnectionStatuses.Connected,
            "waba", "waba", "phone", "phone", "protected-token", "protected-token").Should().BeTrue();

        WhatsAppService.IsExistingLiveConnectionUnchanged(
            WhatsAppProviderModes.Live, true, WhatsAppProviderModes.Live, true, WhatsAppConnectionStatuses.Connected,
            "new-waba", "waba", "phone", "phone", "protected-token", "protected-token").Should().BeFalse();
    }
}
