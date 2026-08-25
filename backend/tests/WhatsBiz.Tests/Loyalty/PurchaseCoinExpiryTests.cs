using FluentAssertions;
using WhatsBiz.Application.Features.Loyalty;

namespace WhatsBiz.Tests.Loyalty;

public sealed class PurchaseCoinExpiryTests
{
    [Fact]
    public void ConfigurationExposesPurchaseCoinValidity()
    {
        typeof(CoinConfigurationDto).GetProperty(nameof(CoinConfigurationDto.PurchaseCoinValidityDays)).Should().NotBeNull();
        typeof(CoinConfigurationInput).GetProperty(nameof(CoinConfigurationInput.PurchaseCoinValidityDays)).Should().NotBeNull();
    }

    [Fact]
    public void MigrationCreatesExpiringPurchaseLotsAndRestoresConsumedLots()
    {
        var sql=File.ReadAllText(Find("database","WhatsBiz.Database","Scripts","V16-PurchaseCoinExpiry.sql"));
        sql.Should().Contain("PurchaseCoinValidityDays").And.Contain("N'PURCHASE_LOYALTY'");
        sql.Should().Contain("INSERT loyalty.CustomerRewardLots").And.Contain("DATEADD(day,@validity");
        sql.Should().Contain("CustomerRewardConsumptions").And.Contain("RemainingCoins=lot.RemainingCoins+c.Coins");
        sql.Should().Contain("IF @remaining>0 THROW 51138");
    }

    [Fact]
    public void ExpirationWorkerIsRegisteredAndWalletAccessExpiresDueLots()
    {
        File.ReadAllText(Find("backend","src","WhatsBiz.Api","Program.cs")).Should().Contain("AddHostedService<RewardCoinExpirationWorker>");
        File.ReadAllText(Find("backend","src","WhatsBiz.Infrastructure","Loyalty","LoyaltyService.cs")).Should().Contain("LOYALTY_ACCESS").And.Contain("LOYALTY_REDEMPTION");
    }

    private static string Find(params string[] parts)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"backend")))directory=directory.Parent;
        return Path.Combine([directory?.FullName??throw new DirectoryNotFoundException(),..parts]);
    }
}
