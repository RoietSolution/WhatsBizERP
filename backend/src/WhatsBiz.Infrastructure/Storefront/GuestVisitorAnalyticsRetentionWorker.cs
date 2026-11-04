using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class GuestVisitorAnalyticsRetentionWorker(
    IConfiguration configuration,
    GuestVisitorAnalyticsRetentionHealth health,
    ILogger<GuestVisitorAnalyticsRetentionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, long, long, Exception?> BatchCompleted =
        LoggerMessage.Define<int, long, long>(LogLevel.Information, new EventId(6201, nameof(BatchCompleted)),
            "Guest analytics retention batch completed. Sessions removed: {Sessions}; events removed: {Events}; search events removed: {SearchEvents}.");
    private static readonly Action<ILogger, Exception?> BatchFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(6202, nameof(BatchFailed)),
            "Guest analytics retention batch failed; it will retry on the next scheduled run.");

    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(configuration.GetValue("GuestAnalytics:CleanupIntervalMinutes", 60), 5, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!configuration.GetValue<bool>("GuestAnalytics:RetentionCleanupEnabled"))
            {
                health.MarkUnhealthy();
            }
            else
            {
                try
                {
                    var batchSize = Math.Clamp(configuration.GetValue("GuestAnalytics:CleanupBatchSize", 500), 1, 5000);
                    var (sessions, events, searches) = await RunRetentionBatchAsync(batchSize, stoppingToken);
                    BatchCompleted(logger, sessions, events, searches, null);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    health.MarkUnhealthy();
                    BatchFailed(logger, exception);
                }
            }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<(int Sessions, long Events, long Searches)> RunRetentionBatchAsync(int batchSize, CancellationToken token, Guid? tenantScope = null)
    {
        if (!configuration.GetValue<bool>("GuestAnalytics:RetentionCleanupEnabled"))
        {
            health.MarkUnhealthy();
            throw new InvalidOperationException("Guest analytics retention cleanup is disabled.");
        }

        try
        {
            var result = await DeleteExpiredBatchAsync(batchSize, tenantScope, token);
            health.MarkHealthy();
            return result;
        }
        catch
        {
            health.MarkUnhealthy();
            throw;
        }
    }

    private async Task<(int Sessions, long Events, long Searches)> DeleteExpiredBatchAsync(int batchSize, Guid? tenantScope, CancellationToken token)
    {
        batchSize = Math.Clamp(batchSize, 1, 5000);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await using var command = new SqlCommand("""
SET NOCOUNT ON;
SET XACT_ABORT ON;
CREATE TABLE #expired(TenantId uniqueidentifier NOT NULL,SessionId uniqueidentifier NOT NULL,PRIMARY KEY(TenantId,SessionId));
INSERT #expired(TenantId,SessionId)
SELECT TOP(@batch) s.TenantId,s.SessionId
FROM commerce.GuestVisitorAnalyticsSessions s WITH(UPDLOCK,READPAST,ROWLOCK)
JOIN commerce.GuestVisitorAnalyticsPolicies p ON p.TenantId=s.TenantId
WHERE p.RetentionDays BETWEEN 1 AND 365 AND p.PolicyReviewedAtUtc IS NOT NULL AND p.PolicyReviewedBy IS NOT NULL
  AND (@tenantScope IS NULL OR s.TenantId=@tenantScope)
  AND s.LastActivityAtUtc<DATEADD(day,-p.RetentionDays,SYSUTCDATETIME())
ORDER BY s.LastActivityAtUtc,s.TenantId,s.SessionId;

DECLARE @eventCount bigint=(SELECT COUNT_BIG(1) FROM commerce.AnalyticsEvents e JOIN #expired x ON x.TenantId=e.TenantId AND x.SessionId=e.GuestSessionId);
DECLARE @searchCount bigint=(SELECT COUNT_BIG(1) FROM commerce.ProductSearchEvents e JOIN #expired x ON x.TenantId=e.TenantId AND x.SessionId=e.GuestSessionId);
UPDATE requestRow SET SearchEventId=NULL
FROM commerce.MissingProductRequests requestRow
JOIN commerce.ProductSearchEvents searchEvent ON searchEvent.SearchEventId=requestRow.SearchEventId AND searchEvent.TenantId=requestRow.TenantId
JOIN #expired x ON x.TenantId=searchEvent.TenantId AND x.SessionId=searchEvent.GuestSessionId;
DELETE s FROM commerce.GuestVisitorAnalyticsSessions s JOIN #expired x ON x.TenantId=s.TenantId AND x.SessionId=s.SessionId;
DECLARE @sessionCount int=@@ROWCOUNT;
SELECT @sessionCount,@eventCount,@searchCount;
""", connection, (SqlTransaction)transaction);
            command.CommandTimeout = 120;
            command.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;
            command.Parameters.Add("@tenantScope", SqlDbType.UniqueIdentifier).Value = (object?)tenantScope ?? DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Retention cleanup returned no batch result.");
            var result = (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2));
            await reader.CloseAsync();
            await transaction.CommitAsync(token);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
