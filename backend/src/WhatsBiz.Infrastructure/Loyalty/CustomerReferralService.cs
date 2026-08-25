using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Referrals;

namespace WhatsBiz.Infrastructure.Loyalty;

public sealed class CustomerReferralService(IConfiguration configuration, IFeatureService features) : ICustomerReferralService
{
    private string Cs => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<ReferralConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token)
    {
        await using var c = new SqlConnection(Cs); await c.OpenAsync(token);
        await using var q = new SqlCommand("SELECT IsEnabled,QualificationType,MinimumQualifyingAmount,ReferrerRewardCoins,ReferredRewardCoins,CoinValidityDays,MaximumRewardedReferralsPerCustomerMonth,MaximumCoinsPerCustomerMonth,ReverseOnRefund,RedemptionCoins,RedemptionValue,MinimumRedemptionCoins,MaximumOrderPercentage,AllowWithCoupons,AllowDiscountedProducts,AllowTax,AllowDelivery FROM loyalty.ReferralConfigurations WHERE TenantId=@tenant", c);
        Add(q,"@tenant",tenantId); await using var r=await q.ExecuteReaderAsync(token);
        return await r.ReadAsync(token) ? MapConfiguration(r) : Defaults();
    }

    public async Task<ReferralConfigurationDto> SaveConfigurationAsync(Guid tenantId, ReferralConfigurationInput input, string? actor, CancellationToken token)
    {
        Validate(input); await using var c=new SqlConnection(Cs); await c.OpenAsync(token);
        await using var q=new SqlCommand("""
MERGE loyalty.ReferralConfigurations t USING(SELECT @tenant TenantId)s ON t.TenantId=s.TenantId
WHEN MATCHED THEN UPDATE SET IsEnabled=@enabled,QualificationType=@qualification,MinimumQualifyingAmount=@amount,ReferrerRewardCoins=@referrer,ReferredRewardCoins=@referred,CoinValidityDays=@validity,MaximumRewardedReferralsPerCustomerMonth=@maxReferrals,MaximumCoinsPerCustomerMonth=@maxCoins,ReverseOnRefund=@reverse,RedemptionCoins=@redemptionCoins,RedemptionValue=@redemptionValue,MinimumRedemptionCoins=@minimumRedemption,MaximumOrderPercentage=@maximumPercentage,AllowWithCoupons=@coupons,AllowDiscountedProducts=@discounted,AllowTax=@tax,AllowDelivery=@delivery,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor
WHEN NOT MATCHED THEN INSERT(TenantId,IsEnabled,QualificationType,MinimumQualifyingAmount,ReferrerRewardCoins,ReferredRewardCoins,CoinValidityDays,MaximumRewardedReferralsPerCustomerMonth,MaximumCoinsPerCustomerMonth,ReverseOnRefund,RedemptionCoins,RedemptionValue,MinimumRedemptionCoins,MaximumOrderPercentage,AllowWithCoupons,AllowDiscountedProducts,AllowTax,AllowDelivery,CreatedBy) VALUES(@tenant,@enabled,@qualification,@amount,@referrer,@referred,@validity,@maxReferrals,@maxCoins,@reverse,@redemptionCoins,@redemptionValue,@minimumRedemption,@maximumPercentage,@coupons,@discounted,@tax,@delivery,@actor);
""",c); AddConfiguration(q,tenantId,input,actor); await q.ExecuteNonQueryAsync(token); return await GetConfigurationAsync(tenantId,token);
    }

    public async Task<ReferralCodeDto> GetOrCreateCodeAsync(Guid tenantId, Guid customerId, string? actor, CancellationToken token)
    {
        await using var c=new SqlConnection(Cs); await c.OpenAsync(token);
        var found=await ReadCode(c,tenantId,customerId,token); if(found is not null)return ToCode(found);
        for(var attempt=0;attempt<8;attempt++)
        {
            var code=ReferralCodeGenerator.Create();
            try { await using var q=new SqlCommand("INSERT loyalty.CustomerReferralCodes(CustomerReferralCodeId,TenantId,CustomerId,ReferralCode,CreatedBy) SELECT NEWID(),@tenant,@customer,@code,@actor WHERE EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsDeleted=0)",c);Add(q,"@tenant",tenantId);Add(q,"@customer",customerId);Add(q,"@code",code);Add(q,"@actor",actor);if(await q.ExecuteNonQueryAsync(token)!=1)throw new EntityNotFoundException("Customer was not found."); }
            catch(SqlException e) when(e.Number is 2601 or 2627){continue;}
            return ToCode((await ReadCode(c,tenantId,customerId,token))!);
        }
        throw new InvalidOperationException("A unique referral code could not be generated.");
    }

    public async Task SetCodeActiveAsync(Guid tenantId,Guid customerId,bool active,string? actor,CancellationToken token)
    {await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("UPDATE loyalty.CustomerReferralCodes SET IsActive=@active,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@actor WHERE TenantId=@tenant AND CustomerId=@customer",c);Add(q,"@tenant",tenantId);Add(q,"@customer",customerId);Add(q,"@active",active);Add(q,"@actor",actor);if(await q.ExecuteNonQueryAsync(token)!=1)throw new EntityNotFoundException("Referral code was not found.");}

    public async Task<ReferralResolutionDto?> ResolveCodeAsync(string code,CancellationToken token)
    {
        code=ReferralCodeGenerator.Normalize(code);await using var c=new SqlConnection(Cs);await c.OpenAsync(token);
        await using var q=new SqlCommand("SELECT rc.TenantId,rc.ReferralCode,t.TenantKey,t.Name,CAST(CASE WHEN rc.IsActive=1 AND cfg.IsEnabled=1 THEN 1 ELSE 0 END AS bit) FROM loyalty.CustomerReferralCodes rc JOIN core.Tenants t ON t.TenantId=rc.TenantId AND t.IsActive=1 LEFT JOIN loyalty.ReferralConfigurations cfg ON cfg.TenantId=rc.TenantId WHERE rc.ReferralCode=@code",c);Add(q,"@code",code);
        await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))return null;
        var tenantId=r.GetGuid(0);var result=new ReferralResolutionDto(r.GetString(1),r.GetString(2),r.GetString(3),r.GetBoolean(4));
        return result with { IsEnabled=result.IsEnabled&&await features.IsEnabledAsync(tenantId,FeatureKeys.CustomerReferralRewards,token) };
    }

    public async Task<ReferralDto> CaptureAsync(Guid tenantId,ReferralCaptureInput input,string? actor,CancellationToken token)
    {await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("loyalty.CaptureCustomerReferral",c){CommandType=CommandType.StoredProcedure};Add(q,"@TenantId",tenantId);Add(q,"@ReferralCode",ReferralCodeGenerator.Normalize(input.Code));Add(q,"@ReferredCustomerId",input.ReferredCustomerId);Add(q,"@CaptureSource",input.Source.ToUpperInvariant());Add(q,"@CreatedBy",actor);try{var id=(Guid)(await q.ExecuteScalarAsync(token)??throw new InvalidOperationException());return (await GetHistoryAsync(tenantId,null,500,token)).Single(x=>x.ReferralId==id);}catch(SqlException e)when(e.Number>=51200){throw new BusinessRuleException(e.Message);}}

    public Task EvaluateOrderAsync(Guid tenantId,Guid orderId,string status,string? actor,CancellationToken token)=>ExecuteProcedure("loyalty.ProcessReferralOrder",tenantId,orderId,status,null,actor,token);
    public Task ReverseOrderAsync(Guid tenantId,Guid orderId,string reason,string? actor,CancellationToken token)=>ExecuteProcedure("loyalty.ReverseReferralOrder",tenantId,orderId,null,reason,actor,token);
    public async Task ApproveAsync(Guid tenantId,Guid referralId,string? actor,CancellationToken token)
    {await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("loyalty.ApproveCustomerReferral",c){CommandType=CommandType.StoredProcedure};Add(q,"@TenantId",tenantId);Add(q,"@ReferralId",referralId);Add(q,"@CreatedBy",actor);try{await q.ExecuteNonQueryAsync(token);}catch(SqlException e)when(e.Number>=51200){throw new BusinessRuleException(e.Message);}}

    public async Task<IReadOnlyCollection<ReferralDto>> GetHistoryAsync(Guid tenantId,Guid? customerId,int take,CancellationToken token)
    {var rows=new List<ReferralDto>();await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(@take) r.CustomerReferralId,r.ReferrerCustomerId,r.ReferredCustomerId,r.Status,r.QualificationType,r.QualifyingOrderId,r.QualifiedAt,r.RewardedAt,r.ReversedAt,r.RejectionReason,r.CreatedOn,CONCAT(LEFT(c.CustomerName,1),N'***') FROM loyalty.CustomerReferrals r JOIN sales.Customers c ON c.CustomerId=r.ReferredCustomerId AND c.TenantId=r.TenantId WHERE r.TenantId=@tenant AND(@customer IS NULL OR r.ReferrerCustomerId=@customer OR r.ReferredCustomerId=@customer) ORDER BY r.CreatedOn DESC",c);Add(q,"@take",Math.Clamp(take,1,500));Add(q,"@tenant",tenantId);Add(q,"@customer",customerId);await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetGuid(5),r.IsDBNull(6)?null:r.GetDateTimeOffset(6),r.IsDBNull(7)?null:r.GetDateTimeOffset(7),r.IsDBNull(8)?null:r.GetDateTimeOffset(8),r.IsDBNull(9)?null:r.GetString(9),r.GetDateTimeOffset(10),r.GetString(11)));return rows;}

    public async Task<ReferralMetricsDto> GetMetricsAsync(Guid tenantId,CancellationToken token)
    {await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT COUNT(*),SUM(CASE WHEN Status='PENDING' THEN 1 ELSE 0 END),SUM(CASE WHEN Status IN('REWARDED','REVERSED') THEN 1 ELSE 0 END),SUM(CASE WHEN Status='REVERSED' THEN 1 ELSE 0 END),ISNULL((SELECT SUM(Coins) FROM loyalty.CoinLedger WHERE TenantId=@tenant AND SourceType='REFERRAL' AND Coins>0),0),ISNULL((SELECT SUM(-Coins) FROM loyalty.CoinLedger WHERE TenantId=@tenant AND TransactionType='REDEEM'),0),ISNULL((SELECT SUM(Coins) FROM loyalty.CoinLedger WHERE TenantId=@tenant),0),ISNULL(SUM(CASE WHEN Status IN('REWARDED','REVERSED') THEN 100.0 ELSE 0 END)/NULLIF(COUNT(*),0),0),ISNULL(SUM(CASE WHEN Status IN('REWARDED','REVERSED') THEN si.GrandTotal ELSE 0 END),0) FROM loyalty.CustomerReferrals r LEFT JOIN sales.SalesInvoices si ON si.InvoiceId=r.QualifyingOrderId WHERE r.TenantId=@tenant; SELECT TOP(10) c.CustomerId,c.CustomerName,COUNT(*),ISNULL(SUM(l.Coins),0) FROM loyalty.CustomerReferrals r JOIN sales.Customers c ON c.CustomerId=r.ReferrerCustomerId AND c.TenantId=r.TenantId LEFT JOIN loyalty.CoinLedger l ON l.TenantId=r.TenantId AND l.SourceId=r.CustomerReferralId AND l.CustomerId=r.ReferrerCustomerId WHERE r.TenantId=@tenant AND r.Status IN('REWARDED','REVERSED') GROUP BY c.CustomerId,c.CustomerName ORDER BY COUNT(*) DESC",c);Add(q,"@tenant",tenantId);await using var r=await q.ExecuteReaderAsync(token);await r.ReadAsync(token);var values=(r.GetInt32(0),r.IsDBNull(1)?0:r.GetInt32(1),r.IsDBNull(2)?0:r.GetInt32(2),r.IsDBNull(3)?0:r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetDecimal(7),r.GetDecimal(8));var top=new List<TopReferrerDto>();await r.NextResultAsync(token);while(await r.ReadAsync(token))top.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3)));return new(values.Item1,values.Item2,values.Item3,values.Item4,values.Item5,values.Item6,values.Item7,values.Item8,values.Item9,top);}

    public async Task AdjustAsync(Guid tenantId,RewardAdjustmentInput input,string? actor,CancellationToken token)
    {if(input.Coins==0||string.IsNullOrWhiteSpace(input.Reason))throw new BusinessRuleException("A non-zero coin amount and reason are required.");await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("loyalty.AdjustCustomerReward",c){CommandType=CommandType.StoredProcedure};Add(q,"@TenantId",tenantId);Add(q,"@CustomerId",input.CustomerId);Add(q,"@Coins",input.Coins);Add(q,"@Reason",input.Reason.Trim());Add(q,"@CreatedBy",actor);try{await q.ExecuteNonQueryAsync(token);}catch(SqlException e)when(e.Number>=51200){throw new BusinessRuleException(e.Message);}}
    public async Task<int> ExpireAsync(Guid? tenantId,int batchSize,string? actor,CancellationToken token)
    {await using var c=new SqlConnection(Cs);await c.OpenAsync(token);await using var q=new SqlCommand("loyalty.ExpireCustomerRewards",c){CommandType=CommandType.StoredProcedure};Add(q,"@TenantId",tenantId);Add(q,"@BatchSize",Math.Clamp(batchSize,1,5000));Add(q,"@CreatedBy",actor);return Convert.ToInt32(await q.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture);}

    private ReferralCodeDto ToCode(CodeRow x){var configured=configuration["App:PublicBaseUrl"]??configuration["WhatsApp:PublicBaseUrl"];if(!Uri.TryCreate(configured,UriKind.Absolute,out var root)||root.Scheme is not("http" or "https"))throw new InvalidOperationException("App:PublicBaseUrl must be an absolute HTTP(S) URL before referral links can be generated.");return new(x.Id,x.CustomerId,x.Code,new Uri(root,$"ref/{Uri.EscapeDataString(x.Code)}").ToString(),x.Active,x.Created);}
    private static async Task<CodeRow?> ReadCode(SqlConnection c,Guid tenant,Guid customer,CancellationToken token){await using var q=new SqlCommand("SELECT CustomerReferralCodeId,CustomerId,ReferralCode,IsActive,CreatedOn FROM loyalty.CustomerReferralCodes WHERE TenantId=@tenant AND CustomerId=@customer",c);Add(q,"@tenant",tenant);Add(q,"@customer",customer);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetBoolean(3),r.GetDateTimeOffset(4)):null;}
    private async Task ExecuteProcedure(string name,Guid tenant,Guid order,string? status,string? reason,string? actor,CancellationToken token)
    {
        await using var c=new SqlConnection(Cs);await c.OpenAsync(token);
        await using var q=new SqlCommand(name,c){CommandType=CommandType.StoredProcedure};
        Add(q,"@TenantId",tenant);Add(q,"@OrderId",order);if(status is not null)Add(q,"@EventStatus",status);if(reason is not null)Add(q,"@Reason",reason);Add(q,"@CreatedBy",actor);
        try{await q.ExecuteNonQueryAsync(token);}catch(SqlException e)when(e.Number>=51200){throw new BusinessRuleException(e.Message);}
    }
    private static ReferralConfigurationDto MapConfiguration(SqlDataReader r)=>new(r.GetBoolean(0),r.GetString(1),r.GetDecimal(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7),r.GetBoolean(8),r.GetInt32(9),r.GetDecimal(10),r.GetInt32(11),r.GetDecimal(12),r.GetBoolean(13),r.GetBoolean(14),r.GetBoolean(15),r.GetBoolean(16));
    private static ReferralConfigurationDto Defaults()=>new(false,"FIRST_COMPLETED_ORDER",0,200,100,180,10,2000,true,100,10,100,20,false,true,false,false);
    private static void Validate(ReferralConfigurationInput x){if(!ReferralQualificationTypes.All.Contains(x.QualificationType)||x.MinimumQualifyingAmount<0||x.ReferrerRewardCoins<0||x.ReferredRewardCoins<0||x.CoinValidityDays<1||x.MaximumRewardedReferralsPerCustomerMonth<1||x.MaximumCoinsPerCustomerMonth<1||x.RedemptionCoins<1||x.RedemptionValue<=0||x.MinimumRedemptionCoins<0||x.MaximumOrderPercentage is <0 or >100)throw new BusinessRuleException("Referral and redemption values are invalid.");}
    private static void AddConfiguration(SqlCommand q,Guid tenant,ReferralConfigurationInput x,string? actor){Add(q,"@tenant",tenant);Add(q,"@enabled",x.IsEnabled);Add(q,"@qualification",x.QualificationType.ToUpperInvariant());Add(q,"@amount",x.MinimumQualifyingAmount);Add(q,"@referrer",x.ReferrerRewardCoins);Add(q,"@referred",x.ReferredRewardCoins);Add(q,"@validity",x.CoinValidityDays);Add(q,"@maxReferrals",x.MaximumRewardedReferralsPerCustomerMonth);Add(q,"@maxCoins",x.MaximumCoinsPerCustomerMonth);Add(q,"@reverse",x.ReverseOnRefund);Add(q,"@redemptionCoins",x.RedemptionCoins);Add(q,"@redemptionValue",x.RedemptionValue);Add(q,"@minimumRedemption",x.MinimumRedemptionCoins);Add(q,"@maximumPercentage",x.MaximumOrderPercentage);Add(q,"@coupons",x.AllowWithCoupons);Add(q,"@discounted",x.AllowDiscountedProducts);Add(q,"@tax",x.AllowTax);Add(q,"@delivery",x.AllowDelivery);Add(q,"@actor",actor);}
    private static void Add(SqlCommand q,string name,object? value)=>q.Parameters.AddWithValue(name,value??DBNull.Value);
    private sealed record CodeRow(Guid Id,Guid CustomerId,string Code,bool Active,DateTimeOffset Created);
}
