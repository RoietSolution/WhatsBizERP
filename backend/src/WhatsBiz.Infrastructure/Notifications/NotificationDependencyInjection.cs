using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Notifications;

public static class NotificationDependencyInjection
{
    public static IServiceCollection AddCustomerNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeatureOptions>()
            .Bind(configuration.GetRequiredSection(FeatureOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient("CustomerNotifications", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<ICustomerNotificationService, CustomerNotificationService>();
        services.AddSingleton<ICustomerMessageProvider, WhatsAppProvider>();
        services.AddSingleton<ICustomerMessageProvider, SmsProvider>();
        services.AddHostedService<CustomerNotificationWorker>();
        return services;
    }
}
