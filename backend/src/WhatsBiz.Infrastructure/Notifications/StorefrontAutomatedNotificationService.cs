using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Notifications;

/// <summary>Writes post-commit storefront notification work. Delivery is performed by the hosted worker.</summary>
public sealed class StorefrontAutomatedNotificationService(IConfiguration configuration, IFeatureService? features = null) : IStorefrontAutomatedNotificationService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task QueueOrderEmailAsync(Guid tenantId, Guid orderId, CancellationToken token)
    {
        // This is the optional retailer alert, not the customer's transactional receipt.
        if (features is null || !await features.IsEnabledAsync(tenantId, FeatureKeys.StorefrontNotifications, token)) return;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var settings = new SqlCommand("SELECT NewOrderEmailNotificationsEnabled,NewOrderNotificationEmail FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant;", connection);
        settings.Parameters.AddWithValue("@tenant", tenantId);
        await using var reader = await settings.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return;
        var enabled = reader.GetBoolean(0);
        var recipient = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
        await reader.CloseAsync();
        var valid = enabled && !string.IsNullOrWhiteSpace(recipient) && MailAddress.TryCreate(recipient, out _);
        await using var insert = new SqlCommand("""
            INSERT commerce.StorefrontAutomatedNotificationDeliveries
                (DeliveryId,TenantId,EventType,Channel,EntityId,RecipientReference,TemplateKey,Status,NextAttemptAtUtc)
            SELECT @id,@tenant,N'STOREFRONT_ORDER_CREATED',N'EMAIL',@entity,@recipient,N'STOREFRONT_ORDER_CREATED',
                   CASE WHEN @valid=1 THEN N'PENDING' ELSE N'SKIPPED' END,
                   CASE WHEN @valid=1 THEN SYSUTCDATETIME() ELSE NULL END
            WHERE NOT EXISTS (SELECT 1 FROM commerce.StorefrontAutomatedNotificationDeliveries WITH (UPDLOCK,HOLDLOCK)
                              WHERE TenantId=@tenant AND EventType=N'STOREFRONT_ORDER_CREATED' AND Channel=N'EMAIL' AND EntityId=@entity);
            """, connection);
        insert.Parameters.AddWithValue("@id", Guid.NewGuid()); insert.Parameters.AddWithValue("@tenant", tenantId);
        insert.Parameters.AddWithValue("@entity", orderId); insert.Parameters.AddWithValue("@recipient", (object?)recipient ?? DBNull.Value);
        insert.Parameters.AddWithValue("@valid", valid); await insert.ExecuteNonQueryAsync(token);
    }
}
