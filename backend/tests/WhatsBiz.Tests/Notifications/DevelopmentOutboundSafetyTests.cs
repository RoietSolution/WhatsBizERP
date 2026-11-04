using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure;
using WhatsBiz.Infrastructure.DemoRequests;
using WhatsBiz.Infrastructure.Delivery;
using WhatsBiz.Infrastructure.Notifications;
using WhatsBiz.Infrastructure.Outbound;
using WhatsBiz.Infrastructure.Payments;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.Notifications;

public sealed class DevelopmentOutboundSafetyTests
{
    [Fact]
    public void PolicyBlocksDevelopmentAndLeavesOtherEnvironmentsEnabled()
    {
        var development = new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"));
        development.AllowsExternalDelivery.Should().BeFalse();
        Action blocked = () => development.EnsureExternalDeliveryAllowed("WhatsApp");
        blocked.Should().Throw<ExternalDeliveryBlockedException>().WithMessage("*blocked in Development*");

        foreach (var environment in new[] { "Test", "QA", "Production" })
            new DevelopmentOutboundSafetyPolicy(new TestEnvironment(environment)).AllowsExternalDelivery.Should().BeTrue();
    }

    [Fact]
    public void DevelopmentDoesNotQueueBlockedDeliveryNotificationsButPreservesMock()
    {
        var blocked = new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"));
        DeliveryService.ShouldCreateNotification("LIVE", blocked).Should().BeFalse();
        DeliveryService.ShouldCreateNotification("META_TEST", blocked).Should().BeFalse();
        DeliveryService.ShouldCreateNotification("MOCK", blocked).Should().BeTrue();
        DeliveryService.ShouldCreateNotification("LIVE", new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Production"))).Should().BeTrue();
    }

    [Fact]
    public async Task RegisteredDevelopmentHttpClientsBlockMetaRazorpaySmsAndOtpBeforeNetwork()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=WhatsBizERP;Trusted_Connection=True;TrustServerCertificate=True",
            ["Features:WhatsApp:Enabled"] = "false",
            ["Features:Sms:Enabled"] = "false",
            ["Jwt:Issuer"] = "WhatsBiz.Tests",
            ["Jwt:Audience"] = "WhatsBiz.Tests",
            ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
            ["Jwt:ExpiryMinutes"] = "30",
            ["Jwt:RefreshTokenExpiryDays"] = "1"
        }).Build();
        var environment = new TestEnvironment("Development");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddInfrastructure(configuration);
        services.AddWhatsAppIntegration(configuration);
        services.AddCustomerNotifications(configuration, environment);
        services.AddHttpClient("StorefrontOtp").AddHttpMessageHandler<DevelopmentOutboundBlockingHandler>();

        services.Any(x => x.ServiceType == typeof(IHostedService) &&
            x.ImplementationType?.Name is "CustomerNotificationWorker" or "StorefrontFeedbackNotificationWorker" or "StorefrontAutomatedNotificationWorker")
            .Should().BeFalse("Development must not register jobs that claim customer delivery work");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        foreach (var name in new[] { "MetaWhatsApp", "Razorpay", "CustomerNotifications", "StorefrontOtp" })
        {
            using var client = factory.CreateClient(name);
            var act = () => client.GetAsync("https://outbound-block-test.invalid");
            await act.Should().ThrowAsync<ExternalDeliveryBlockedException>();
        }
    }

    [Fact]
    public async Task DevelopmentBlocksRazorpayPaymentAndRefundBeforeNetwork()
    {
        var gateway = new RazorpayPaymentGateway(new GuardedClientFactory(
            new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"))));
        var config = new PaymentGatewayConfiguration(PaymentProviders.Razorpay, "synthetic-key", "synthetic-secret", "synthetic-webhook", true, null, null);

        var create = () => gateway.CreatePaymentAsync(config,
            new(Guid.NewGuid(), Guid.NewGuid(), "DEV-ORDER", 12m, "INR", "DEV-REF", null, null), default);
        await create.Should().ThrowAsync<ExternalDeliveryBlockedException>();

        var refund = () => gateway.CreateRefundAsync(config, "synthetic-payment", 12m, "INR", Guid.NewGuid(), Guid.NewGuid(), default);
        await refund.Should().ThrowAsync<ExternalDeliveryBlockedException>();
    }

    [Fact]
    public async Task PaymentAndRefundServicesRejectDevelopmentBeforeWritingAttemptState()
    {
        var policy = new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"));
        var configuration = new ConfigurationBuilder().Build();
        var payments = new CommercePaymentService(configuration, null!, null!, null!, null!, policy);
        var create = () => payments.CreateAttemptForTenantAsync(Guid.NewGuid(),
            new(Guid.NewGuid(), PaymentProviders.Razorpay), "test", default);
        await create.Should().ThrowAsync<ExternalDeliveryBlockedException>();

        var refunds = new CommerceRefundService(configuration, null!, null!, null!, policy);
        Func<Task> startRefund = () => refunds.StartRazorpayAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Func<Task> reconcileRefund = () => refunds.ReconcileRazorpayAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Func<Task> receiveRefundWebhook = () => refunds.ProcessRazorpayRefundWebhookAsync(ReadOnlyMemory<byte>.Empty, "unused", default);
        await startRefund.Should().ThrowAsync<ExternalDeliveryBlockedException>();
        await reconcileRefund.Should().ThrowAsync<ExternalDeliveryBlockedException>();
        await receiveRefundWebhook.Should().ThrowAsync<ExternalDeliveryBlockedException>();
    }

    [Fact]
    public async Task MetaProviderReportsBlockedSendAsUnsuccessfulAndMockCommerceStillWorks()
    {
        var policy = new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsApp:Meta:GraphBaseUrl"] = "https://graph.facebook.com"
        }).Build();
        var realProvider = new MetaCloudApiWhatsAppProvider(new GuardedClientFactory(policy), configuration,
            NullLogger<MetaCloudApiWhatsAppProvider>.Instance);
        realProvider.Supports("LIVE").Should().BeTrue();
        realProvider.Supports("META_TEST").Should().BeTrue();
        var result = await realProvider.SendTestMessageAsync(
            new("v23.0", "synthetic-phone-id", "synthetic-token", "+910000000000", "test"), default);
        result.Succeeded.Should().BeFalse();
        result.ProviderMessageId.Should().BeNull();

        var mock = new MockWhatsAppProvider();
        var mockResult = await mock.SendCommerceAsync(new("v1", "mock", "mock", "+910000000000",
            new("TEXT", "MOCK", "local test", null)), default);
        mockResult.Succeeded.Should().BeTrue();
        mockResult.ProviderMessageId.Should().StartWith("mock-commerce-");
    }

    [Fact]
    public async Task DevelopmentBlocksDirectSmtpBeforeOpeningAConnection()
    {
        var options = Options.Create(new DemoRequestOptions
        {
            Email = new SmtpOptions { Enabled = true, Host = "127.0.0.1", Port = 25, FromAddress = "dev@example.test" }
        });
        var sender = new SmtpDemoRequestEmailSender(options,
            new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development")));
        var send = () => sender.SendAsync(new("dev@example.test", "test", "test"), default);
        await send.Should().ThrowAsync<ExternalDeliveryBlockedException>();
    }

    [Fact]
    public async Task StorefrontEmailTestIsBlockedBeforeReadingOrSendingEmail()
    {
        var service = new WhatsBiz.Infrastructure.Storefront.StorefrontAdministrationService(null!, null!, null!, null!, null!,
            Options.Create(new StorefrontFeedbackNotificationOptions()), new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development")));
        Func<Task> send = () => service.SendNotificationTestEmailAsync(default);
        await send.Should().ThrowAsync<ExternalDeliveryBlockedException>();
    }

    [Fact]
    public async Task DevelopmentWebhookEndpointsAcknowledgeAndDiscardWithoutProcessing()
    {
        var policy = new DevelopmentOutboundSafetyPolicy(new TestEnvironment("Development"));
        var payments = new PaymentWebhooksController(null!, null!, policy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var paymentResult = await payments.Razorpay(default);
        paymentResult.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)paymentResult).Value!.GetType().GetProperty("ignored")!.GetValue(((OkObjectResult)paymentResult).Value).Should().Be(true);

        var whatsapp = new WhatsAppController(null!, null!, outboundSafety: policy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var webhookResult = await whatsapp.ReceiveWebhook(default);
        webhookResult.Should().BeOfType<OkObjectResult>();
        var verifyResult = await whatsapp.VerifyWebhook("subscribe", "unused", "unused", default);
        verifyResult.Should().BeOfType<NotFoundResult>();
    }

    private sealed class GuardedClientFactory(IOutboundSafetyPolicy policy) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new DevelopmentOutboundBlockingHandler(policy)
        {
            InnerHandler = new NoNetworkHandler()
        }) { BaseAddress = new Uri("https://api.razorpay.com/v1/") };
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The test attempted to reach the network.");
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "WhatsBiz.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class AllowTestOutboundSafetyPolicy : IOutboundSafetyPolicy
{
    public bool AllowsExternalDelivery => true;
    public void EnsureExternalDeliveryAllowed(string integration) { }
}
