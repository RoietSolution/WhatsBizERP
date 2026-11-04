using System.Data;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed record CityInput(string CityName, string StateName, string Slug, string MarketplaceName, string? Description, Guid? LogoMediaId, Guid? BannerMediaId, string? BannerHeadline, string? BannerSubtitle, string? SeoTitle, string? SeoDescription, string? ExternalMarketingDomain, bool IsActive, bool IsPublished, string? Version);

public sealed record RetailerCityInput(Guid CityId, string? Locality, string? ServiceArea, bool IsActive = true);

public sealed class PublicMarketplaceService(ApplicationDbContext db, ICurrentUserService currentUser, IFeatureService features, IStorefrontAvailabilityService availability, IProductImageOptimizer optimizer, IStorefrontMediaStorage storage, IStorefrontFeedbackAttachmentScanner scanner)
{
    private bool IsOwner => currentUser.Roles.Contains("ApplicationOwner", StringComparer.Ordinal);

    public async Task<object?> Resolve(string slug, CancellationToken ct)
    {
        string normalized;
        try { normalized = PublicSlugPolicy.NormalizeRouteSegment(slug); }
        catch (ArgumentException) { return null; }
        var route = await db.PublicRoutes.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedSlug == normalized && x.RouteStatus == "ACTIVE", ct);
        if (route is null) return null;
        if (route.EntityType == "RETAILER" && route.TenantId is Guid tenantId)
        {
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IsActive, ct);
            return tenant is null ? null : new { Slug = normalized, Type = "RETAILER", StoreKey = tenant.TenantKey, CanonicalPath = "/" + normalized };
        }
        if (route.EntityType == "CITY" && route.CityId is Guid cityId)
        {
            var city = await db.MarketplaceCities.AsNoTracking().SingleOrDefaultAsync(x => x.CityId == cityId && x.IsActive && x.IsPublished, ct);
            return city is null ? null : new { Slug = normalized, Type = "CITY", CityId = city.CityId, CanonicalPath = "/" + normalized };
        }
        return null;
    }

    public async Task<object> City(Guid cityId, CancellationToken ct)
    {
        var city = await (from c in db.MarketplaceCities.AsNoTracking()
            join r in db.PublicRoutes.AsNoTracking() on c.CityId equals r.CityId
            where c.CityId == cityId && c.IsActive && c.IsPublished && r.RouteStatus == "ACTIVE"
            select new { c.CityId, c.CityName, c.StateName, c.MarketplaceName, c.Description, c.BannerHeadline, c.BannerSubtitle, c.SeoTitle, c.SeoDescription, Slug = r.OriginalSlug,
                LogoUrl=c.LogoMediaId.HasValue?$"/api/public/cities/{r.OriginalSlug}/media/logo?v={c.LogoMediaId.Value:N}":null,
                BannerUrl=c.BannerMediaId.HasValue?$"/api/public/cities/{r.OriginalSlug}/media/banner?v={c.BannerMediaId.Value:N}":null }).SingleOrDefaultAsync(ct);
        if (city is null) throw new KeyNotFoundException("Marketplace was not found.");
        var storeRows = await db.Database.SqlQuery<CityStoreRow>($"""
            SELECT t.TenantId,LOWER(t.TenantKey) StoreKey,COALESCE(NULLIF(LTRIM(RTRIM(t.DisplayName)),N''),t.Name) StoreName,
                   COALESCE(bt.Name,e.BusinessType) BusinessType,e.ShortDescription,e.OptedIn,e.OwnerApproved,
                   sc.LogoMediaId,m.Locality
            FROM commerce.CityRetailers m
            JOIN core.Tenants t ON t.TenantId=m.TenantId AND t.IsActive=1
            JOIN commerce.StoreDirectoryEntries e ON e.TenantId=t.TenantId AND e.OptedIn=1 AND e.OwnerApproved=1
            JOIN commerce.PublicRouteRegistry rr ON rr.TenantId=t.TenantId AND rr.EntityType='RETAILER' AND rr.RouteStatus='ACTIVE'
            LEFT JOIN core.BusinessTypes bt ON bt.Code=e.BusinessTypeCode AND bt.IsActive=1
            LEFT JOIN commerce.StorefrontConfigurations sc ON sc.TenantId=t.TenantId
            WHERE m.CityId={cityId} AND m.IsActive=1
            ORDER BY StoreName
            """).ToArrayAsync(ct);
        var eligibleStores = new List<CityStoreRow>();
        foreach (var row in storeRows)
            if (await features.IsEnabledAsync(row.TenantId, FeatureKeys.CityMarketplace, ct)) eligibleStores.Add(row);
        var stores = eligibleStores.Select(x => new { x.StoreKey, x.StoreName, x.BusinessType, x.ShortDescription, x.Locality, LogoUrl = x.LogoMediaId is Guid id ? $"/api/store/{Uri.EscapeDataString(x.StoreKey)}/presentation/logo?v={id:N}" : null }).ToArray();
        return new { City = city, Stores = stores };
    }

    public async Task<object> CityProducts(string slug, int page, int pageSize, string? search, string? categoryFilter, string? brandFilter, Guid? searchEventId, CancellationToken ct)
    {
        var resolved = await Resolve(slug, ct);
        var resolvedType = resolved?.GetType().GetProperty("Type")?.GetValue(resolved)?.ToString();
        var cityIdValue = resolved?.GetType().GetProperty("CityId")?.GetValue(resolved);
        if (resolvedType != "CITY" || cityIdValue is not Guid cityId) throw new KeyNotFoundException("Marketplace was not found.");
        page = Math.Clamp(page,1,100000); pageSize=Math.Clamp(pageSize,1,50);
        var term=string.IsNullOrWhiteSpace(search)?null:search.Trim();
        var categoryKey=NormalizeMarketplaceFacet(categoryFilter);
        var brandKey=NormalizeMarketplaceFacet(brandFilter);
        var mappedTenants=await db.MarketplaceRetailers.AsNoTracking().Where(x=>x.CityId==cityId&&x.IsActive).Select(x=>x.TenantId).Distinct().ToArrayAsync(ct);
        var entitledTenants=new List<Guid>();
        foreach(var tenantId in mappedTenants) if(await features.IsEnabledAsync(tenantId,FeatureKeys.CityMarketplace,ct)) entitledTenants.Add(tenantId);
        if(entitledTenants.Count==0)
        {
            await RecordCitySearch(cityId, searchEventId, term, 0, page, ct);
            return new { Items=Array.Empty<PublicCityProduct>(),PageNumber=page,PageSize=pageSize,TotalCount=0,HasMore=false,Categories=Array.Empty<object>(),Brands=Array.Empty<object>() };
        }
        var query = from mapping in db.MarketplaceRetailers.AsNoTracking()
            join city in db.MarketplaceCities.AsNoTracking() on mapping.CityId equals city.CityId
            join cityRoute in db.PublicRoutes.AsNoTracking() on city.CityId equals cityRoute.CityId
            join tenant in db.Tenants.AsNoTracking() on mapping.TenantId equals tenant.TenantId
            join directory in db.Set<StoreDirectoryEntry>().AsNoTracking() on tenant.TenantId equals directory.TenantId
            join storeRoute in db.PublicRoutes.AsNoTracking() on tenant.TenantId equals storeRoute.TenantId
            join product in db.Products.AsNoTracking() on tenant.TenantId equals product.TenantId
            join category in db.ProductCategories.AsNoTracking() on product.CategoryId equals category.ProductCategoryId
            join brand in db.Brands.AsNoTracking() on product.BrandId equals brand.BrandId
            join unit in db.UnitsOfMeasure.AsNoTracking() on product.UnitId equals unit.UnitId
            join listingJoin in db.PartnerProductListings.AsNoTracking() on new { TenantId=tenant.TenantId, product.ProductId } equals new { TenantId=listingJoin.OperatingTenantId, listingJoin.ProductId } into listings
            from listing in listings.DefaultIfEmpty()
            join sellerJoin in db.PartnerSellers.AsNoTracking() on new { TenantId=tenant.TenantId, SellerId=(Guid?)listing.PartnerSellerId } equals new { TenantId=sellerJoin.OperatingTenantId, SellerId=(Guid?)sellerJoin.PartnerSellerId } into sellers
            from seller in sellers.DefaultIfEmpty()
            where entitledTenants.Contains(tenant.TenantId) && mapping.CityId==cityId && mapping.IsActive && city.IsActive && city.IsPublished && cityRoute.RouteStatus=="ACTIVE"
                && tenant.IsActive && directory.OptedIn && directory.OwnerApproved && storeRoute.RouteStatus=="ACTIVE"
                && product.IsActive && !product.IsDeleted && product.IsWhatsAppVisible
                && ApplicationDbContext.IsProductPubliclyApproved(tenant.TenantId,product.ProductId)
                && category.IsActive && !category.IsDeleted && db.Set<TenantProductCategory>().Any(x=>x.TenantId==tenant.TenantId&&x.ProductCategoryId==product.CategoryId)
                && brand.IsActive && !brand.IsDeleted && db.Set<TenantBrand>().Any(x=>x.TenantId==tenant.TenantId&&x.BrandId==product.BrandId)
                && unit.IsActive && !unit.IsDeleted && db.Set<TenantUnitOfMeasure>().Any(x=>x.TenantId==tenant.TenantId&&x.UnitId==product.UnitId)
                && (listing==null || (listing.ListingStatus=="APPROVED" && seller!=null && seller.SellerStatus=="ACTIVE" && seller.VerificationStatus=="VERIFIED"))
            select new CityProductRow { Id=product.ProductId,TenantId=tenant.TenantId,StoreKey=tenant.TenantKey,StoreName=tenant.DisplayName??tenant.Name,CategoryId=product.CategoryId,CategoryName=category.CategoryName,BrandId=product.BrandId,BrandName=brand.BrandName,Name=product.ProductName,Code=product.ProductCode,Description=product.ShortDescription??"",Price=product.SellingPrice,Mrp=product.MRP,Gst=product.GSTPercentage,GstPricingType=product.GstPricingType,ImageId=db.ProductImages.Where(i=>i.TenantId==tenant.TenantId&&i.ProductId==product.ProductId&&i.IsActive&&!i.IsDeleted).OrderByDescending(i=>i.IsPrimary).Select(i=>(Guid?)i.ProductImageId).FirstOrDefault(),HasStock=db.InventoryBalances.Any(balance=>EF.Property<Guid?>(balance,"TenantId")==tenant.TenantId&&balance.ProductId==product.ProductId&&db.Warehouses.Any(w=>w.WarehouseId==balance.WarehouseId&&EF.Property<Guid?>(w,"TenantId")==tenant.TenantId&&w.IsActive&&!w.IsDeleted)&&balance.QuantityOnHand>balance.QuantityReserved),SellerId=listing==null?(Guid?)null:listing.PartnerSellerId,SellerName=seller==null?null:seller.SellerName};
        var facetQuery=query;
        if(categoryKey is not null) query=query.Where(x=>EF.Functions.Collate(x.CategoryName.Trim(),"Latin1_General_100_CI_AS")==categoryKey);
        if(brandKey is not null) query=query.Where(x=>EF.Functions.Collate(x.BrandName.Trim(),"Latin1_General_100_CI_AS")==brandKey);
        if(term is not null) query=query.Where(x=>x.Name.Contains(term)||x.Code.Contains(term)||x.Description.Contains(term)||x.CategoryName.Contains(term)||x.BrandName.Contains(term));
        var total=await query.CountAsync(ct);
        await RecordCitySearch(cityId, searchEventId, term, total, page, ct);
        var rows=await query.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        var availabilityByProduct=new Dictionary<(Guid Tenant,Guid Product),WhatsBiz.Application.Features.Storefront.StorefrontProductAvailability>();
        foreach(var group in rows.GroupBy(x=>x.TenantId))
            foreach(var item in await availability.GetProductAvailabilityAsync(group.Key,group.Select(x=>x.Id).ToArray(),ct)) availabilityByProduct[(group.Key,item.Key)]=item.Value;
        var items=rows.Select(x=>
        {
            availabilityByProduct.TryGetValue((x.TenantId,x.Id),out var state);
            var hasStock=x.HasStock;
            var canOrder=hasStock&&(state?.CanOrderNow??true);
            var status=!hasStock?"OUT_OF_STOCK":state?.Status??"IN_STOCK";
            return new PublicCityProduct(x.Id,x.TenantId,x.StoreKey,x.StoreName,x.CategoryId,x.CategoryName,x.BrandId,x.BrandName,x.Name,x.Description,
                x.ImageId.HasValue?$"/api/store/{Uri.EscapeDataString(x.StoreKey)}/products/{x.Id}/image":null,x.Price,x.Mrp>x.Price?x.Mrp:null,x.Gst,x.GstPricingType,status,canOrder,
                state?.CustomerMessage,x.SellerId is null&&canOrder,
                x.SellerId is not null?"Partner purchases are not available yet.":null,x.SellerId,x.SellerName);
        }).ToArray();
        var categoryCandidates=await facetQuery
            .Select(x=>new{x.TenantId,x.StoreKey,x.CategoryId,x.CategoryName,HasCategoryImage=db.StorefrontCategoryImages.Any(image=>image.TenantId==x.TenantId&&image.ProductCategoryId==x.CategoryId)})
            .Distinct().OrderByDescending(x=>x.HasCategoryImage).ThenBy(x=>x.CategoryName).ToArrayAsync(ct);
        var categoryOverrides=await db.CityMarketplaceMedia.AsNoTracking().Where(x=>x.CityId==cityId&&x.ResourceType=="CATEGORY"&&x.ResourceKey!=null).ToDictionaryAsync(x=>x.ResourceKey!,x=>x.MediaId,StringComparer.Ordinal,ct);
        var categoryCounts=await facetQuery.GroupBy(x=>new{x.TenantId,CategoryName=EF.Functions.Collate(x.CategoryName.Trim(),"Latin1_General_100_CI_AS")})
            .Select(group=>new{group.Key.CategoryName,ProductCount=group.Count()}).ToArrayAsync(ct);
        var countsByCategory=categoryCounts.GroupBy(x=>NormalizeMarketplaceFacet(x.CategoryName)!,StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.Sum(x=>x.ProductCount),StringComparer.Ordinal);
        var categories=categoryCandidates.Select(x=>new{Key=NormalizeMarketplaceFacet(x.CategoryName)!,x.CategoryName,x.StoreKey,x.CategoryId,x.HasCategoryImage})
            .GroupBy(x=>x.Key,StringComparer.Ordinal).Select(group=>{var image=group.FirstOrDefault(x=>x.HasCategoryImage);var hasOverride=categoryOverrides.TryGetValue(group.Key,out var overrideId);var imageUrl=hasOverride?$"/api/public/cities/{Uri.EscapeDataString(slug)}/media/categories/{CategoryMediaToken(group.Key)}?v={overrideId:N}":image is null?null:$"/api/store/{Uri.EscapeDataString(image.StoreKey)}/presentation/categories/{image.CategoryId}";return new{CategoryKey=group.Key,CategoryName=group.OrderBy(x=>x.CategoryName,StringComparer.OrdinalIgnoreCase).First().CategoryName,ProductCount=countsByCategory.GetValueOrDefault(group.Key),ImageUrl=imageUrl,HasCityImageOverride=hasOverride};})
            .OrderBy(x=>x.CategoryName).ToArray();
        var brandNames=await facetQuery.Select(x=>x.BrandName).Distinct().ToArrayAsync(ct);
        var brands=brandNames.Select(name=>new {BrandKey=NormalizeMarketplaceFacet(name)!,BrandName=name}).GroupBy(x=>x.BrandKey,StringComparer.Ordinal).Select(x=>x.OrderBy(y=>y.BrandName,StringComparer.OrdinalIgnoreCase).First()).OrderBy(x=>x.BrandName).ToArray();
        return new { Items=items, PageNumber=page,PageSize=pageSize,TotalCount=total,HasMore=page*pageSize<total,Categories=categories,Brands=brands };
    }

    private async Task RecordCitySearch(Guid cityId, Guid? eventId, string? term, int resultCount, int page, CancellationToken ct)
    {
        if (page != 1 || eventId is null || eventId == Guid.Empty || term is null || term.Length is < 2 or > 200) return;
        var normalized = SearchTermPrivacy.ForStorage(term);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS(SELECT 1 FROM commerce.CityMarketplaceSearchEvents WITH(UPDLOCK,HOLDLOCK) WHERE SearchEventId={eventId.Value})
                INSERT commerce.CityMarketplaceSearchEvents(SearchEventId,CityId,SearchContext,NormalizedKeyword,ResultCount)
                VALUES({eventId.Value},{cityId},'CITY',{normalized},{resultCount});
            """, ct);
    }

    private static string? NormalizeMarketplaceFacet(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        return value.Trim().ToLowerInvariant();
    }

    private async Task<Guid[]> CityCategoryDescendants(Guid cityId,Guid root,CancellationToken ct)
    {
        var rows=await (from m in db.MarketplaceRetailers
            join p in db.Products on m.TenantId equals p.TenantId
            join c in db.ProductCategories on p.CategoryId equals c.ProductCategoryId
            where m.CityId==cityId&&m.IsActive&&p.IsActive&&!p.IsDeleted&&c.IsActive&&!c.IsDeleted
            select new{c.ProductCategoryId,c.ParentCategoryId}).Distinct().ToArrayAsync(ct);
        var children=rows.Where(x=>x.ParentCategoryId.HasValue).GroupBy(x=>x.ParentCategoryId!.Value).ToDictionary(x=>x.Key,x=>x.Select(y=>y.ProductCategoryId).ToArray());
        var result=new HashSet<Guid>();void Visit(Guid id){if(!result.Add(id))return;if(children.TryGetValue(id,out var nested))foreach(var child in nested)Visit(child);}Visit(root);return result.ToArray();
    }

    public async Task<object[]> RetailerCities(CancellationToken ct)
    {
        var tenant=currentUser.TenantId??throw new UnauthorizedAccessException("Retailer context is required.");
        if(!await features.IsEnabledAsync(tenant,FeatureKeys.CityMarketplace,ct))throw new UnauthorizedAccessException("City Marketplace is not enabled for this retailer.");
        return await (from c in db.MarketplaceCities.AsNoTracking() join route in db.PublicRoutes.AsNoTracking() on c.CityId equals route.CityId
            join mapRow in db.MarketplaceRetailers.AsNoTracking().Where(x=>x.TenantId==tenant) on c.CityId equals mapRow.CityId into maps
            from map in maps.DefaultIfEmpty()
            where c.IsActive&&c.IsPublished&&route.RouteStatus=="ACTIVE"
            select (object)new{c.CityId,c.CityName,c.StateName,Slug=route.OriginalSlug,IsAssigned=map!=null&&map.IsActive,IsRequested=map!=null&&!map.IsActive,Locality=map==null?null:map.Locality,ServiceArea=map==null?null:map.ServiceArea,IsActive=map!=null&&map.IsActive,
                DirectoryOptedIn=db.Set<StoreDirectoryEntry>().Where(e=>e.TenantId==tenant).Select(e=>(bool?)e.OptedIn).FirstOrDefault()??false,
                DirectoryOwnerApproved=db.Set<StoreDirectoryEntry>().Where(e=>e.TenantId==tenant).Select(e=>(bool?)e.OwnerApproved).FirstOrDefault()??false}).ToArrayAsync(ct);
    }

    public async Task SaveRetailerCity(RetailerCityInput input,string actor,CancellationToken ct)
    {
        var tenant=currentUser.TenantId??throw new UnauthorizedAccessException("Retailer context is required.");
        if(!await features.IsEnabledAsync(tenant,FeatureKeys.CityMarketplace,ct))throw new UnauthorizedAccessException("City Marketplace is not enabled for this retailer.");
        if(!await db.MarketplaceCities.AnyAsync(c=>c.CityId==input.CityId&&c.IsActive&&c.IsPublished,ct))throw new ArgumentException("Choose an active published city.");
        var map=await db.MarketplaceRetailers.SingleOrDefaultAsync(x=>x.CityId==input.CityId&&x.TenantId==tenant,ct);
        if(!input.IsActive)
        {
            if(map?.IsActive==true)throw new UnauthorizedAccessException("An owner-approved city assignment can only be changed by the Application Owner.");
            if(map is not null){db.MarketplaceRetailers.Remove(map);await db.SaveChangesAsync(ct);}return;
        }
        if(map is null){map=new CityRetailer{CityId=input.CityId,TenantId=tenant,CreatedAt=DateTimeOffset.UtcNow,CreatedBy=actor,IsActive=false};db.MarketplaceRetailers.Add(map);}
        map.Locality=Limit(input.Locality,120);map.ServiceArea=Limit(input.ServiceArea,300);
        await db.SaveChangesAsync(ct);
    }

    public async Task<object?> CityBySlug(string slug, CancellationToken ct)
    {
        var resolved = await Resolve(slug, ct);
        if (resolved is null) return null;
        var type = resolved.GetType().GetProperty("Type")?.GetValue(resolved)?.ToString();
        var id = resolved.GetType().GetProperty("CityId")?.GetValue(resolved);
        return type == "CITY" && id is Guid cityId ? await City(cityId, ct) : null;
    }

    public async Task<IReadOnlyList<object>> OwnerCities(CancellationToken ct)
    {
        EnsureOwner();
        return await (from c in db.MarketplaceCities.AsNoTracking()
            join r in db.PublicRoutes.AsNoTracking() on c.CityId equals r.CityId
            orderby c.CityName
            select (object)new { c.CityId, c.CityName, c.StateName, Slug = r.OriginalSlug, c.MarketplaceName, c.Description, c.BannerHeadline, c.BannerSubtitle,
                LogoUrl=c.LogoMediaId.HasValue?$"/api/public/cities/{r.OriginalSlug}/media/logo?v={c.LogoMediaId.Value:N}":null,
                BannerUrl=c.BannerMediaId.HasValue?$"/api/public/cities/{r.OriginalSlug}/media/banner?v={c.BannerMediaId.Value:N}":null,
                c.SeoTitle, c.SeoDescription, c.ExternalMarketingDomain, c.IsActive, c.IsPublished, Version = Convert.ToBase64String(c.Version) }).ToArrayAsync(ct);
    }

    public async Task<object> SaveCity(Guid? cityId, CityInput input, string actor, CancellationToken ct)
    {
        EnsureOwner();
        string slug;
        try { slug = PublicSlugPolicy.Normalize(input.Slug); }
        catch (ArgumentException exception) { throw new ValidationException([new ValidationFailure(nameof(CityInput.Slug), exception.Message)]); }
        var requiredErrors = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(input.CityName)) requiredErrors.Add(new(nameof(CityInput.CityName), "City name is required."));
        if (string.IsNullOrWhiteSpace(input.StateName)) requiredErrors.Add(new(nameof(CityInput.StateName), "State is required."));
        if (string.IsNullOrWhiteSpace(input.MarketplaceName)) requiredErrors.Add(new(nameof(CityInput.MarketplaceName), "Marketplace display name is required."));
        if (input.CityName?.Trim().Length > 120) requiredErrors.Add(new(nameof(CityInput.CityName), "City name must be 120 characters or fewer."));
        if (input.StateName?.Trim().Length > 120) requiredErrors.Add(new(nameof(CityInput.StateName), "State must be 120 characters or fewer."));
        if (input.MarketplaceName?.Trim().Length > 160) requiredErrors.Add(new(nameof(CityInput.MarketplaceName), "Marketplace display name must be 160 characters or fewer."));
        if (input.BannerHeadline?.Trim().Length > 120) requiredErrors.Add(new(nameof(CityInput.BannerHeadline), "Banner headline must be 120 characters or fewer."));
        if (input.BannerSubtitle?.Trim().Length > 240) requiredErrors.Add(new(nameof(CityInput.BannerSubtitle), "Banner subtitle must be 240 characters or fewer."));
        if (requiredErrors.Count != 0) throw new ValidationException(requiredErrors);
        var cityName = input.CityName!.Trim();
        var stateName = input.StateName!.Trim();
        var marketplaceName = input.MarketplaceName!.Trim();
        if (input.IsPublished && !input.IsActive) throw new ValidationException([new ValidationFailure(nameof(CityInput.IsPublished), "An inactive city cannot be published.")]);
        if (cityId is null && input.IsPublished) throw new ValidationException([new ValidationFailure(nameof(CityInput.IsPublished), "Create the city as unpublished, then review and publish it explicitly.")]);
        string? marketingDomain;
        try { marketingDomain = NormalizeHttpsHost(input.ExternalMarketingDomain); }
        catch (ArgumentException exception) { throw new ValidationException([new ValidationFailure(nameof(CityInput.ExternalMarketingDomain), exception.Message)]); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        MarketplaceCity city;
        PublicRoute route;
        if (cityId is Guid id)
        {
            city = await db.MarketplaceCities.SingleOrDefaultAsync(x => x.CityId == id, ct) ?? throw new KeyNotFoundException("City was not found.");
            if (input.Version is null || !city.Version.SequenceEqual(Convert.FromBase64String(input.Version))) throw new DbUpdateConcurrencyException("City changed; reload before saving.");
            route = await db.PublicRoutes.SingleAsync(x => x.CityId == id, ct);
        }
        else
        {
            city = new MarketplaceCity { CityId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, CreatedBy = actor };
            route = new PublicRoute { RouteId = Guid.NewGuid(), CityId = city.CityId, EntityType = "CITY", RouteStatus = "INACTIVE", City = city };
            db.MarketplaceCities.Add(city); db.PublicRoutes.Add(route);
        }
        if (cityId.HasValue && !string.Equals(route.NormalizedSlug, slug, StringComparison.Ordinal))
            throw new ValidationException([new ValidationFailure(nameof(CityInput.Slug), "A city slug cannot be changed in place. A reviewed alias/redirect workflow is required first.")]);
        var conflict = await db.PublicRoutes.AnyAsync(x => x.NormalizedSlug == slug && x.RouteId != route.RouteId, ct);
        if (conflict) throw new WhatsBiz.Application.Common.Exceptions.BusinessRuleException("That public route is already reserved by a city or retailer. Choose a different slug.");
        city.CityName = cityName; city.StateName = stateName; city.MarketplaceName = marketplaceName;
        city.Description = Limit(input.Description, 500); city.BannerHeadline = Limit(input.BannerHeadline, 120); city.BannerSubtitle = Limit(input.BannerSubtitle, 240); city.SeoTitle = Limit(input.SeoTitle, 180); city.SeoDescription = Limit(input.SeoDescription, 320);
        city.ExternalMarketingDomain = marketingDomain;
        city.IsActive = input.IsActive; city.IsPublished = input.IsPublished;
        city.UpdatedAt = cityId.HasValue ? DateTimeOffset.UtcNow : null; city.UpdatedBy = cityId.HasValue ? actor : null;
        route.OriginalSlug = slug; route.RouteStatus = input.IsActive && input.IsPublished ? "ACTIVE" : input.IsActive ? "UNPUBLISHED" : "INACTIVE";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { city.CityId, city.CityName, city.StateName, Slug = route.OriginalSlug, city.MarketplaceName, city.IsActive, city.IsPublished, Version = Convert.ToBase64String(city.Version), actor };
    }

    public async Task<object[]> OwnerCityRetailers(Guid cityId, CancellationToken ct)
    {
        EnsureOwner();
        if (!await db.MarketplaceCities.AnyAsync(x => x.CityId == cityId, ct)) throw new KeyNotFoundException("City was not found.");
        var candidates = await (from tenant in db.Tenants.AsNoTracking()
            join directory in db.Set<StoreDirectoryEntry>().AsNoTracking() on tenant.TenantId equals directory.TenantId
            join mapping in db.MarketplaceRetailers.AsNoTracking().Where(x => x.CityId == cityId) on tenant.TenantId equals mapping.TenantId into maps
            from mapping in maps.DefaultIfEmpty()
            where tenant.IsActive && directory.OptedIn && directory.OwnerApproved
            orderby tenant.Name
            select new { tenant.TenantId, StoreName = tenant.DisplayName ?? tenant.Name, tenant.TenantKey,
                IsAssigned = mapping != null && mapping.IsActive, IsRequested = mapping != null && !mapping.IsActive,
                mapping.Locality, mapping.ServiceArea, IsActive = mapping != null && mapping.IsActive }).ToArrayAsync(ct);
        var eligible = new List<object>();
        foreach (var retailer in candidates)
            if (await features.IsEnabledAsync(retailer.TenantId, FeatureKeys.CityMarketplace, ct)) eligible.Add(retailer);
        return eligible.ToArray();
    }

    public async Task UploadCityMedia(Guid cityId,string kind,string fileName,Stream content,CancellationToken ct)
    {
        EnsureOwner();var resource=NormalizeCityMediaKind(kind);
        await SaveCityMedia(cityId,resource,null,fileName,content,ct);
    }

    public async Task UploadCityCategoryMedia(Guid cityId,string categoryKey,string fileName,Stream content,CancellationToken ct)
    {
        EnsureOwner();var key=DecodeCategoryMediaToken(categoryKey);
        if(key is null||key.Length>160||key.Any(char.IsControl))throw new ArgumentException("Choose a valid eligible city category.");
        // Only categories represented by currently public, eligible city products may receive public media.
        var productPage=await EligibleCityCategoryKeys(cityId,ct);
        if(!productPage.Contains(key,StringComparer.Ordinal))throw new ArgumentException("The category is not currently eligible in this marketplace.");
        await SaveCityMedia(cityId,"CATEGORY",key,fileName,content,ct);
    }

    private async Task SaveCityMedia(Guid cityId,string resource,string? resourceKey,string fileName,Stream content,CancellationToken ct)
    {
        _=await db.MarketplaceCities.SingleOrDefaultAsync(x=>x.CityId==cityId,ct)??throw new KeyNotFoundException("City was not found.");
        await using var input=new MemoryStream();await content.CopyToAsync(input,ct);
        StorefrontAttachmentScanResult scan;
        try { scan=await scanner.ScanAsync(input.ToArray(),ct); }
        catch(Exception exception) when(exception is not OperationCanceledException) { throw new WhatsBiz.Application.Common.Exceptions.BusinessRuleException("The image could not be security-scanned and was not published."); }
        StorefrontProductImageScanGuard.RequireClean(scan);
        var image=await optimizer.OptimizeAsync(fileName,null,input.ToArray(),ct);var mediaId=Guid.NewGuid();
        var stored=await storage.StoreStorefrontAsync(new(cityId,"city",mediaId,image.CatalogData,image.ThumbnailData,image.ContentType),ct);
        var dbStorage=stored.Provider.Equals(ProductImageStorageProviders.Database,StringComparison.OrdinalIgnoreCase);
        var row=new CityMarketplaceMedia{MediaId=mediaId,CityId=cityId,ResourceType=resource,ResourceKey=resourceKey,FileName=image.FileName,ContentType=image.ContentType,ThumbnailContentType="image/webp",StorageProvider=stored.Provider,ObjectKey=stored.ObjectKey,ThumbnailObjectKey=stored.ThumbnailObjectKey,ImageData=dbStorage?image.CatalogData:null,ThumbnailData=dbStorage?image.ThumbnailData:null,ContentHash=stored.ContentHash,CreatedAt=DateTime.UtcNow,CreatedBy=currentUser.UserId?.ToString()??"owner"};
        CityMarketplaceMedia? old=null;
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var city=await db.MarketplaceCities.SingleAsync(x=>x.CityId==cityId,ct);
            old=await db.CityMarketplaceMedia.SingleOrDefaultAsync(x=>x.CityId==cityId&&x.ResourceType==resource&&x.ResourceKey==resourceKey,ct);
            if(old is not null){db.CityMarketplaceMedia.Remove(old);await db.SaveChangesAsync(ct);}db.CityMarketplaceMedia.Add(row);
            if(resource!="CATEGORY"){if(resource=="LOGO")city.LogoMediaId=mediaId;else city.BannerMediaId=mediaId;city.UpdatedAt=DateTimeOffset.UtcNow;city.UpdatedBy=currentUser.UserId?.ToString();}
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        catch{await storage.DeleteStorefrontAsync(new(cityId,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),CancellationToken.None);throw;}
        if(old is not null)await storage.DeleteStorefrontAsync(new(cityId,old.StorageProvider,old.ObjectKey,old.ThumbnailObjectKey),ct);
    }

    public async Task RemoveCityMedia(Guid cityId,string kind,CancellationToken ct)
    {
        EnsureOwner();var resource=NormalizeCityMediaKind(kind);var city=await db.MarketplaceCities.SingleOrDefaultAsync(x=>x.CityId==cityId,ct)??throw new KeyNotFoundException("City was not found.");
        await RemoveCityMediaRow(cityId,resource,null,city,ct);
    }

    public async Task RemoveCityCategoryMedia(Guid cityId,string categoryKey,CancellationToken ct)
    {
        EnsureOwner();var key=DecodeCategoryMediaToken(categoryKey);if(key is null||key.Length>160)throw new ArgumentException("Choose a valid category key.");
        var city=await db.MarketplaceCities.SingleOrDefaultAsync(x=>x.CityId==cityId,ct)??throw new KeyNotFoundException("City was not found.");
        await RemoveCityMediaRow(cityId,"CATEGORY",key,city,ct);
    }

    private async Task RemoveCityMediaRow(Guid cityId,string resource,string? resourceKey,MarketplaceCity city,CancellationToken ct)
    {
        var row=await db.CityMarketplaceMedia.SingleOrDefaultAsync(x=>x.CityId==cityId&&x.ResourceType==resource&&x.ResourceKey==resourceKey,ct);if(row is null)return;
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct))
        {db.CityMarketplaceMedia.Remove(row);if(resource=="LOGO")city.LogoMediaId=null;else if(resource=="BANNER")city.BannerMediaId=null;if(resource!="CATEGORY"){city.UpdatedAt=DateTimeOffset.UtcNow;city.UpdatedBy=currentUser.UserId?.ToString();}await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        await storage.DeleteStorefrontAsync(new(cityId,row.StorageProvider,row.ObjectKey,row.ThumbnailObjectKey),ct);
    }

    public Task<StorefrontImage?> PublicCityMedia(string slug,string kind,CancellationToken ct)=>PublicCityMedia(slug,kind,null,ct);

    public async Task<StorefrontImage?> PublicCityMedia(string slug,string kind,string? requestedCategoryKey,CancellationToken ct)
    {
        string normalized;try{normalized=PublicSlugPolicy.Normalize(slug);}catch(ArgumentException){return null;}var resource=kind.Trim().ToUpperInvariant();if(resource is not ("LOGO" or "BANNER" or "CATEGORY"))return null;
        var categoryKey=resource=="CATEGORY"?DecodeCategoryMediaToken(requestedCategoryKey):null;
        if(resource=="CATEGORY")
        {
            if(categoryKey is null||categoryKey.Length>160)return null;
        }
        var row=await (from c in db.MarketplaceCities.AsNoTracking() join route in db.PublicRoutes.AsNoTracking() on c.CityId equals route.CityId join media in db.CityMarketplaceMedia.AsNoTracking() on c.CityId equals media.CityId
            where route.NormalizedSlug==normalized&&route.RouteStatus=="ACTIVE"&&c.IsActive&&c.IsPublished&&media.ResourceType==resource&&media.ResourceKey==categoryKey&&(resource=="CATEGORY"||(resource=="LOGO"?c.LogoMediaId==media.MediaId:c.BannerMediaId==media.MediaId))
            select media).SingleOrDefaultAsync(ct);if(row is null)return null;
        var content=await storage.ReadStorefrontAsync(new(row.CityId,row.StorageProvider,row.ObjectKey,row.ImageData??[],row.ContentType),ct);return content is null?null:new StorefrontImage(content.ContentType,content.Content);
    }

    private async Task<string[]> EligibleCityCategoryKeys(Guid cityId,CancellationToken ct)
    {
        var slug=await db.PublicRoutes.AsNoTracking().Where(x=>x.CityId==cityId&&x.EntityType=="CITY"&&x.RouteStatus=="ACTIVE").Select(x=>x.NormalizedSlug).SingleOrDefaultAsync(ct);
        if(slug is null) return [];
        var result=await CityProducts(slug,1,1,null,null,null,null,ct);
        var categories=result.GetType().GetProperty("Categories")?.GetValue(result) as System.Collections.IEnumerable;
        if(categories is null)return [];
        return categories.Cast<object>().Select(x=>x.GetType().GetProperty("CategoryKey")?.GetValue(x)?.ToString()).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).ToArray();
    }

    private static string CategoryMediaToken(string key)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string? DecodeCategoryMediaToken(string? token)
    {
        if(string.IsNullOrWhiteSpace(token)||token.Length>860)return null;
        try{var value=token.Replace('-','+').Replace('_','/');value+=new string('=',(4-value.Length%4)%4);return NormalizeMarketplaceFacet(Encoding.UTF8.GetString(Convert.FromBase64String(value)));}
        catch(FormatException){return null;}
    }

    private static string NormalizeCityMediaKind(string value)=>value.Trim().ToUpperInvariant() switch{"LOGO"=>"LOGO","BANNER"=>"BANNER",_=>throw new ArgumentException("Choose logo or banner.")};

    public async Task SaveOwnerCityRetailer(Guid cityId, Guid tenantId, bool assigned, string? locality, string? serviceArea, CancellationToken ct)
    {
        EnsureOwner();
        var city = await db.MarketplaceCities.SingleOrDefaultAsync(x => x.CityId == cityId, ct) ?? throw new KeyNotFoundException("City was not found.");
        var map = await db.MarketplaceRetailers.SingleOrDefaultAsync(x => x.CityId == cityId && x.TenantId == tenantId, ct);
        if (!assigned) { if (map is not null) db.MarketplaceRetailers.Remove(map); await db.SaveChangesAsync(ct); return; }
        var eligible = await (from tenant in db.Tenants join directory in db.Set<StoreDirectoryEntry>() on tenant.TenantId equals directory.TenantId
            where tenant.TenantId == tenantId && tenant.IsActive && directory.OptedIn && directory.OwnerApproved select tenant.TenantId).AnyAsync(ct);
        if (!eligible) throw new ArgumentException("Only active retailers with an opted-in, owner-approved directory listing can be assigned.");
        if (!await features.IsEnabledAsync(tenantId, FeatureKeys.CityMarketplace, ct)) throw new ArgumentException("The retailer does not have effective City Marketplace access.");
        if (map is null) { map = new CityRetailer { CityId=cityId, TenantId=tenantId, IsActive=false, CreatedAt=DateTimeOffset.UtcNow }; db.MarketplaceRetailers.Add(map); }
        map.Locality=Limit(locality,120); map.ServiceArea=Limit(serviceArea,300); map.IsActive=city.IsActive && city.IsPublished;
        await db.SaveChangesAsync(ct);
    }

    private void EnsureOwner() { if (!IsOwner) throw new UnauthorizedAccessException("Application Owner access required."); }
    private static string? Limit(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, length)];
    private static string? NormalizeHttpsHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Enter a hostname for a verified HTTPS marketing domain.");
        return uri.IdnHost.ToLowerInvariant();
    }
    public sealed class CityStoreRow
    {
        public Guid TenantId { get; set; } public string StoreKey { get; set; } = ""; public string StoreName { get; set; } = "";
        public string BusinessType { get; set; } = ""; public string ShortDescription { get; set; } = ""; public string? Locality { get; set; }
        public bool OptedIn { get; set; } public bool OwnerApproved { get; set; } public Guid? LogoMediaId { get; set; }
    }
    public sealed class CityProductRow
    {
        public Guid Id{get;set;} public Guid TenantId{get;set;} public string StoreKey{get;set;}="";public string StoreName{get;set;}="";public Guid CategoryId{get;set;}public string CategoryName{get;set;}="";public Guid BrandId{get;set;}public string BrandName{get;set;}="";public string Name{get;set;}="";public string Code{get;set;}="";public string Description{get;set;}="";public decimal Price{get;set;}public decimal Mrp{get;set;}public decimal Gst{get;set;}public string GstPricingType{get;set;}="EXCLUSIVE";public Guid? ImageId{get;set;}public bool HasStock{get;set;}public Guid? SellerId{get;set;}public string? SellerName{get;set;}
    }
}
