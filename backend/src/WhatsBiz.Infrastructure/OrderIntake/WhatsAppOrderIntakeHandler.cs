using WhatsBiz.Application.Features.OrderIntake;

namespace WhatsBiz.Infrastructure.OrderIntake;

public sealed class WhatsAppOrderIntakeHandler(IOrderIntakeService intake) : IWhatsAppOrderIntakeHandler
{
    public Task<bool> TryCaptureAsync(Guid tenantId, string phoneNumberId, string sender, string messageId,
        string? profileName, string? text, CancellationToken token) => string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(false)
            : intake.CreateWhatsAppAsync(tenantId, phoneNumberId, sender, messageId, profileName, text, token);
}
