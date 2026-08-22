using System.Reflection;
using FluentAssertions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppCommerceDemoTests
{
    [Fact]
    public async Task MockProviderGeneratesConfirmationFromActualOrderValues()
    {
        var messages = await new MockWhatsAppProvider().SendOrderConfirmationAsync("WB-0042", 1250.50m, default);
        messages.Should().ContainSingle();
        messages.Single().Text.Should().Contain("WB-0042").And.Contain("1250.50");
    }

    [Fact]
    public void ResolverSelectsMockWithoutWorkflowConditionals()
    {
        var mock = new MockWhatsAppProvider();
        var resolver = new WhatsAppCommerceProviderResolver([mock]);
        resolver.Resolve("mock").Should().BeSameAs(mock);
        var action = () => resolver.Resolve("LIVE");
        action.Should().Throw<BusinessRuleException>().WithMessage("*not implemented*");
    }

    [Fact]
    public void CommerceControllerRequiresFeatureAndEachEndpointRequiresPermission()
    {
        typeof(WhatsAppCommerceController).GetCustomAttributes<RequireFeatureAttribute>().Single().Policy
            .Should().Be(PermissionPolicyProvider.FeaturePrefix + FeatureKeys.WhatsAppCommerce);
        foreach (var method in typeof(WhatsAppCommerceController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(x => x.Name != "TenantId"))
            method.GetCustomAttributes<HasPermissionAttribute>().Should().ContainSingle(method.Name);
    }

    [Theory]
    [InlineData("HELD", "Order Confirmed")]
    [InlineData("SUSPENDED", "Order Confirmed")]
    [InlineData("COMPLETED", "Completed")]
    [InlineData("VOID", "Cancelled")]
    public void CommerceStatusIsMappedFromActualErpStatus(string erpStatus, string expected) =>
        WhatsAppCommerceService.DisplayStatus(erpStatus).Should().Be(expected);

    [Fact]
    public async Task CompletedNotificationUsesCustomerFriendlyActualOrderMessage()
    {
        var messages = await new MockWhatsAppProvider().SendOrderStatusAsync("WB-1024", "COMPLETED", default);
        messages.Single().Text.Should().Contain("WB-1024").And.Contain("completed successfully");
    }

    [Fact]
    public async Task MockProviderSendsCollectionUsingRealProductValues()
    {
        var result = await new MockWhatsAppProvider().SendProductCollectionAsync(new("", "", "", "919999999999", "Wedding Collection", [
            new(Guid.NewGuid(), "Banarasi Silk Saree", "SKU-1", 1299m, null, null, null)], false), default);
        result.Succeeded.Should().BeTrue();
        result.NativeUsed.Should().BeFalse();
        result.ProductsSent.Should().Be(1);
        result.SafeMessage.Should().Contain("Banarasi Silk Saree").And.Contain("1299.00");
    }
}
