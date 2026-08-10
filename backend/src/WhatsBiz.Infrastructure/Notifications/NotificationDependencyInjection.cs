using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Notifications;

public static class NotificationDependencyInjection
{
    public static IServiceCollection AddCustomerNotifications(this IServiceCollection services)
    {
        services.AddHttpClient("CustomerNotifications", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<ICustomerNotificationService, CustomerNotificationService>();
        services.AddSingleton<ICustomerMessageProvider, WhatsAppProvider>();
        services.AddSingleton<ICustomerMessageProvider, SmsProvider>();
        services.AddHostedService<CustomerNotificationWorker>();
        return services;
    }
}
