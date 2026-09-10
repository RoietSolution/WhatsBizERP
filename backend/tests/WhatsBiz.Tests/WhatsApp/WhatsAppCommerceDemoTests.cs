using System.Reflection;
using FluentAssertions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
using WhatsBiz.Application.Common.Interfaces;

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
    public void CommerceControllerSeparatesOwnerDemoFromTenantOperations()
    {
        var actions = typeof(WhatsAppCommerceController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any()).ToArray();
        actions.Should().OnlyContain(method => method.GetCustomAttributes<HasPermissionAttribute>().Count() == 1);
        actions.Where(method => method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
                .Any(attribute => attribute.Template?.Contains("administration/tenants/{tenantId:guid}", StringComparison.Ordinal) == true))
            .Should().OnlyContain(method => method.GetCustomAttributes<PlatformAuthorizeAttribute>().Count() == 1);
    }

    [Fact]
    public void RetailerCatalogueUsesAuthenticatedTenantAndRejectsCrossTenantTarget()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var retailer = new TestCurrentUser(tenantA, ["SystemAdministrator"]);

        WhatsAppCommerceService.ResolveAuthenticatedTenant(retailer, tenantA).Should().Be(tenantA);
        var crossTenant = () => WhatsAppCommerceService.ResolveAuthenticatedTenant(retailer, tenantB);
        crossTenant.Should().Throw<EntityNotFoundException>();
    }

    [Fact]
    public void OwnerCatalogueRequiresTheExplicitTargetPassedByPlatformEndpoint()
    {
        var target = Guid.NewGuid();
        var owner = new TestCurrentUser(null, ["ApplicationOwner"]);

        WhatsAppCommerceService.ResolveAuthenticatedTenant(owner, target).Should().Be(target);
    }

    [Fact]
    public void RetailerDemoEndpointCannotAcceptTenantOverrideAndPlatformEndpointRequiresOwnerAuthorization()
    {
        var retailerSetup = typeof(WhatsAppCommerceController).GetMethod(nameof(WhatsAppCommerceController.SetupForRetailer))!;
        retailerSetup.GetParameters().Should().NotContain(parameter => parameter.Name == "tenantId");
        retailerSetup.GetCustomAttributes<PlatformAuthorizeAttribute>().Should().BeEmpty();

        var ownerSetup = typeof(WhatsAppCommerceController).GetMethod(nameof(WhatsAppCommerceController.Setup))!;
        ownerSetup.GetParameters().Should().Contain(parameter => parameter.Name == "tenantId" && parameter.ParameterType == typeof(Guid));
        ownerSetup.GetCustomAttributes<PlatformAuthorizeAttribute>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("HELD", "Order Confirmed")]
    [InlineData("SUSPENDED", "Order Confirmed")]
    [InlineData("COMPLETED", "Completed")]
    [InlineData("VOID", "Cancelled")]
    public void CommerceStatusIsMappedFromActualErpStatus(string erpStatus, string expected) =>
        WhatsAppCommerceService.DisplayStatus(erpStatus).Should().Be(expected);

    [Theory]
    [InlineData("MOCK", true)]
    [InlineData("mock", true)]
    [InlineData("META_TEST", true)]
    [InlineData("meta_test", true)]
    [InlineData("LIVE", false)]
    [InlineData("", false)]
    public void DemoSupportsOnlyMockAndMetaTest(string mode, bool expected) =>
        WhatsAppCommerceService.DemoModeSupported(mode).Should().Be(expected);

    [Fact]
    public void MetaTestOrderConfirmationUsesActualOrderValues()
    {
        var message = WhatsAppCommerceService.OrderConfirmationText("WB-2048", 987.65m);

        message.Should().Contain("WB-2048").And.Contain("987.65").And.Contain("confirmed");
    }

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

    private sealed record TestCurrentUser(Guid? TenantId, IReadOnlyCollection<string> Roles) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "test";
        public string? Email => "test@example.com";
        public IReadOnlyCollection<string> Permissions => [];
    }
}
