using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Application.Common.Features;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed record PartnerSellerInput(string SellerName, string SellerType, string? ContactName, string? ContactEmail, string? ContactPhone, Guid? CityId, string? Locality, string? BusinessRegistrationReference, string? TaxRegistrationReference, string? FoodRegistrationReference);

public sealed class PartnerSellerService(ApplicationDbContext db, ICurrentUserService user, IFeatureService features)
{
    private Guid Tenant => user.TenantId is Guid id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("Operating retailer context is required.");
    private bool IsOwner => user.Roles.Contains("ApplicationOwner", StringComparer.Ordinal);

    public async Task<object[]> List(CancellationToken ct)
    {
        if (IsOwner && user.TenantId is null)
            return await db.PartnerSellers.AsNoTracking().OrderBy(x => x.SellerName).Select(x => (object)new { x.OperatingTenantId, x.PartnerSellerId, x.SellerName, x.SellerType, x.VerificationStatus, x.SellerStatus, x.CityId, CityName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.CityName).FirstOrDefault(), StateName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.StateName).FirstOrDefault(), x.Locality, Version = Convert.ToBase64String(x.Version) }).ToArrayAsync(ct);
        return await (from x in db.PartnerSellers.AsNoTracking().Where(x => x.OperatingTenantId == Tenant).OrderBy(x => x.SellerName)
            select (object)new { x.PartnerSellerId, x.SellerName, x.SellerType, x.ContactName, x.ContactEmail, x.ContactPhone, x.CityId, CityName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.CityName).FirstOrDefault(), StateName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.StateName).FirstOrDefault(), x.Locality, x.BusinessRegistrationReference, x.TaxRegistrationReference, x.FoodRegistrationReference, x.VerificationStatus, x.SellerStatus, LinkedProductCount=db.PartnerProductListings.Count(l=>l.OperatingTenantId==x.OperatingTenantId&&l.PartnerSellerId==x.PartnerSellerId), Version = Convert.ToBase64String(x.Version) }).ToArrayAsync(ct);
    }

    public async Task<object[]> OwnerList(CancellationToken ct)
    {
        if(!IsOwner)throw new UnauthorizedAccessException("Application Owner access required.");
        return await (from x in db.PartnerSellers.AsNoTracking().OrderBy(x=>x.SellerName)
            join t in db.Tenants.AsNoTracking() on x.OperatingTenantId equals t.TenantId
            select (object)new{x.OperatingTenantId,StoreName=t.DisplayName??t.Name,x.PartnerSellerId,x.SellerName,x.SellerType,x.CityId,CityName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.CityName).FirstOrDefault(),StateName=db.MarketplaceCities.Where(c=>c.CityId==x.CityId).Select(c=>c.StateName).FirstOrDefault(),x.Locality,x.VerificationStatus,x.SellerStatus,Version=Convert.ToBase64String(x.Version)}).ToArrayAsync(ct);
    }

    public async Task<object[]> OwnerListingQueue(CancellationToken ct)
    {
        if(!IsOwner)throw new UnauthorizedAccessException("Application Owner access required.");
        return await (from l in db.PartnerProductListings.AsNoTracking()
            join seller in db.PartnerSellers.AsNoTracking() on new{l.OperatingTenantId,l.PartnerSellerId} equals new{seller.OperatingTenantId,seller.PartnerSellerId}
            join product in db.Products.AsNoTracking() on new{TenantId=l.OperatingTenantId,l.ProductId} equals new{product.TenantId,product.ProductId}
            join tenant in db.Tenants.AsNoTracking() on l.OperatingTenantId equals tenant.TenantId
            where l.ListingStatus=="PENDING_APPROVAL"
            orderby l.CreatedAt
            select (object)new{l.OperatingTenantId,StoreName=tenant.DisplayName??tenant.Name,l.ProductId,ProductName=product.ProductName,ProductCode=product.ProductCode,l.PartnerSellerId,seller.SellerName,seller.VerificationStatus,seller.SellerStatus,l.ListingStatus,Version=Convert.ToBase64String(l.Version)}).ToArrayAsync(ct);
    }

    public async Task SetListingDecision(Guid tenant,Guid product,Guid seller,string status,string reason,string version,CancellationToken ct)
    {
        if(!IsOwner)throw new UnauthorizedAccessException("Application Owner access required.");
        if(status is not("APPROVED" or "REJECTED" or "SUSPENDED")||string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Choose a listing decision and provide a reason.");
        var row=await db.PartnerProductListings.SingleOrDefaultAsync(x=>x.OperatingTenantId==tenant&&x.ProductId==product&&x.PartnerSellerId==seller,ct)??throw new KeyNotFoundException("Partner product listing not found.");
        if(!row.Version.SequenceEqual(Convert.FromBase64String(version)))throw new DbUpdateConcurrencyException("Listing changed; reload before review.");
        if(status=="APPROVED"&&!await db.PartnerSellers.AnyAsync(x=>x.OperatingTenantId==tenant&&x.PartnerSellerId==seller&&x.SellerStatus=="ACTIVE"&&x.VerificationStatus=="VERIFIED",ct))throw new ArgumentException("Verify and activate the partner before approving its product listing.");
        row.ListingStatus=status;
        db.Set<PublicCatalogAudit>().Add(new PublicCatalogAudit{TenantId=tenant,ProductId=product,Action="PARTNER_LISTING_"+status,Actor=user.UserId?.ToString()??"owner",Reason=reason.Trim(),OccurredAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> Create(PartnerSellerInput input, CancellationToken ct)
    {
        var name = input.SellerName.Trim();
        var kind = input.SellerType.Trim().ToUpperInvariant();
        if (name.Length is < 1 or > 160 || kind is not ("INDIVIDUAL" or "BUSINESS" or "FOOD_BUSINESS" or "OTHER")) throw new ArgumentException("Enter a seller name and a supported seller type.");
        if (input.ContactEmail is { Length: > 256 } || input.ContactPhone is { Length: > 32 }) throw new ArgumentException("Seller contact information exceeds the supported length.");
        await ValidateCity(input.CityId, ct);
        var row = new PartnerSeller { PartnerSellerId = Guid.NewGuid(), OperatingTenantId = Tenant, SellerName = name, SellerType = kind,
            ContactName = Clean(input.ContactName,120), ContactEmail = Clean(input.ContactEmail,256), ContactPhone = Clean(input.ContactPhone,32), CityId=input.CityId,
            Locality=Clean(input.Locality,120), BusinessRegistrationReference=Clean(input.BusinessRegistrationReference,100),
            TaxRegistrationReference=Clean(input.TaxRegistrationReference,100), FoodRegistrationReference=Clean(input.FoodRegistrationReference,100),
            VerificationStatus="PENDING", SellerStatus="ACTIVE", CreatedAt=DateTimeOffset.UtcNow, CreatedBy=user.UserId?.ToString() ?? "" };
        db.PartnerSellers.Add(row); Audit(row,"PARTNER_CREATED","Partner seller registered."); await db.SaveChangesAsync(ct);
        return new { row.PartnerSellerId, row.SellerName, row.SellerType, row.CityId, row.Locality, row.VerificationStatus, row.SellerStatus, Version=Convert.ToBase64String(row.Version) };
    }

    public async Task<object[]> EligibleForProducts(CancellationToken ct)
    {
        var tenant=Tenant;
        return await db.PartnerSellers.AsNoTracking().Where(x=>x.OperatingTenantId==tenant&&x.SellerStatus=="ACTIVE"&&x.VerificationStatus=="VERIFIED")
            .OrderBy(x=>x.SellerName).Select(x=>(object)new{x.PartnerSellerId,x.SellerName,x.SellerType}).ToArrayAsync(ct);
    }

    public async Task<object> Update(Guid sellerId, PartnerSellerInput input, string version, CancellationToken ct)
    {
        var row=await db.PartnerSellers.SingleOrDefaultAsync(x=>x.OperatingTenantId==Tenant&&x.PartnerSellerId==sellerId,ct)??throw new KeyNotFoundException("Partner seller not found.");
        if(!row.Version.SequenceEqual(Convert.FromBase64String(version)))throw new DbUpdateConcurrencyException("Partner seller changed; reload before saving.");
        await ValidateCity(input.CityId,ct);
        Apply(row,input); row.VerificationStatus="PENDING";
        Audit(row,"PARTNER_UPDATED","Partner details changed; owner verification is required again.");
        await db.SaveChangesAsync(ct);
        return new{row.PartnerSellerId,row.SellerName,row.SellerType,row.CityId,row.Locality,row.VerificationStatus,row.SellerStatus,Version=Convert.ToBase64String(row.Version)};
    }

    public async Task SetStatus(Guid sellerId,string status,string version,CancellationToken ct)
    {
        if(status is not("ACTIVE" or "SUSPENDED"))throw new ArgumentException("Choose Active or Suspended.");
        var row=await db.PartnerSellers.SingleOrDefaultAsync(x=>x.OperatingTenantId==Tenant&&x.PartnerSellerId==sellerId,ct)??throw new KeyNotFoundException("Partner seller not found.");
        if(!row.Version.SequenceEqual(Convert.FromBase64String(version)))throw new DbUpdateConcurrencyException("Partner seller changed; reload before saving.");
        if(status=="ACTIVE"&&row.VerificationStatus!="VERIFIED")throw new ArgumentException("Only an owner-verified partner can be activated.");
        row.SellerStatus=status; Audit(row,"PARTNER_STATUS_"+status,"Retailer changed seller status."); await db.SaveChangesAsync(ct);
    }

    public async Task<object[]> History(Guid sellerId,CancellationToken ct)
    {
        var tenant=Tenant;var marker=$"\"partnerSellerId\":\"{sellerId:D}\"";
        return await db.Set<PublicCatalogAudit>().AsNoTracking().Where(x=>x.TenantId==tenant&&x.ProductId==null&&x.Action.StartsWith("PARTNER_")&&x.ContentSnapshot!=null&&x.ContentSnapshot.Contains(marker))
            .OrderByDescending(x=>x.AuditId).Take(100).Select(x=>(object)new{x.Action,x.Actor,x.Reason,x.OccurredAt}).ToArrayAsync(ct);
    }

    public async Task<object[]> OwnerHistory(Guid tenant,Guid sellerId,CancellationToken ct)
    {
        if(!IsOwner)throw new UnauthorizedAccessException("Application Owner access required.");
        if(!await db.PartnerSellers.AnyAsync(x=>x.OperatingTenantId==tenant&&x.PartnerSellerId==sellerId,ct))throw new KeyNotFoundException("Partner seller not found.");
        var marker=$"\"partnerSellerId\":\"{sellerId:D}\"";
        return await db.Set<PublicCatalogAudit>().AsNoTracking().Where(x=>x.TenantId==tenant&&x.ProductId==null&&x.Action.StartsWith("PARTNER_")&&x.ContentSnapshot!=null&&x.ContentSnapshot.Contains(marker))
            .OrderByDescending(x=>x.AuditId).Take(100).Select(x=>(object)new{x.Action,x.Actor,x.Reason,x.OccurredAt}).ToArrayAsync(ct);
    }

    public async Task<object?> ProductListing(Guid productId,CancellationToken ct)
    {
        var tenant=Tenant;
        return await (from l in db.PartnerProductListings.AsNoTracking() join s in db.PartnerSellers.AsNoTracking() on new{l.OperatingTenantId,l.PartnerSellerId} equals new{s.OperatingTenantId,s.PartnerSellerId}
            where l.OperatingTenantId==tenant&&l.ProductId==productId select new{l.PartnerSellerId,s.SellerName,s.SellerStatus,s.VerificationStatus,l.ListingStatus,Version=Convert.ToBase64String(l.Version)}).SingleOrDefaultAsync(ct);
    }

    public async Task SetProductListing(Guid productId,Guid? sellerId,string actor,CancellationToken ct)
    {
        var tenant=Tenant;
        if(!await db.Products.AnyAsync(x=>x.ProductId==productId&&x.TenantId==tenant&&!x.IsDeleted,ct))throw new KeyNotFoundException("Product not found.");
        var old=await db.PartnerProductListings.SingleOrDefaultAsync(x=>x.OperatingTenantId==tenant&&x.ProductId==productId,ct);
        var partnerFeatureEnabled=await features.IsEnabledAsync(tenant,FeatureKeys.PartnerSellerManagement,ct);
        if(!partnerFeatureEnabled&&(sellerId is not null||old is not null))
            throw new UnauthorizedAccessException("Partner product source is not enabled for this retailer.");
        if(sellerId is null) { if(old is not null){db.PartnerProductListings.Remove(old); db.Set<PublicCatalogAudit>().Add(new PublicCatalogAudit{TenantId=tenant,ProductId=productId,Action="PARTNER_LISTING_REMOVED",Actor=actor,Reason="Authorized product source change",OccurredAt=DateTimeOffset.UtcNow});} }
        else {
            if(!await db.PartnerSellers.AnyAsync(x=>x.OperatingTenantId==tenant&&x.PartnerSellerId==sellerId&&x.SellerStatus=="ACTIVE"&&x.VerificationStatus=="VERIFIED",ct))throw new ArgumentException("Select an active, owner-verified partner belonging to this retailer.");
            if(old is null){old=new PartnerProductListing{OperatingTenantId=tenant,ProductId=productId,PartnerSellerId=sellerId.Value,ListingStatus="PENDING_APPROVAL",CreatedAt=DateTimeOffset.UtcNow,CreatedBy=actor};db.PartnerProductListings.Add(old);}
            else if(old.PartnerSellerId!=sellerId.Value){old.PartnerSellerId=sellerId.Value;old.ListingStatus="PENDING_APPROVAL";}
            else return;
            db.Set<PublicCatalogAudit>().Add(new PublicCatalogAudit{TenantId=tenant,ProductId=productId,Action="PARTNER_LISTING_SUBMITTED",Actor=actor,Reason="Partner seller association submitted",OccurredAt=DateTimeOffset.UtcNow});
        }
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(PartnerSeller row,PartnerSellerInput input)
    {
        var name=input.SellerName.Trim();var kind=input.SellerType.Trim().ToUpperInvariant();
        if(name.Length is <1 or >160||kind is not("INDIVIDUAL" or "BUSINESS" or "FOOD_BUSINESS" or "OTHER"))throw new ArgumentException("Enter a seller name and supported type.");
        row.SellerName=name;row.SellerType=kind;row.ContactName=Clean(input.ContactName,120);row.ContactEmail=Clean(input.ContactEmail,256);row.ContactPhone=Clean(input.ContactPhone,32);row.CityId=input.CityId;row.Locality=Clean(input.Locality,120);row.BusinessRegistrationReference=Clean(input.BusinessRegistrationReference,100);row.TaxRegistrationReference=Clean(input.TaxRegistrationReference,100);row.FoodRegistrationReference=Clean(input.FoodRegistrationReference,100);
    }

    private async Task ValidateCity(Guid? cityId,CancellationToken ct)
    {
        if(cityId.HasValue&&!await db.MarketplaceCities.AnyAsync(x=>x.CityId==cityId&&x.IsActive&&x.IsPublished,ct))
            throw new ArgumentException("Choose an active, published marketplace city.");
    }

    public async Task SetOwnerDecision(Guid operatingTenant, Guid seller, string status, string reason, string version, CancellationToken ct)
    {
        if (!IsOwner) throw new UnauthorizedAccessException("Application Owner access required.");
        if (status is not ("VERIFIED" or "REJECTED" or "SUSPENDED") || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Choose a supported verification decision and provide an audit reason.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.PartnerSellers.SingleOrDefaultAsync(x => x.OperatingTenantId == operatingTenant && x.PartnerSellerId == seller, ct) ?? throw new KeyNotFoundException("Partner seller not found.");
        if (!row.Version.SequenceEqual(Convert.FromBase64String(version))) throw new DbUpdateConcurrencyException("Partner seller changed; reload before reviewing.");
        row.VerificationStatus = status;
        db.Set<PublicCatalogAudit>().Add(new PublicCatalogAudit { TenantId=operatingTenant, ProductId=null, Action="PARTNER_"+status, Actor=user.UserId?.ToString() ?? "owner", Reason=reason.Trim(), ContentSnapshot=System.Text.Json.JsonSerializer.Serialize(new{partnerSellerId=row.PartnerSellerId,sellerName=row.SellerName,verificationStatus=row.VerificationStatus,sellerStatus=row.SellerStatus}), OccurredAt=DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private void Audit(PartnerSeller row,string action,string reason)=>db.Set<PublicCatalogAudit>().Add(new PublicCatalogAudit{TenantId=row.OperatingTenantId,ProductId=null,Action=action,Actor=user.UserId?.ToString()??"retailer",Reason=reason,ContentSnapshot=System.Text.Json.JsonSerializer.Serialize(new{partnerSellerId=row.PartnerSellerId,sellerName=row.SellerName,sellerType=row.SellerType,verificationStatus=row.VerificationStatus,sellerStatus=row.SellerStatus,cityId=row.CityId,locality=row.Locality}),OccurredAt=DateTimeOffset.UtcNow});

    private static string? Clean(string? value,int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length,max)];
}
