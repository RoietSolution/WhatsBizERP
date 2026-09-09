using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
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
}
