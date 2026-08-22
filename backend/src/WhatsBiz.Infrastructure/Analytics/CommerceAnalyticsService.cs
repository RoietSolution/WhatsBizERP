using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Analytics;

public sealed class CommerceAnalyticsService(IConfiguration configuration) : ICommerceAnalyticsService
{
    private static readonly HashSet<string> EventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHATSAPP_CONVERSATION_STARTED", "PRODUCT_SEARCH", "PRODUCT_SEARCH_NO_MATCH", "PRODUCT_SEARCH_CLARIFICATION",
        "PRODUCT_RESULTS_RETURNED", "COLLECTION_SEARCH", "COLLECTION_VIEWED", "COLLECTION_SENT", "PRODUCT_VIEWED",
        "ADD_TO_CART", "CHECKOUT_STARTED", "ORDER_CREATED", "PAYMENT_SUCCESS"
    };

    public async Task RecordAsync(Guid tenantId, CommerceAnalyticsEventInput input, CancellationToken token)
    {
        if (!EventTypes.Contains(input.EventType)) return;
        try
        {
            string? metadata = null;
            if (input.Metadata.HasValue)
            {
                using var document = JsonDocument.Parse(input.Metadata.Value.GetRawText());
                metadata = JsonSerializer.Serialize(document.RootElement);
                if (metadata.Length > 4000) metadata = metadata[..4000];
            }
            await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync(token);
            await using var command = new SqlCommand(@"INSERT commerce.AnalyticsEvents
                (AnalyticsEventId,TenantId,EventType,CustomerId,ConversationId,ProductId,VariantId,CollectionId,MetadataJson,CreatedOn)
                VALUES(@id,@tenant,@type,@customer,@conversation,@product,@variant,@collection,@metadata,SYSUTCDATETIME());", connection);
            command.Parameters.AddWithValue("@id", Guid.NewGuid());
            command.Parameters.AddWithValue("@tenant", tenantId);
            command.Parameters.AddWithValue("@type", input.EventType.ToUpperInvariant());
            command.Parameters.AddWithValue("@customer", (object?)input.CustomerId ?? DBNull.Value);
            command.Parameters.AddWithValue("@conversation", (object?)input.ConversationId ?? DBNull.Value);
            command.Parameters.AddWithValue("@product", (object?)input.ProductId ?? DBNull.Value);
            command.Parameters.AddWithValue("@variant", (object?)input.VariantId ?? DBNull.Value);
            command.Parameters.AddWithValue("@collection", (object?)input.CollectionId ?? DBNull.Value);
            command.Parameters.AddWithValue("@metadata", (object?)metadata ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(token);
        }
        catch (SqlException) { }
    }
}
