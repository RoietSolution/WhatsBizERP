using WhatsBiz.Application.Features.CustomerNotifications;

namespace WhatsBiz.Application.Common.Interfaces;

public interface ICustomerNotificationService
{
    Task QueueInvoice(Guid invoiceId, string eventType, CancellationToken token);
    Task<CustomerNotificationSettingsDto> GetSettings(CancellationToken token);
    Task SaveSettings(CustomerNotificationSettingsInput input, string? user, CancellationToken token);
    Task<IReadOnlyCollection<CustomerNotificationHistoryDto>> History(int take, CancellationToken token);
    Task Retry(Guid notificationId, string? user, CancellationToken token);
    Task<NotificationConfigurationStatusDto> ConfigurationStatus(CancellationToken token);
}
