using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class PublicCatalogSqlIntegrationTests
{
    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task MigrationRerunApprovalEditsImagesAndRevocationUntilRollback()
    {
        await using var connection=new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        connection.Database.Should().Be("WhatsBizERP_PosGstIntegrationTest");
        await using(var identity=new SqlCommand("SELECT @@SERVERNAME",connection))
            (await identity.ExecuteScalarAsync()).Should().Be("DESKTOP-DQ0868S");
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"database/migrations/V72-PublicCatalogGovernance.sql")))root=root.Parent;
        root.Should().NotBeNull();
        var tenant=Guid.NewGuid();var other=Guid.NewGuid();var legacyRouteTenant=Guid.NewGuid();var product=Guid.NewGuid();var legacy=Guid.NewGuid();
        var categoryId=Guid.NewGuid();var brandId=Guid.NewGuid();var unitId=Guid.NewGuid();var cityId=Guid.NewGuid();var strictCityId=Guid.NewGuid();var partnerId=Guid.NewGuid();var mediaId=Guid.NewGuid();
        Exception? fixtureFailure=null;Exception? cleanupFailure=null;
        const string schemaStateSql="SELECT CONCAT(OBJECT_ID(N'commerce.ProductPublications'),':',OBJECT_ID(N'commerce.PublicCatalogAudit'),':',OBJECT_ID(N'commerce.StoreDirectoryEntries'),':',OBJECT_ID(N'commerce.PublicCatalogBaseline'),':',OBJECT_ID(N'commerce.PublicRouteRegistry'),':',OBJECT_ID(N'commerce.Cities'),':',OBJECT_ID(N'commerce.CityRetailers'),':',OBJECT_ID(N'commerce.PartnerSellers'),':',OBJECT_ID(N'commerce.PartnerProductListings'),':',OBJECT_ID(N'commerce.CityMarketplaceMedia'),':',(SELECT COUNT(*) FROM sys.indexes WHERE name IN(N'UX_Products_PublicationTenant',N'IX_ProductImages_PublicContent',N'IX_PublicCatalogAudit_Product',N'IX_StoreDirectoryEntries_Publication',N'UX_PublicRouteRegistry_NormalizedSlug',N'IX_CityMarketplaceMedia_City')))";
        string schemaBefore;
        await using(var before=new SqlCommand(schemaStateSql,connection))schemaBefore=(string)(await before.ExecuteScalarAsync())!;
        await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                async Task Execute(string text)
                {
                    foreach(var batch in Regex.Split(text,@"(?im)^\s*GO\s*(?:--.*)?$"))
                        if(!string.IsNullOrWhiteSpace(batch)){await using var cmd=new SqlCommand(batch,connection,transaction);await cmd.ExecuteNonQueryAsync();}
                }
                async Task Migration(string name)=>await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations",name+".sql")));
                foreach(var name in new[]{"V67-StorefrontScheduledAvailability","V68-StorefrontFeaturedCategories","V69-StorefrontPopupCampaigns","V70-WhatsAppProductSharing","V71-StorefrontFeatureEntitlements"})await Migration(name);
                await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
                await db.Database.UseTransactionAsync(transaction);
                db.AddRange(new Tenant{TenantId=tenant,TenantKey="catalog-"+tenant.ToString("N"),Name="Catalog fixture",IsActive=true},new Tenant{TenantId=other,TenantKey="catalog-"+other.ToString("N"),Name="Other fixture",IsActive=true},new Tenant{TenantId=legacyRouteTenant,TenantKey="catalog_legacy-"+legacyRouteTenant.ToString("N"),Name="Legacy route fixture",IsActive=true});
                var category=new ProductCategory{ProductCategoryId=categoryId,CategoryName="Catalog fixture",CategoryCode=Guid.NewGuid().ToString("N")};
                var brand=new Brand{BrandId=brandId,BrandName="Fixture",BrandCode=Guid.NewGuid().ToString("N")};
                var unit=new UnitOfMeasure{UnitId=unitId,UnitName="Each",UnitCode="T"+Guid.NewGuid().ToString("N")[..10],ShortName="ea"};
                db.AddRange(category,brand,unit);await db.SaveChangesAsync();
                Product NewProduct(Guid id)=>new(){ProductId=id,TenantId=tenant,ProductName="Review fixture",ProductCode=Guid.NewGuid().ToString("N"),CategoryId=category.ProductCategoryId,BrandId=brand.BrandId,UnitId=unit.UnitId,SellingPrice=100,PurchasePrice=10,MRP=120,GSTPercentage=18};
                db.Add(NewProduct(legacy));await db.SaveChangesAsync();
                await Migration("V72-PublicCatalogGovernance");
                await using(var legacyPublication=new SqlCommand("INSERT commerce.ProductPublications(TenantId,ProductId,Status,ContentHash) VALUES(@tenant,@product,'LEGACY',commerce.ProductPublicContentHash(@tenant,@product));",connection,transaction))
                {legacyPublication.Parameters.AddWithValue("@tenant",tenant);legacyPublication.Parameters.AddWithValue("@product",legacy);await legacyPublication.ExecuteNonQueryAsync();}
                await Migration("V73-RetailerBusinessTypes");
                await Migration("V74-CityMarketplaceAndPartnerSellers");
                await Migration("V75-CityMarketplaceMedia");
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/validators/V74-CityMarketplaceAndPartnerSellers.validate.sql")));
                await Execute(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/validators/V75-CityMarketplaceMedia.validate.sql")));
                await Migration("V74-CityMarketplaceAndPartnerSellers");
                await Migration("V75-CityMarketplaceMedia");
                await using(var legacyRoute=new SqlCommand("SELECT CONCAT(OriginalSlug,N'|',NormalizedSlug) FROM commerce.PublicRouteRegistry WHERE TenantId=@tenant AND EntityType='RETAILER'",connection,transaction))
                {legacyRoute.Parameters.AddWithValue("@tenant",legacyRouteTenant);(await legacyRoute.ExecuteScalarAsync()).Should().Be("catalog_legacy-"+legacyRouteTenant.ToString("N")+"|catalog_legacy-"+legacyRouteTenant.ToString("N"));}
                await using(var city=new SqlCommand("INSERT commerce.Cities(CityId,CityName,StateName,MarketplaceName,IsActive,IsPublished,CreatedBy) VALUES(@city,N'Fixture City',N'Fixture State',N'Fixture Marketplace',1,1,N'sql-test'); INSERT commerce.PublicRouteRegistry(OriginalSlug,EntityType,CityId,RouteStatus,CreatedBy) VALUES(N'fixture-city','CITY',@city,'ACTIVE',N'sql-test');",connection,transaction))
                {city.Parameters.AddWithValue("@city",cityId);await city.ExecuteNonQueryAsync();}
                await using(var strictCitySlug=new SqlCommand("INSERT commerce.Cities(CityId,CityName,StateName,MarketplaceName,IsActive,IsPublished,CreatedBy) VALUES(@city,N'Strict Fixture City',N'Fixture State',N'Strict Fixture Marketplace',1,1,N'sql-test'); SET XACT_ABORT OFF; DECLARE @rejected bit=0; BEGIN TRY INSERT commerce.PublicRouteRegistry(OriginalSlug,EntityType,CityId,RouteStatus,CreatedBy) VALUES(N'fixture_city','CITY',@city,'ACTIVE',N'sql-test'); END TRY BEGIN CATCH IF ERROR_NUMBER()<>547 THROW; SET @rejected=1; END CATCH; SELECT @rejected;",connection,transaction))
                {strictCitySlug.Parameters.AddWithValue("@city",strictCityId);Convert.ToBoolean(await strictCitySlug.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().BeTrue("city slugs remain strict");}
                await using(var duplicateCitySlug=new SqlCommand("SET XACT_ABORT OFF; DECLARE @rejected bit=0; BEGIN TRY INSERT commerce.PublicRouteRegistry(OriginalSlug,EntityType,CityId,RouteStatus,CreatedBy) VALUES(N'FIXTURE-CITY','CITY',@city,'ACTIVE',N'sql-test'); END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN(2601,2627) THROW; SET @rejected=1; END CATCH; SELECT @rejected;",connection,transaction))
                {duplicateCitySlug.Parameters.AddWithValue("@city",strictCityId);Convert.ToBoolean(await duplicateCitySlug.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().BeTrue("public route uniqueness is case-insensitive across entities");}
                // Tenant category/unit ownership remains a separate requirement.
                await using(var maps=new SqlCommand("INSERT master.TenantProductCategories(TenantId,ProductCategoryId) VALUES(@tenant,@category);INSERT master.TenantBrands(TenantId,BrandId) VALUES(@tenant,@brand);INSERT master.TenantUnitsOfMeasure(TenantId,UnitId) VALUES(@tenant,@unit)",connection,transaction))
                {maps.Parameters.AddWithValue("@tenant",tenant);maps.Parameters.AddWithValue("@category",category.ProductCategoryId);maps.Parameters.AddWithValue("@brand",brand.BrandId);maps.Parameters.AddWithValue("@unit",unit.UnitId);await maps.ExecuteNonQueryAsync();}
                db.Add(NewProduct(product));await db.SaveChangesAsync();
                db.PartnerSellers.Add(new PartnerSeller{PartnerSellerId=partnerId,OperatingTenantId=tenant,SellerName="Fixture partner",SellerType="BUSINESS",CityId=cityId,VerificationStatus="PENDING",SellerStatus="ACTIVE",CreatedAt=DateTimeOffset.UtcNow,CreatedBy="sql-test"});await db.SaveChangesAsync();
                await using(var pendingListing=new SqlCommand("INSERT commerce.PartnerProductListings(OperatingTenantId,ProductId,PartnerSellerId,ListingStatus,CreatedBy) VALUES(@tenant,@product,@seller,'PENDING_APPROVAL',N'sql-test'); SELECT ListingStatus FROM commerce.PartnerProductListings WHERE OperatingTenantId=@tenant AND ProductId=@product;",connection,transaction))
                {pendingListing.Parameters.AddWithValue("@tenant",tenant);pendingListing.Parameters.AddWithValue("@product",product);pendingListing.Parameters.AddWithValue("@seller",partnerId);(await pendingListing.ExecuteScalarAsync()).Should().Be("PENDING_APPROVAL");}
                await using(var media=new SqlCommand("INSERT commerce.CityMarketplaceMedia(MediaId,CityId,ResourceType,FileName,ContentType,ThumbnailContentType,StorageProvider,ObjectKey,ThumbnailObjectKey,ImageData,ThumbnailData,ContentHash,CreatedBy) VALUES(@media,@city,'LOGO',N'fixture.webp',N'image/webp',N'image/webp',N'DATABASE',NULL,NULL,0x01,0x01,REPLICATE('a',64),N'sql-test'); UPDATE commerce.Cities SET LogoMediaId=@media WHERE CityId=@city;",connection,transaction))
                {media.Parameters.AddWithValue("@media",mediaId);media.Parameters.AddWithValue("@city",cityId);await media.ExecuteNonQueryAsync();}
                await using(var mediaConstraint=new SqlCommand("SET XACT_ABORT OFF; DECLARE @rejected bit=0; BEGIN TRY INSERT commerce.CityMarketplaceMedia(MediaId,CityId,ResourceType,FileName,ContentType,ThumbnailContentType,StorageProvider,ImageData,ThumbnailData,ContentHash,CreatedBy) VALUES(NEWID(),@city,'LOGO',N'invalid.webp',N'image/webp',N'image/webp',N'DATABASE',0x01,0x01,REPLICATE('b',64),N'sql-test'); END TRY BEGIN CATCH IF ERROR_NUMBER()<>547 THROW; SET @rejected=1; END CATCH; SELECT @rejected;",connection,transaction))
                {mediaConstraint.Parameters.AddWithValue("@city",Guid.NewGuid());Convert.ToBoolean(await mediaConstraint.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().BeTrue("city media cannot reference a different or nonexistent city");}
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/validators/V75-CityMarketplaceMedia.validate.sql")));
                await using(var partnerIsolation=new SqlCommand("SET XACT_ABORT OFF; DECLARE @rejected bit=0; BEGIN TRY INSERT commerce.PartnerProductListings(OperatingTenantId,ProductId,PartnerSellerId,ListingStatus,CreatedBy) VALUES(@other,@product,@seller,'PENDING_APPROVAL',N'sql-test'); END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN(547,2601,2627) THROW; SET @rejected=1; END CATCH; SELECT @rejected;",connection,transaction))
                {partnerIsolation.Parameters.AddWithValue("@other",other);partnerIsolation.Parameters.AddWithValue("@product",product);partnerIsolation.Parameters.AddWithValue("@seller",partnerId);Convert.ToBoolean(await partnerIsolation.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().BeTrue("a listing cannot cross operating-tenant ownership");}
                await using(var removePartnerFixture=new SqlCommand("DELETE commerce.PartnerProductListings WHERE OperatingTenantId=@tenant AND ProductId=@product",connection,transaction))
                {removePartnerFixture.Parameters.AddWithValue("@tenant",tenant);removePartnerFixture.Parameters.AddWithValue("@product",product);await removePartnerFixture.ExecuteNonQueryAsync();}
                await Migration("V72-PublicCatalogGovernance");
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/validators/V72-PublicCatalogGovernance.validate.sql")));
                async Task<bool> Public(Guid id)=>await db.Products.Where(p=>p.TenantId==tenant&&p.ProductId==id).Select(p=>ApplicationDbContext.IsProductPubliclyApproved(tenant,id)).SingleAsync();
                var postingSource=await File.ReadAllTextAsync(Path.Combine(root!.FullName,"backend/src/WhatsBiz.Infrastructure/POS/POSEngine.cs"));
                var postingSql=Regex.Match(postingSource,"\"\"\"\\s*(DECLARE @ids[\\s\\S]*?)\"\"\"").Groups[1].Value;
                postingSql.Should().NotBeEmpty();
                var checkoutSource=await File.ReadAllTextAsync(Path.Combine(root.FullName,"backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs"));
                var pricingSql=Regex.Match(checkoutSource,"\"\"\"\\s*(SELECT p.ProductId,j.Quantity[\\s\\S]*?)\"\"\"").Groups[1].Value;
                pricingSql.Should().NotBeEmpty();
                async Task<int> FinalRejections(Guid effectiveTenant)
                {
                    await using var command=new SqlCommand(postingSql,connection,transaction);
                    command.Parameters.AddWithValue("@tenant",effectiveTenant);command.Parameters.AddWithValue("@items",JsonSerializer.Serialize(new[]{new{ProductId=product,Quantity=1}}));
                    return Convert.ToInt32(await command.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture);
                }
                async Task<int> PricingRows()
                {
                    await using var command=new SqlCommand(pricingSql,connection,transaction);
                    command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@items",JsonSerializer.Serialize(new[]{new{ProductId=product,Quantity=1}}));
                    await using var reader=await command.ExecuteReaderAsync();var rows=0;while(await reader.ReadAsync())rows++;return rows;
                }
                (await Public(legacy)).Should().BeTrue("a fresh legacy baseline must preserve existing catalog visibility");
                (await Public(product)).Should().BeFalse("reruns must never grandfather new products");
                var store=new StorefrontService(db,null!);var storeKey="catalog-"+tenant.ToString("N");
                (await store.GetProductAsync(storeKey,product,default)).Should().BeNull();
                (await store.GetProductImageAsync(storeKey,product,null,false,default)).Should().BeNull();
                (await PricingRows()).Should().Be(0,"the actual cart/checkout product query excludes drafts");
                (await FinalRejections(tenant)).Should().Be(1,"the actual transactional posting guard rejects drafts");
                var retailer=new PublicCatalogService(db,new Actor(tenant,false));var owner=new PublicCatalogService(db,new Actor(null,true));
                async Task<CatalogAction> Input(string action,string? reason=null)
                {
                    db.ChangeTracker.Clear();var details=JsonSerializer.SerializeToElement(await owner.Details(tenant,product,default));
                    return new(action,details.GetProperty("ContentVersion").GetString()!,details.GetProperty("ReviewVersion").ValueKind==JsonValueKind.Null?null:details.GetProperty("ReviewVersion").GetString(),reason);
                }
                await retailer.Decide(tenant,product,await Input("SUBMIT"),default);
                (await Public(product)).Should().BeFalse();
                var stale=await Input("APPROVE");
                await db.Products.Where(p=>p.ProductId==product&&p.TenantId==tenant).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.ShortDescription,"New public description"));
                var staleReview=()=>owner.Decide(tenant,product,stale,default);
                await staleReview.Should().ThrowAsync<BusinessRuleException>("a concurrent retailer edit invalidates a review");
                await retailer.Decide(tenant,product,await Input("SUBMIT"),default);
                await owner.Decide(tenant,product,await Input("REJECT","Correct the public description"),default);
                (await Public(product)).Should().BeFalse();
                await retailer.Decide(tenant,product,await Input("SUBMIT"),default);
                await owner.Decide(tenant,product,await Input("APPROVE"),default);
                (await Public(product)).Should().BeTrue();
                (await PricingRows()).Should().Be(1);
                (await FinalRejections(tenant)).Should().Be(0);
                (await FinalRejections(other)).Should().Be(1,"a known ID cannot bypass tenant ownership");
                (await store.GetProductAsync(storeKey,product,default)).Should().NotBeNull();
                // This is a nonpublic stock-policy edit, not a public content edit.
                await db.Products.Where(p=>p.ProductId==product).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.MinimumStock,20));
                (await Public(product)).Should().BeTrue();
                db.Add(new ProductImage{TenantId=tenant,ProductId=product,FileName="fixture.png",ContentType="image/png",ImageData=[1,2,3],ThumbnailData=[1],IsPrimary=true});await db.SaveChangesAsync();
                (await Public(product)).Should().BeFalse("new public media must not leak while awaiting review");
                await retailer.Decide(tenant,product,await Input("SUBMIT"),default);
                await owner.Decide(tenant,product,await Input("APPROVE"),default);
                var get=typeof(StorefrontService).GetMethod("EligibleProducts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var actual=(IQueryable<Product>)get.Invoke(store,[tenant])!;
                (await actual.AnyAsync(p=>p.ProductId==product)).Should().BeTrue();
                (await db.Products.Where(p=>p.ProductId==product).Select(p=>ApplicationDbContext.IsProductPubliclyApproved(other,product)).SingleAsync()).Should().BeFalse();
                await owner.Decide(tenant,product,await Input("SUSPEND","Owner revoked publication"),default);
                (await actual.AnyAsync(p=>p.ProductId==product)).Should().BeFalse();
                (await PricingRows()).Should().Be(0);
                (await FinalRejections(tenant)).Should().Be(1);
                (await store.GetProductAsync(storeKey,product,default)).Should().BeNull();
                (await store.GetProductImageAsync(storeKey,product,null,false,default)).Should().BeNull();
                var count=await db.Set<PublicCatalogAudit>().CountAsync(x=>x.TenantId==tenant&&x.ProductId==product);
                count.Should().BeGreaterThanOrEqualTo(8);
                var body=JsonSerializer.Serialize(await owner.Queue(tenant,"SUSPENDED",null,null,1,default));body.Should().Contain(product.ToString());
                var foreign=()=>new PublicCatalogService(db,new Actor(other,false)).Details(tenant,product,default);
                await foreign.Should().ThrowAsync<System.UnauthorizedAccessException>();
                await retailer.SaveDirectory(tenant,new("GROCERY","Fixture store",true,null),default);
                var settings=JsonSerializer.SerializeToElement(await retailer.DirectorySettings(tenant,default));
                await owner.PublishDirectory(tenant,true,settings.GetProperty("Version").GetString()!,"APPROVE",null,default);
                JsonSerializer.Serialize(await owner.Directory(default)).Should().Contain("catalog-"+tenant.ToString("N"));
                settings=JsonSerializer.SerializeToElement(await retailer.DirectorySettings(tenant,default));
                await retailer.SaveDirectory(tenant,new("GROCERY","Edited listing needs approval",true,settings.GetProperty("Version").GetString()),default);
                JsonSerializer.Serialize(await owner.Directory(default)).Should().NotContain("catalog-"+tenant.ToString("N"));
                settings=JsonSerializer.SerializeToElement(await retailer.DirectorySettings(tenant,default));
                await owner.PublishDirectory(tenant,false,settings.GetProperty("Version").GetString()!,"REJECT","Clarify the directory description",default);
                var rejectedDirectory=JsonSerializer.Serialize(await owner.OwnerDirectory(default));
                rejectedDirectory.Should().Contain("REJECTED").And.Contain("Edited listing needs approval");
                settings=JsonSerializer.SerializeToElement(await retailer.DirectorySettings(tenant,default));
                await owner.PublishDirectory(tenant,true,settings.GetProperty("Version").GetString()!,"APPROVE",null,default);
                JsonSerializer.Serialize(await owner.Directory(default)).Should().Contain("catalog-"+tenant.ToString("N"));
                var enabledFeatures=new FeatureGate(true);
                var marketplace=new PublicMarketplaceService(db,new Actor(null,true),enabledFeatures,new AvailabilityStub(),null!,null!,null!);
                var eligibleRetailers=JsonSerializer.Serialize(await marketplace.OwnerCityRetailers(cityId,default));
                eligibleRetailers.Should().Contain(tenant.ToString(),"an opted-in, owner-approved retailer with City Marketplace access must be listed before it has a city mapping");
                eligibleRetailers.Should().NotContain(other.ToString());
                var disabledRetailerRows=JsonSerializer.Serialize(await new PublicMarketplaceService(db,new Actor(null,true),new FeatureGate(false),new AvailabilityStub(),null!,null!,null!).OwnerCityRetailers(cityId,default));
                disabledRetailerRows.Should().NotContain(tenant.ToString(),"the owner list must still respect effective City Marketplace access");
                var blockedCityAssignment=()=>new PublicMarketplaceService(db,new Actor(null,true),new FeatureGate(false),new AvailabilityStub(),null!,null!,null!).SaveOwnerCityRetailer(cityId,tenant,true,"Central","Information only",default);
                await blockedCityAssignment.Should().ThrowAsync<ArgumentException>("city access requires effective City Marketplace entitlement");
                await marketplace.SaveOwnerCityRetailer(cityId,tenant,true,"Central","Information only",default);
                (await db.MarketplaceRetailers.AnyAsync(x=>x.CityId==cityId&&x.TenantId==tenant&&x.IsActive&&x.Locality=="Central")).Should().BeTrue();
                var partnerRetailer=new PartnerSellerService(db,new Actor(tenant,false),enabledFeatures);
                var partnerOwner=new PartnerSellerService(db,new Actor(null,true),enabledFeatures);
                var blockedPartnerAssociation=()=>new PartnerSellerService(db,new Actor(tenant,false),new FeatureGate(false)).SetProductListing(legacy,partnerId,"fixture",default);
                await blockedPartnerAssociation.Should().ThrowAsync<System.UnauthorizedAccessException>("partner association requires its tenant feature entitlement");
                var unverifiedAssociation=()=>partnerRetailer.SetProductListing(legacy,partnerId,"fixture",default);
                await unverifiedAssociation.Should().ThrowAsync<ArgumentException>("pending sellers cannot be linked to public products");
                await db.PartnerSellers.Where(x=>x.OperatingTenantId==tenant&&x.PartnerSellerId==partnerId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.VerificationStatus,"VERIFIED"));
                await partnerRetailer.SetProductListing(legacy,partnerId,"fixture-retailer",default);
                var listing=JsonSerializer.SerializeToElement(await partnerRetailer.ProductListing(legacy,default));
                await partnerOwner.SetListingDecision(tenant,legacy,partnerId,"APPROVED","Verified catalog fixture",listing.GetProperty("Version").GetString()!,default);
                JsonSerializer.Serialize(await partnerRetailer.ProductListing(legacy,default)).Should().Contain("APPROVED");
                await partnerRetailer.SetProductListing(product,partnerId,"fixture-retailer",default);
                var rejectedListing=JsonSerializer.SerializeToElement(await partnerRetailer.ProductListing(product,default));
                await partnerOwner.SetListingDecision(tenant,product,partnerId,"REJECTED","Review required",rejectedListing.GetProperty("Version").GetString()!,default);
                JsonSerializer.Serialize(await partnerRetailer.ProductListing(product,default)).Should().Contain("REJECTED");
                (await store.GetProductAsync(storeKey,legacy,default)).Should().NotBeNull("verified and approved partner products remain discoverable in the operating storefront");
                var cityProducts=JsonSerializer.Serialize(await marketplace.CityProducts("fixture-city",1,24,null,null,null,null,default));
                cityProducts.Should().Contain(legacy.ToString()).And.Contain("Fixture partner").And.Contain("\"IsPurchasable\":false");
                var categoryFiltered=JsonSerializer.Serialize(await marketplace.CityProducts("fixture-city",1,24,null,"catalog fixture",null,null,default));
                categoryFiltered.Should().Contain(legacy.ToString(),"city category facets filter by normalized category name rather than tenant-local category IDs");
                var brandFiltered=JsonSerializer.Serialize(await marketplace.CityProducts("fixture-city",1,24,null,null,"fixture",null,default));
                brandFiltered.Should().Contain(legacy.ToString(),"city brand facets filter by normalized brand name rather than tenant-local brand IDs");
                await using(var checkoutGuard=new SqlCommand("SELECT TOP(1) 1 FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j JOIN commerce.PartnerProductListings l ON l.OperatingTenantId=@tenant AND l.ProductId=j.ProductId",connection,transaction))
                {checkoutGuard.Parameters.AddWithValue("@tenant",tenant);checkoutGuard.Parameters.AddWithValue("@items",JsonSerializer.Serialize(new[]{new{ProductId=legacy,Quantity=1}}));(await checkoutGuard.ExecuteScalarAsync()).Should().Be(1,"the final checkout guard blocks partner-linked products");}
                await marketplace.SaveOwnerCityRetailer(cityId,tenant,false,null,null,default);
                (await db.MarketplaceRetailers.AnyAsync(x=>x.CityId==cityId&&x.TenantId==tenant)).Should().BeFalse("owner removal removes the city mapping");
                settings=JsonSerializer.SerializeToElement(await retailer.DirectorySettings(tenant,default));
                await owner.PublishDirectory(tenant,false,settings.GetProperty("Version").GetString()!,"UNPUBLISH",null,default);
                JsonSerializer.Serialize(await owner.OwnerDirectory(default)).Should().Contain("UNPUBLISHED");
                JsonSerializer.Serialize(await owner.Directory(default)).Should().NotContain("catalog-"+tenant.ToString("N"));
            }
            catch(Exception exception){fixtureFailure=exception;}
            finally{try{await transaction.RollbackAsync();}catch(Exception exception){cleanupFailure=exception;}}
        }
        try
        {
            await using var clean=new SqlCommand("SELECT (SELECT COUNT(*) FROM core.Tenants WHERE TenantId IN(@tenant,@other,@legacyTenant))+(SELECT COUNT(*) FROM master.Products WHERE ProductId IN(@product,@legacy))+(SELECT COUNT(*) FROM master.ProductCategories WHERE ProductCategoryId=@category)+(SELECT COUNT(*) FROM master.Brands WHERE BrandId=@brand)+(SELECT COUNT(*) FROM master.UnitsOfMeasure WHERE UnitId=@unit)+(SELECT COUNT(*) FROM master.TenantProductCategories WHERE TenantId=@tenant AND ProductCategoryId=@category)+(SELECT COUNT(*) FROM master.TenantBrands WHERE TenantId=@tenant AND BrandId=@brand)+(SELECT COUNT(*) FROM master.TenantUnitsOfMeasure WHERE TenantId=@tenant AND UnitId=@unit)+(SELECT COUNT(*) FROM commerce.PublicRouteRegistry WHERE TenantId IN(@tenant,@other,@legacyTenant) OR CityId IN(@city,@strictCity))+(SELECT COUNT(*) FROM commerce.Cities WHERE CityId IN(@city,@strictCity))+(SELECT COUNT(*) FROM commerce.CityRetailers WHERE CityId IN(@city,@strictCity))+(SELECT COUNT(*) FROM commerce.PartnerSellers WHERE PartnerSellerId=@seller)+(SELECT COUNT(*) FROM commerce.PartnerProductListings WHERE ProductId IN(@product,@legacy))+(SELECT COUNT(*) FROM commerce.CityMarketplaceMedia WHERE MediaId=@media)+(SELECT COUNT(*) FROM commerce.ProductPublications WHERE TenantId=@tenant AND ProductId IN(@product,@legacy))+(SELECT COUNT(*) FROM commerce.PublicCatalogAudit WHERE TenantId=@tenant)+(SELECT COUNT(*) FROM commerce.StoreDirectoryEntries WHERE TenantId=@tenant)+(SELECT COUNT(*) FROM master.ProductImages WHERE TenantId=@tenant AND ProductId=@product)",connection);
            clean.Parameters.AddWithValue("@tenant",tenant);clean.Parameters.AddWithValue("@other",other);clean.Parameters.AddWithValue("@legacyTenant",legacyRouteTenant);clean.Parameters.AddWithValue("@product",product);clean.Parameters.AddWithValue("@legacy",legacy);clean.Parameters.AddWithValue("@city",cityId);clean.Parameters.AddWithValue("@strictCity",strictCityId);clean.Parameters.AddWithValue("@seller",partnerId);clean.Parameters.AddWithValue("@media",mediaId);
            clean.Parameters.AddWithValue("@category",categoryId);clean.Parameters.AddWithValue("@brand",brandId);clean.Parameters.AddWithValue("@unit",unitId);
            Convert.ToInt32(await clean.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().Be(0,"all fixture DDL and data changes must be rolled back");
            await using var after=new SqlCommand(schemaStateSql,connection);
            ((string)(await after.ExecuteScalarAsync())!).Should().Be(schemaBefore,"governance tables and new indexes must return to their pre-test state");
        }
        catch(Exception exception){cleanupFailure=exception;}
        if(fixtureFailure is not null&&cleanupFailure is not null)throw new AggregateException("The SQL fixture failed and post-rollback cleanup verification also failed.",fixtureFailure,cleanupFailure);
        if(cleanupFailure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        if(fixtureFailure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fixtureFailure).Throw();
    }
    private sealed class Actor(Guid? tenant,bool owner):ICurrentUserService
    {public Guid? TenantId=>tenant;public Guid? UserId=>Guid.Parse("00000000-0000-0000-0000-000000000001");public string? Username=>"catalog-fixture";public string? Email=>null;public IReadOnlyCollection<string> Roles=>owner?["ApplicationOwner"]:[];public IReadOnlyCollection<string> Permissions=>[];}

    private sealed class FeatureGate(bool enabled):IFeatureService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId,string featureKey,CancellationToken cancellationToken=default)=>Task.FromResult(enabled);
        public Task<IReadOnlyDictionary<string,bool>> GetEffectiveFeaturesAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId,IReadOnlyCollection<TenantFeatureUpdate> updates,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantDisplayNameAsync(Guid tenantId,string? displayName,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public void InvalidateTenant(Guid tenantId)=>throw new NotSupportedException();
        public void InvalidateAll()=>throw new NotSupportedException();
    }

    private sealed class AvailabilityStub:IStorefrontAvailabilityService
    {
        public Task<Guid?> ResolveTenantIdAsync(string storeKey,CancellationToken token)=>throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid,StorefrontProductAvailability>> GetProductAvailabilityAsync(Guid tenantId,IReadOnlyCollection<Guid> productIds,CancellationToken token)=>Task.FromResult<IReadOnlyDictionary<Guid,StorefrontProductAvailability>>(new Dictionary<Guid,StorefrontProductAvailability>());
        public Task<IReadOnlyCollection<StorefrontAvailabilityAnnouncementDto>> GetAnnouncementsAsync(Guid tenantId,CancellationToken token)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<StorefrontAvailabilityScheduleDto>> GetSchedulesAsync(Guid tenantId,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontAvailabilityScheduleDto> SaveScheduleAsync(Guid tenantId,Guid? scheduleId,SaveStorefrontAvailabilityScheduleInput input,CancellationToken token)=>throw new NotSupportedException();
        public Task DeleteScheduleAsync(Guid tenantId,Guid scheduleId,CancellationToken token)=>throw new NotSupportedException();
        public Task EnsureOrderEligibleAsync(Guid tenantId,IReadOnlyCollection<Guid> productIds,CancellationToken token)=>throw new NotSupportedException();
    }
}
