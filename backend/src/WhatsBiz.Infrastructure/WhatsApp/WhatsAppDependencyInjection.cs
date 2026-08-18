using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.POS;

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
        services.AddScoped<IWhatsAppCommerceService, WhatsAppCommerceService>();
        services.AddScoped<IPOSLifecycleService, POSLifecycleService>();
        services.AddSingleton<IWhatsAppCommerceProvider, MockWhatsAppProvider>();
        services.AddSingleton<IWhatsAppCommerceProvider, MetaCloudApiWhatsAppProvider>();
        services.AddSingleton<IWhatsAppCommerceProviderResolver, WhatsAppCommerceProviderResolver>();
        services.AddHttpClient("MetaWhatsApp", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WhatsBizERP/2.0");
        });
        return services;
    }
}
