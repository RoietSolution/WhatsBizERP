using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Features.Referrals;

namespace WhatsBiz.Infrastructure.Loyalty;

public sealed class RewardCoinExpirationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<RewardCoinExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(configuration.GetValue("Loyalty:ExpirationIntervalMinutes", 60), 5, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICustomerReferralService>();
                var count = await service.ExpireAsync(null, 5000, "EXPIRATION_WORKER", stoppingToken);
                if (count > 0) RewardExpirationLogs.Expired(logger, count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                RewardExpirationLogs.Failed(logger, exception);
            }
        }
    }
}

internal static partial class RewardExpirationLogs
{
    [LoggerMessage(5101,LogLevel.Information,"Expired {Count} customer reward coin lots.")]
    public static partial void Expired(ILogger logger,int count);
    [LoggerMessage(5102,LogLevel.Error,"Customer reward coin expiration failed.")]
    public static partial void Failed(ILogger logger,Exception exception);
}
