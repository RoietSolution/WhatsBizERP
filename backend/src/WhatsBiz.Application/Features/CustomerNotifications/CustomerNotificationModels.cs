namespace WhatsBiz.Application.Features.CustomerNotifications;

public static class CustomerNotificationEvents
{
    public const string SuccessfulSale = "SUCCESSFUL_SALE";
    public const string SuccessfulPayment = "SUCCESSFUL_PAYMENT";
}

public sealed record CustomerNotificationSettingsDto(
    bool Enabled, bool WhatsAppEnabled, bool SmsEnabled,
    bool SuccessfulSale, bool SuccessfulPayment,
    string WhatsAppTemplate, string SmsTemplate);

public sealed record CustomerNotificationSettingsInput(
    bool Enabled, bool WhatsAppEnabled, bool SmsEnabled,
    bool SuccessfulSale, bool SuccessfulPayment,
    string WhatsAppTemplate, string SmsTemplate);

public sealed record CustomerNotificationHistoryDto(
    Guid Id, DateTimeOffset CreatedOn, string CustomerName, string InvoiceNumber,
    string EventType, string Channel, string Recipient, string Status,
    int AttemptCount, string? ErrorMessage, DateTimeOffset? SentOn,
    DateTimeOffset? LastAttemptOn);

public sealed record NotificationConfigurationStatusDto(
    bool WhatsAppConfigured, bool SmsConfigured, string Message);
