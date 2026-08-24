using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Loyalty;

namespace WhatsBiz.Infrastructure.Loyalty;

public sealed class LoyaltyService(IConfiguration configuration) : ILoyaltyService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<CoinConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var products = new List<ProductCoinRuleDto>();
        await using (var command = new SqlCommand("SELECT p.ProductId,p.ProductCode,p.ProductName,r.IsEnabled,r.CoinsPerUnit FROM loyalty.ProductCoinRules r JOIN master.Products p ON p.ProductId=r.ProductId WHERE r.TenantId=@tenant ORDER BY p.ProductName;", connection))
        { command.Parameters.AddWithValue("@tenant",tenantId); await using var reader=await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)) products.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetInt32(4))); }
        var categories = new List<CategoryCoinRuleDto>();
        await using (var command = new SqlCommand("SELECT c.ProductCategoryId,c.CategoryCode,c.CategoryName,r.IsEnabled,r.CoinsPerUnit FROM loyalty.CategoryCoinRules r JOIN master.ProductCategories c ON c.ProductCategoryId=r.ProductCategoryId WHERE r.TenantId=@tenant ORDER BY c.CategoryName;", connection))
        { command.Parameters.AddWithValue("@tenant",tenantId); await using var reader=await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)) categories.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3),reader.GetInt32(4))); }
        await using var config = new SqlCommand("SELECT IsEnabled,PurchaseAmount,PurchaseCoins,EarningPriority,AwardOrderStatus,RedemptionCoins,RedemptionValue,MinimumRedemptionCoins,MaximumRedemptionCoins,AllowWithOtherDiscounts,RestoreRedeemedOnCancel,RestoreRedeemedOnRefund FROM loyalty.CoinConfigurations WHERE TenantId=@tenant;", connection);
        config.Parameters.AddWithValue("@tenant",tenantId); await using var row=await config.ExecuteReaderAsync(token);
        if(!await row.ReadAsync(token)) return new(false,100,1,"PRODUCT_FIRST","DELIVERED",100,10,100,null,false,true,true,products,categories);
        return new(row.GetBoolean(0),row.GetDecimal(1),row.GetInt32(2),row.GetString(3),row.GetString(4),row.GetInt32(5),row.GetDecimal(6),row.GetInt32(7),row.IsDBNull(8)?null:row.GetInt32(8),row.GetBoolean(9),row.GetBoolean(10),row.GetBoolean(11),products,categories);
    }

    public async Task<CoinConfigurationDto> SaveConfigurationAsync(Guid tenantId, CoinConfigurationInput input, string? actor, CancellationToken token)
    {
        Validate(input);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await using (var command=new SqlCommand("""
MERGE loyalty.CoinConfigurations AS t USING(SELECT @tenant TenantId) s ON t.TenantId=s.TenantId
WHEN MATCHED THEN UPDATE SET IsEnabled=@enabled,PurchaseAmount=@amount,PurchaseCoins=@coins,EarningPriority=@priority,AwardOrderStatus=@status,RedemptionCoins=@redeemCoins,RedemptionValue=@redeemValue,MinimumRedemptionCoins=@minimum,MaximumRedemptionCoins=@maximum,AllowWithOtherDiscounts=@combine,RestoreRedeemedOnCancel=@cancel,RestoreRedeemedOnRefund=@refund,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor
WHEN NOT MATCHED THEN INSERT(TenantId,IsEnabled,PurchaseAmount,PurchaseCoins,EarningPriority,AwardOrderStatus,RedemptionCoins,RedemptionValue,MinimumRedemptionCoins,MaximumRedemptionCoins,AllowWithOtherDiscounts,RestoreRedeemedOnCancel,RestoreRedeemedOnRefund,CreatedBy) VALUES(@tenant,@enabled,@amount,@coins,@priority,@status,@redeemCoins,@redeemValue,@minimum,@maximum,@combine,@cancel,@refund,@actor);
""",connection,tx))
            { Add(command,"@tenant",tenantId);Add(command,"@enabled",input.IsEnabled);Add(command,"@amount",input.PurchaseAmount);Add(command,"@coins",input.PurchaseCoins);Add(command,"@priority",input.EarningPriority);Add(command,"@status",input.AwardOrderStatus);Add(command,"@redeemCoins",input.RedemptionCoins);Add(command,"@redeemValue",input.RedemptionValue);Add(command,"@minimum",input.MinimumRedemptionCoins);Add(command,"@maximum",input.MaximumRedemptionCoins);Add(command,"@combine",input.AllowWithOtherDiscounts);Add(command,"@cancel",input.RestoreRedeemedOnCancel);Add(command,"@refund",input.RestoreRedeemedOnRefund);Add(command,"@actor",actor);await command.ExecuteNonQueryAsync(token);}
            await Execute(connection,tx,"DELETE loyalty.ProductCoinRules WHERE TenantId=@tenant; DELETE loyalty.CategoryCoinRules WHERE TenantId=@tenant;",tenantId,token);
            foreach(var rule in input.ProductRules)
            { await using var command=new SqlCommand("INSERT loyalty.ProductCoinRules(TenantId,ProductId,IsEnabled,CoinsPerUnit,CreatedBy) SELECT @tenant,@id,@enabled,@coins,@actor WHERE EXISTS(SELECT 1 FROM master.Products WHERE ProductId=@id AND IsDeleted=0);",connection,tx);Add(command,"@tenant",tenantId);Add(command,"@id",rule.ProductId);Add(command,"@enabled",rule.IsEnabled);Add(command,"@coins",rule.CoinsPerUnit);Add(command,"@actor",actor);if(await command.ExecuteNonQueryAsync(token)!=1)throw new BusinessRuleException("A configured product was not found."); }
            foreach(var rule in input.CategoryRules)
            { await using var command=new SqlCommand("INSERT loyalty.CategoryCoinRules(TenantId,ProductCategoryId,IsEnabled,CoinsPerUnit,CreatedBy) SELECT @tenant,@id,@enabled,@coins,@actor WHERE EXISTS(SELECT 1 FROM master.ProductCategories WHERE ProductCategoryId=@id AND IsDeleted=0);",connection,tx);Add(command,"@tenant",tenantId);Add(command,"@id",rule.ProductCategoryId);Add(command,"@enabled",rule.IsEnabled);Add(command,"@coins",rule.CoinsPerUnit);Add(command,"@actor",actor);if(await command.ExecuteNonQueryAsync(token)!=1)throw new BusinessRuleException("A configured category was not found."); }
            await tx.CommitAsync(token);
        }
        catch { await tx.RollbackAsync(token); throw; }
        return await GetConfigurationAsync(tenantId,token);
    }

    public async Task<CoinWalletDto> GetWalletAsync(Guid tenantId, Guid customerId, int take, CancellationToken token)
    {
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        await using(var exists=new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsDeleted=0;",connection)){Add(exists,"@tenant",tenantId);Add(exists,"@customer",customerId);if(Convert.ToInt32(await exists.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture)!=1)throw new EntityNotFoundException("Customer was not found.");}
        var entries=new List<CoinTransactionDto>();
        await using(var command=new SqlCommand("SELECT TOP(@take) CoinTransactionId,TransactionType,Coins,SourceType,SourceId,RupeeValue,Description,CreatedOn FROM loyalty.CoinLedger WHERE TenantId=@tenant AND CustomerId=@customer ORDER BY CreatedOn DESC,CoinTransactionId DESC;",connection)){Add(command,"@take",Math.Clamp(take,1,500));Add(command,"@tenant",tenantId);Add(command,"@customer",customerId);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))entries.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetInt32(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetGuid(4),reader.IsDBNull(5)?null:reader.GetDecimal(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetDateTimeOffset(7)));}
        await using var totals=new SqlCommand("SELECT ISNULL(SUM(Coins),0),ISNULL(SUM(CASE WHEN TransactionType IN('EARN','BONUS','REFERRAL','CAMPAIGN') THEN Coins ELSE 0 END),0),ISNULL(SUM(CASE WHEN TransactionType='REDEEM' THEN -Coins ELSE 0 END),0) FROM loyalty.CoinLedger WHERE TenantId=@tenant AND CustomerId=@customer;",connection);Add(totals,"@tenant",tenantId);Add(totals,"@customer",customerId);await using var row=await totals.ExecuteReaderAsync(token);await row.ReadAsync(token);return new(customerId,row.GetInt32(0),row.GetInt32(1),row.GetInt32(2),entries);
    }

    public async Task<CoinRedemptionQuote> QuoteRedemptionAsync(Guid tenantId, Guid customerId, int coins, decimal otherDiscount, CancellationToken token)
    {
        if(coins<=0) return new(0,0,(await GetWalletAsync(tenantId,customerId,1,token)).AvailableCoins);
        var config=await GetConfigurationAsync(tenantId,token);if(!config.IsEnabled)throw new BusinessRuleException("The coin system is not enabled.");
        var wallet=await GetWalletAsync(tenantId,customerId,1,token);
        if(coins> wallet.AvailableCoins)throw new BusinessRuleException("Insufficient coin balance.");
        if(coins<config.MinimumRedemptionCoins || (config.MaximumRedemptionCoins.HasValue&&coins>config.MaximumRedemptionCoins) || coins%config.RedemptionCoins!=0)throw new BusinessRuleException("Coin redemption does not meet the configured limits or conversion increment.");
        if(otherDiscount>0&&!config.AllowWithOtherDiscounts)throw new BusinessRuleException("Coins cannot be combined with another discount.");
        return new(coins,decimal.Round((decimal)coins/config.RedemptionCoins*config.RedemptionValue,2),wallet.AvailableCoins-coins);
    }

    public async Task RedeemAsync(Guid tenantId, Guid customerId, Guid orderId, int coins, decimal otherDiscount, string? actor, CancellationToken token)
    { if(coins<=0)return;await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(token);try{await using var command=new SqlCommand("loyalty.RedeemForOrder",connection,tx){CommandType=CommandType.StoredProcedure};Add(command,"@TenantId",tenantId);Add(command,"@CustomerId",customerId);Add(command,"@OrderId",orderId);Add(command,"@Coins",coins);Add(command,"@OtherDiscount",otherDiscount);Add(command,"@CreatedBy",actor);await command.ExecuteNonQueryAsync(token);await tx.CommitAsync(token);}catch(SqlException ex)when(ex.Number>=51100){await tx.RollbackAsync(token);throw new BusinessRuleException(ex.Message);} }
    public async Task ProcessOrderAsync(Guid tenantId, Guid orderId, string status, string? actor, CancellationToken token)
    { await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);await using var command=new SqlCommand("loyalty.ProcessOrder",connection){CommandType=CommandType.StoredProcedure};Add(command,"@TenantId",tenantId);Add(command,"@OrderId",orderId);Add(command,"@EventStatus",status);Add(command,"@CreatedBy",actor);await command.ExecuteNonQueryAsync(token); }

    private static void Validate(CoinConfigurationInput x){if(x.PurchaseAmount<=0||x.PurchaseCoins<=0||x.RedemptionCoins<=0||x.RedemptionValue<=0||x.MinimumRedemptionCoins<0||x.MaximumRedemptionCoins<x.MinimumRedemptionCoins)throw new BusinessRuleException("Coin rates and limits must be positive and consistent.");if(x.EarningPriority is not("PRODUCT_FIRST" or "PURCHASE_FIRST"))throw new BusinessRuleException("Select a valid earning priority.");if(x.AwardOrderStatus is not("COMPLETED" or "DELIVERED"))throw new BusinessRuleException("Select Completed or Delivered as the award status.");if(x.ProductRules.GroupBy(y=>y.ProductId).Any(y=>y.Count()>1)||x.CategoryRules.GroupBy(y=>y.ProductCategoryId).Any(y=>y.Count()>1)||x.ProductRules.Any(y=>y.CoinsPerUnit<0)||x.CategoryRules.Any(y=>y.CoinsPerUnit<0))throw new BusinessRuleException("Coin rules must be unique and cannot contain negative coins.");}
    private static void Add(SqlCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
    private static async Task Execute(SqlConnection c,SqlTransaction t,string sql,Guid tenant,CancellationToken token){await using var command=new SqlCommand(sql,c,t);Add(command,"@tenant",tenant);await command.ExecuteNonQueryAsync(token);}
}
