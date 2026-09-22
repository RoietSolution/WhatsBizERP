using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.POS;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Infrastructure.Payments;

namespace WhatsBiz.Infrastructure.WhatsApp;

public static class WhatsAppDependencyInjection
{
    public static IServiceCollection AddWhatsAppIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(configuration["Security:DataProtection:ApplicationName"] ?? "WhatsBizERP");
        var keyRingPath = configuration["Security:DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddScoped<IWhatsAppService, WhatsAppService>();
        services.AddScoped<IWhatsAppUsageBillingService, WhatsAppUsageBillingService>();
        services.AddScoped<IWhatsAppCommerceService, WhatsAppCommerceService>();
        services.AddScoped<IWhatsAppInboundCommerceHandler, WhatsAppInboundCommerceHandler>();
        services.AddScoped<IPOSLifecycleService, POSLifecycleService>();
        services.AddScoped<ICommercePaymentService, CommercePaymentService>();
        services.AddSingleton<IPaymentGateway, RazorpayPaymentGateway>();
        services.AddSingleton<IPaymentGateway, DirectUpiPaymentGateway>();
        services.AddSingleton<IPaymentGateway, CashOnDeliveryPaymentGateway>();
        services.AddSingleton<IPaymentGatewayResolver, PaymentGatewayResolver>();
        services.AddSingleton<IWhatsAppCommerceProvider, MockWhatsAppProvider>();
        services.AddSingleton<IWhatsAppCommerceProvider, MetaCloudApiWhatsAppProvider>();
        services.AddSingleton<IWhatsAppCommerceProviderResolver, WhatsAppCommerceProviderResolver>();
        services.AddHttpClient("MetaWhatsApp", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WhatsBizERP/2.0");
        })
        // The OAuth code exchange necessarily puts app credentials and the
        // one-time authorization code in the Graph request URI. Suppress the
        // factory's default request-URI logging for this named client.
        .RemoveAllLoggers();
        services.AddHttpClient("Razorpay", client =>
        {
            client.BaseAddress = new Uri("https://api.razorpay.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WhatsBizERP/2.0");
        }).RemoveAllLoggers();
        return services;
    }
}
