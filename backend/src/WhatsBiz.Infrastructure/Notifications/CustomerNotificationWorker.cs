using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WhatsBiz.Infrastructure.Notifications;

internal sealed class CustomerNotificationWorker(
    IConfiguration configuration,
    IEnumerable<ICustomerMessageProvider> providers,
    ILogger<CustomerNotificationWorker> logger) : BackgroundService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessOne(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                NotificationLogs.WorkerFailed(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOne(CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var claim = new SqlCommand("""
            ;WITH candidate AS
            (
                SELECT TOP(1) * FROM integration.CustomerNotifications WITH(UPDLOCK,READPAST,ROWLOCK)
                WHERE (Status=N'PENDING' AND NextAttemptOn<=SYSUTCDATETIME())
                   OR (Status=N'PROCESSING' AND LastAttemptOn<DATEADD(minute,-5,SYSUTCDATETIME()))
                ORDER BY CreatedOn
            )
            UPDATE candidate SET Status=N'PROCESSING',AttemptCount=AttemptCount+1,LastAttemptOn=SYSUTCDATETIME()
            OUTPUT inserted.CustomerNotificationId,inserted.Channel,inserted.Recipient,inserted.Message,inserted.AttemptCount;
            """, connection, transaction);
        await using var reader = await claim.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { await reader.CloseAsync(); await transaction.CommitAsync(token); return false; }
        var item = new WorkItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
        await reader.CloseAsync(); await transaction.CommitAsync(token);

        var provider = providers.SingleOrDefault(x => string.Equals(x.Channel, item.Channel, StringComparison.OrdinalIgnoreCase));
        var result = provider is null ? new ProviderResult(false, null, "NOT_CONFIGURED: no provider is registered.") : await provider.Send(item.Recipient, item.Message, item.Id, token);
        await using var update = new SqlCommand("""
            UPDATE integration.CustomerNotifications SET
              Status=CASE WHEN @success=1 THEN N'SENT' WHEN AttemptCount>=3 THEN N'FAILED' ELSE N'PENDING' END,
              ProviderMessageId=@providerId,ErrorMessage=@error,
              SentOn=CASE WHEN @success=1 THEN SYSUTCDATETIME() ELSE NULL END,
              NextAttemptOn=CASE WHEN @success=1 OR AttemptCount>=3 THEN NULL ELSE DATEADD(minute,CASE AttemptCount WHEN 1 THEN 1 ELSE 5 END,SYSUTCDATETIME()) END
            WHERE CustomerNotificationId=@id AND Status=N'PROCESSING';
            """, connection);
        update.Parameters.AddWithValue("@id", item.Id); update.Parameters.AddWithValue("@success", result.Succeeded); update.Parameters.AddWithValue("@providerId", result.ProviderMessageId ?? (object)DBNull.Value); update.Parameters.AddWithValue("@error", result.ErrorMessage ?? (object)DBNull.Value);
        await update.ExecuteNonQueryAsync(token); return true;
    }
    private sealed record WorkItem(Guid Id, string Channel, string Recipient, string Message, int AttemptCount);
}
