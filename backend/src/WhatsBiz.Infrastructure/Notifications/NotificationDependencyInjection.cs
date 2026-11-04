using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using WhatsBiz.Infrastructure.Outbound;

namespace WhatsBiz.Infrastructure.Notifications;

public static class NotificationDependencyInjection
{
    public static IServiceCollection AddCustomerNotifications(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<FeatureOptions>()
            .Bind(configuration.GetRequiredSection(FeatureOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient("CustomerNotifications", client => client.Timeout = TimeSpan.FromSeconds(15))
            .AddHttpMessageHandler<DevelopmentOutboundBlockingHandler>();
        services.AddScoped<ICustomerNotificationService, CustomerNotificationService>();
        services.AddSingleton<ICustomerMessageProvider, WhatsAppProvider>();
        services.AddSingleton<ICustomerMessageProvider, SmsProvider>();
        services.AddOptions<StorefrontFeedbackNotificationOptions>()
            .Bind(configuration.GetSection(StorefrontFeedbackNotificationOptions.SectionName));
        if (!environment.IsDevelopment())
        {
            services.AddHostedService<CustomerNotificationWorker>();
            services.AddHostedService<StorefrontFeedbackNotificationWorker>();
            services.AddHostedService<StorefrontAutomatedNotificationWorker>();
        }
        return services;
    }
}
