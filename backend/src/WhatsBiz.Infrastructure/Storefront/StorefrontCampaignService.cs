using System.Data;
using System.Globalization;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontCampaignService(IConfiguration configuration, ApplicationDbContext db,
    IStorefrontAvailabilityService availability, IProductImageOptimizer optimizer, IStorefrontMediaStorage storage,
    TimeProvider? timeProvider = null, ILogger<StorefrontCampaignService>? logger = null, IFeatureService? features = null) : IStorefrontCampaignService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private async Task<bool> Entitled(Guid tenant, CancellationToken token) => features is not null && await features.IsEnabledAsync(tenant, FeatureKeys.StorefrontPopupCampaigns, token);
    private async Task RequireManagement(Guid tenant, CancellationToken token) { if (!await Entitled(tenant, token)) throw new BusinessRuleException("Storefront Popup Campaigns is not enabled for this retailer."); }
    private async Task<SqlConnection> Open(CancellationToken token) { var c=new SqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(token);return c; }
    private static void Add(SqlCommand q,string key,object? value)=>q.Parameters.AddWithValue(key,value??DBNull.Value);
    public async Task<IReadOnlyCollection<StorefrontCampaign>> ListAsync(Guid tenant,CancellationToken token)
    {
        await RequireManagement(tenant, token);
        await using var c=await Open(token);await using var q=new SqlCommand("SELECT CampaignId,Name,Headline,HtmlContent,IsActive,StartsAtUtc,EndsAtUtc,TimeZoneId,Frequency,Priority,CtaText,DestinationType,DestinationId,ScheduleId,OnlyWhenOrderingOpen,MediaId,UpdatedAtUtc FROM commerce.StorefrontPopupCampaigns WHERE TenantId=@tenant ORDER BY Priority DESC,CampaignId",c);Add(q,"@tenant",tenant);
        await using var r=await q.ExecuteReaderAsync(token);var rows=new List<StorefrontCampaign>();
        while(await r.ReadAsync(token)){
            var zone=StorefrontCampaignPolicy.Zone(r.GetString(7));var start=r.GetDateTimeOffset(5);var end=r.GetDateTimeOffset(6);
            rows.Add(new(r.GetGuid(0),new(r.GetString(1),r.GetString(2),StorefrontCampaignPolicy.Sanitize(r.GetString(3)),r.GetBoolean(4),
                TimeZoneInfo.ConvertTime(start,zone).ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture),TimeZoneInfo.ConvertTime(end,zone).ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture),r.GetString(7),r.GetString(8),r.GetInt32(9),r.IsDBNull(10)?null:r.GetString(10),r.IsDBNull(11)?null:r.GetString(11),r.IsDBNull(12)?null:r.GetGuid(12),r.IsDBNull(13)?null:r.GetGuid(13),r.GetBoolean(14)),start,end,r.IsDBNull(15)?null:r.GetGuid(15),r.GetDateTimeOffset(16)));
        }return rows;
    }
    public async Task<StorefrontCampaign> SaveAsync(Guid tenant,Guid? id,SaveStorefrontCampaign input,CancellationToken token)
    {
        await RequireManagement(tenant, token);
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>150||string.IsNullOrWhiteSpace(input.Headline)||input.Headline.Length>200||input.CtaText?.Length>80||string.IsNullOrWhiteSpace(input.TimeZoneId)||input.TimeZoneId.Length>100||input.Priority is < -10000 or >10000)
            throw new BusinessRuleException("Enter a campaign name, headline and valid priority.");
        if(input.Frequency is not ("VISIT" or "DAY" or "CAMPAIGN"))throw new BusinessRuleException("Choose a valid display frequency.");
        if(input.OnlyWhenOrderingOpen&&!input.ScheduleId.HasValue)throw new BusinessRuleException("Choose an ordering schedule.");
        var start=StorefrontCampaignPolicy.ToUtc(input.StartsAtLocal,input.TimeZoneId);var end=StorefrontCampaignPolicy.ToUtc(input.EndsAtLocal,input.TimeZoneId);
        if(end<=start)throw new BusinessRuleException("Campaign end must be after its start.");
        var html=StorefrontCampaignPolicy.Sanitize(input.HtmlContent);var campaignId=id??Guid.NewGuid();
        await using var c=await Open(token);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(token);
        if(input.ScheduleId.HasValue){await using var check=new SqlCommand("SELECT COUNT(1) FROM commerce.StorefrontAvailabilitySchedules WHERE TenantId=@tenant AND ScheduleId=@id",c,tx);Add(check,"@tenant",tenant);Add(check,"@id",input.ScheduleId);if((int)(await check.ExecuteScalarAsync(token)??0)!=1)throw new BusinessRuleException("Schedule does not belong to this store.");}
        if(input.DestinationId.HasValue||input.DestinationType is not null){
            var sql=input.DestinationType switch{
                "PRODUCT"=>"SELECT COUNT(1) FROM master.Products WHERE TenantId=@tenant AND ProductId=@id AND IsActive=1 AND IsDeleted=0 AND IsWhatsAppVisible=1",
                "CATEGORY"=>"SELECT COUNT(1) FROM master.TenantProductCategories t JOIN master.ProductCategories c ON c.ProductCategoryId=t.ProductCategoryId WHERE t.TenantId=@tenant AND c.ProductCategoryId=@id AND c.IsActive=1 AND c.IsDeleted=0",
                "OFFER"=>"SELECT COUNT(1) FROM commerce.StorefrontPromotions WHERE TenantId=@tenant AND PromotionId=@id AND IsActive=1 AND IsDeleted=0",
                _=>throw new BusinessRuleException("Choose a category, product or offer destination.")};
            await using var check=new SqlCommand(sql,c,tx);Add(check,"@tenant",tenant);Add(check,"@id",input.DestinationId);if((int)(await check.ExecuteScalarAsync(token)??0)!=1)throw new BusinessRuleException("Destination is unavailable for this store.");
        }
        await using var q=new SqlCommand(id.HasValue?"""
            UPDATE commerce.StorefrontPopupCampaigns SET Name=@name,Headline=@headline,HtmlContent=@html,IsActive=@active,StartsAtUtc=@start,EndsAtUtc=@end,TimeZoneId=@zone,Frequency=@frequency,Priority=@priority,CtaText=@cta,DestinationType=@kind,DestinationId=@destination,ScheduleId=@schedule,OnlyWhenOrderingOpen=@open,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND CampaignId=@id;
            """:"""
            INSERT commerce.StorefrontPopupCampaigns(CampaignId,TenantId,Name,Headline,HtmlContent,IsActive,StartsAtUtc,EndsAtUtc,TimeZoneId,Frequency,Priority,CtaText,DestinationType,DestinationId,ScheduleId,OnlyWhenOrderingOpen) VALUES(@id,@tenant,@name,@headline,@html,@active,@start,@end,@zone,@frequency,@priority,@cta,@kind,@destination,@schedule,@open);
            """,c,tx);
        Add(q,"@id",campaignId);Add(q,"@tenant",tenant);Add(q,"@name",input.Name.Trim());Add(q,"@headline",input.Headline.Trim());Add(q,"@html",html);Add(q,"@active",input.IsActive);Add(q,"@start",start);Add(q,"@end",end);Add(q,"@zone",input.TimeZoneId);Add(q,"@frequency",input.Frequency);Add(q,"@priority",input.Priority);Add(q,"@cta",input.CtaText);Add(q,"@kind",input.DestinationType);Add(q,"@destination",input.DestinationId);Add(q,"@schedule",input.ScheduleId);Add(q,"@open",input.OnlyWhenOrderingOpen);
        if(await q.ExecuteNonQueryAsync(token)!=1)throw new BusinessRuleException("Campaign was not found.");
        await tx.CommitAsync(token);return(await ListAsync(tenant,token)).Single(x=>x.CampaignId==campaignId);
    }
    public async Task<StorefrontCampaignFeed?> FeedAsync(string storeKey,CancellationToken token)
    {
        var tenant=await availability.ResolveTenantIdAsync(storeKey,token);if(!tenant.HasValue)return null;
        if (!await Entitled(tenant.Value, token)) return new([], clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(1));
        var now=clock.GetUtcNow();var all=await ListAsync(tenant.Value,token);var rows=all.Where(x=>x.Settings.IsActive&&x.EndsAtUtc>now).ToArray();
        var windows=rows.Any(x=>x.Settings.OnlyWhenOrderingOpen)?await availability.GetCampaignWindowsAsync(tenant.Value,token):[];
        var midnights=rows.Where(x=>x.Settings.Frequency=="DAY").Select(x=>{var zone=StorefrontCampaignPolicy.Zone(x.Settings.TimeZoneId);var local=TimeZoneInfo.ConvertTime(now,zone).Date.AddDays(1);while(zone.IsInvalidTime(local))local=local.AddMinutes(1);return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone));});
        var transitions=rows.SelectMany(x=>new[]{x.StartsAtUtc,x.EndsAtUtc}).Concat(midnights).Concat(windows.SelectMany(x=>new[]{x.NextOrderOpenAtUtc,x.NextOrderCloseAtUtc}).Where(x=>x.HasValue).Select(x=>x!.Value)).Where(x=>x>now).Append(now.AddMinutes(5));
        var visible=rows.Where(x=>StorefrontCampaignPolicy.Eligible(x,now,windows.Any(w=>w.ScheduleId==x.Settings.ScheduleId&&w.Status.StartsWith("ORDERING_OPEN",StringComparison.Ordinal)))).Select(x=>{
            var root="/"+Uri.EscapeDataString(storeKey);var kind=x.Settings.DestinationType;var target=x.Settings.DestinationId;
            var destination=target.HasValue?kind switch{"CATEGORY"=>root+"?category="+target,"PRODUCT"=>root+"/products/"+target,"OFFER"=>root+"/offers/"+target,_=>null}:null;
            return new PublicStorefrontCampaign(x.CampaignId,x.Settings.Headline,StorefrontCampaignPolicy.Sanitize(x.Settings.HtmlContent),x.Settings.Frequency,x.Settings.Priority,x.MediaId.HasValue?$"/api/store/{Uri.EscapeDataString(storeKey)}/campaigns/{x.CampaignId}/image/{x.MediaId}":null,x.Settings.CtaText,destination,TimeZoneInfo.ConvertTime(now,StorefrontCampaignPolicy.Zone(x.Settings.TimeZoneId)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),x.EndsAtUtc);
        }).ToArray();
        return new(visible,now,transitions.Min());
    }
    public async Task UploadAsync(Guid tenant,Guid id,string fileName,Stream content,CancellationToken token)
    {
        var stage="campaign-lookup";
        try{
        if(!(await ListAsync(tenant,token)).Any(x=>x.CampaignId==id))throw new BusinessRuleException("Campaign was not found.");
        stage="image-validation";
        await using var bytes=new MemoryStream();await content.CopyToAsync(bytes,token);if(bytes.Length is <=0 or >5242880)throw new BusinessRuleException("Choose an image up to 5 MB.");
        var image=await optimizer.OptimizeAsync(fileName,null,bytes.ToArray(),token);var mediaId=Guid.NewGuid();
        stage="media-storage";
        var stored=await storage.StoreStorefrontAsync(new(tenant,"campaign",mediaId,image.CatalogData,image.ThumbnailData,image.ContentType),token);
        try{
            stage="database-transaction";
            await using var tx=await db.Database.BeginTransactionAsync(token);
            var database=stored.Provider==ProductImageStorageProviders.Database;
            db.StorefrontMedia.Add(new StorefrontMedia{MediaId=mediaId,TenantId=tenant,ResourceType="campaign",FileName=image.FileName,ContentType=image.ContentType,ThumbnailContentType="image/webp",StorageProvider=stored.Provider,ObjectKey=stored.ObjectKey,ThumbnailObjectKey=stored.ThumbnailObjectKey,ImageData=database?image.CatalogData:null,ThumbnailData=database?image.ThumbnailData:null,ContentHash=stored.ContentHash,CreatedAt=DateTime.UtcNow});
            stage="database-media-save";
            await db.SaveChangesAsync(token);
            stage="campaign-association";
            if(await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commerce.StorefrontPopupCampaigns SET MediaId={mediaId},UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId={tenant} AND CampaignId={id}",token)!=1)throw new BusinessRuleException("Campaign was not found.");
            stage="transaction-commit";
            await tx.CommitAsync(token);
        }catch{
            try{await storage.DeleteStorefrontAsync(new(tenant,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),CancellationToken.None);}
            catch(Exception cleanup){StorefrontCampaignUploadLogs.Failed(logger??NullLogger<StorefrontCampaignService>.Instance,cleanup,"new-media-cleanup",Activity.Current?.Id??"unavailable");}
            throw;
        }
        }catch(Exception error) when(error is not OperationCanceledException && error is not BusinessRuleException){StorefrontCampaignUploadLogs.Failed(logger??NullLogger<StorefrontCampaignService>.Instance,error,stage,Activity.Current?.Id??"unavailable");throw;}
    }
    public async Task RemoveImageAsync(Guid tenant,Guid id,CancellationToken token)
    {await RequireManagement(tenant, token);if(await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commerce.StorefrontPopupCampaigns SET MediaId=NULL,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId={tenant} AND CampaignId={id}",token)!=1)throw new BusinessRuleException("Campaign was not found.");}
    public async Task<StorefrontImage?> ImageAsync(string storeKey,Guid id,Guid media,CancellationToken token)
    {
        var tenant=await availability.ResolveTenantIdAsync(storeKey,token);if(!tenant.HasValue)return null;
        var feed=await FeedAsync(storeKey,token);if(feed is null||!feed.Campaigns.Any(x=>x.CampaignId==id))return null;
        if(!(await ListAsync(tenant.Value,token)).Any(x=>x.CampaignId==id&&x.MediaId==media))return null;
        return await ReadImageAsync(tenant.Value,media,token);
    }
    public async Task<StorefrontImage?> GetAdminImageAsync(Guid tenant,Guid id,CancellationToken token)
    {
        var campaign=(await ListAsync(tenant,token)).SingleOrDefault(x=>x.CampaignId==id);
        return campaign?.MediaId is Guid media?await ReadImageAsync(tenant,media,token):null;
    }
    private async Task<StorefrontImage?> ReadImageAsync(Guid tenant,Guid media,CancellationToken token)
    {
        var row=await db.StorefrontMedia.AsNoTracking().SingleOrDefaultAsync(x=>x.TenantId==tenant&&x.MediaId==media&&x.ResourceType=="campaign"&&x.CustomerId==null,token);if(row is null)return null;
        var image=await storage.ReadStorefrontAsync(new(tenant,row.StorageProvider,row.ObjectKey,row.ImageData??[],row.ContentType),token);
        return image is null?null:new(image.ContentType,image.Content);
    }
}

internal static partial class StorefrontCampaignUploadLogs
{
    [LoggerMessage(4201,LogLevel.Error,"Campaign image upload failed at Stage={Stage} ActivityId={ActivityId}. No file content, filename or credentials are logged.")]
    public static partial void Failed(ILogger logger,Exception exception,string stage,string activityId);
}
