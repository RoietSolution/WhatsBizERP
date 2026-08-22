using System.Text.Json;

namespace WhatsBiz.Application.Common.Interfaces;

public sealed record CommerceAnalyticsEventInput(string EventType, Guid? CustomerId, Guid? ConversationId,
    Guid? ProductId, Guid? VariantId, Guid? CollectionId, JsonElement? Metadata);

public interface ICommerceAnalyticsService
{
    Task RecordAsync(Guid tenantId, CommerceAnalyticsEventInput input, CancellationToken token);
}
