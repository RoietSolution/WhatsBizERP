namespace WhatsBiz.Application.Features.Loyalty;

public sealed record CoinConfigurationDto(bool IsEnabled, decimal PurchaseAmount, int PurchaseCoins,
    string EarningPriority, string AwardOrderStatus, int RedemptionCoins, decimal RedemptionValue,
    int MinimumRedemptionCoins, int? MaximumRedemptionCoins, bool AllowWithOtherDiscounts,
    bool RestoreRedeemedOnCancel, bool RestoreRedeemedOnRefund,
    IReadOnlyCollection<ProductCoinRuleDto> ProductRules, IReadOnlyCollection<CategoryCoinRuleDto> CategoryRules);
public sealed record ProductCoinRuleDto(Guid ProductId, string ProductCode, string ProductName, bool IsEnabled, int CoinsPerUnit);
public sealed record CategoryCoinRuleDto(Guid ProductCategoryId, string CategoryCode, string CategoryName, bool IsEnabled, int CoinsPerUnit);
public sealed record CoinConfigurationInput(bool IsEnabled, decimal PurchaseAmount, int PurchaseCoins,
    string EarningPriority, string AwardOrderStatus, int RedemptionCoins, decimal RedemptionValue,
    int MinimumRedemptionCoins, int? MaximumRedemptionCoins, bool AllowWithOtherDiscounts,
    bool RestoreRedeemedOnCancel, bool RestoreRedeemedOnRefund,
    IReadOnlyCollection<ProductCoinRuleInput> ProductRules, IReadOnlyCollection<CategoryCoinRuleInput> CategoryRules);
public sealed record ProductCoinRuleInput(Guid ProductId, bool IsEnabled, int CoinsPerUnit);
public sealed record CategoryCoinRuleInput(Guid ProductCategoryId, bool IsEnabled, int CoinsPerUnit);
public sealed record CoinTransactionDto(Guid CoinTransactionId, string TransactionType, int Coins,
    string SourceType, Guid? SourceId, decimal? RupeeValue, string? Description, DateTimeOffset CreatedOn);
public sealed record CoinWalletDto(Guid CustomerId, int AvailableCoins, int TotalEarned, int TotalRedeemed,
    IReadOnlyCollection<CoinTransactionDto> Transactions);
public sealed record CoinRedemptionQuote(int RequestedCoins, decimal Discount, int BalanceAfterRedemption);

public interface ILoyaltyService
{
    Task<CoinConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token);
    Task<CoinConfigurationDto> SaveConfigurationAsync(Guid tenantId, CoinConfigurationInput input, string? actor, CancellationToken token);
    Task<CoinWalletDto> GetWalletAsync(Guid tenantId, Guid customerId, int take, CancellationToken token);
    Task<CoinRedemptionQuote> QuoteRedemptionAsync(Guid tenantId, Guid customerId, int coins, decimal otherDiscount, CancellationToken token);
    Task RedeemAsync(Guid tenantId, Guid customerId, Guid orderId, int coins, decimal otherDiscount, string? actor, CancellationToken token);
    Task ProcessOrderAsync(Guid tenantId, Guid orderId, string status, string? actor, CancellationToken token);
}
