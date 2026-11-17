using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed partial class MarketplaceReservationExpiryWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<MarketplaceReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Marketplace:ReservationExpiryWorkerEnabled", false)) return;
        var minutes = Math.Clamp(configuration.GetValue("Marketplace:ReservationExpiryIntervalMinutes", 15), 5, 1440);
        var batchSize = Math.Clamp(configuration.GetValue("Marketplace:ReservationExpiryBatchSize", 100), 1, 500);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMarketplaceOrderLifecycleService>();
                var count = await service.ExpireDueAcrossTenantsAsync(batchSize, "MARKETPLACE_EXPIRATION_WORKER", stoppingToken);
                if (count > 0) MarketplaceReservationExpiryLogs.Expired(logger, count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { MarketplaceReservationExpiryLogs.Failed(logger, exception); }
        }
    }
}

internal static partial class MarketplaceReservationExpiryLogs
{
    [LoggerMessage(8701, LogLevel.Information, "Expired {Count} marketplace orders and released their reservations.")]
    public static partial void Expired(ILogger logger, int count);

    [LoggerMessage(8702, LogLevel.Error, "Marketplace reservation expiry batch failed.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
