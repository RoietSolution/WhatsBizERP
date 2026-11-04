using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MediatR;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Suppliers;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Suppliers;
using WhatsBiz.Domain.Products;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/suppliers/{supplierId:guid}/catalog-import")]
public sealed class SupplierCatalogImportController(ApplicationDbContext db, ICurrentUserService user, IProductRepository products, ISender sender) : ControllerBase
{
    private static readonly string[] MissingCategoryErrors = ["Category is missing and automatic category creation is disabled."];
    private static readonly string[] MissingSubcategoryErrors = ["Subcategory is missing and automatic category creation is disabled."];
    private static readonly string[] DuplicateIdentifierErrors = ["This supplier SKU or fallback handle appears more than once in the import file."];
    private static readonly string[] AmbiguousIdentifierErrors = ["Supplier SKU or fallback handle matches multiple saved catalogue records; resolve the supplier identifiers before importing."];
    private static readonly string[] MissingGstErrors = ["GST requires an explicit profile value, mapped GST column or an existing verified product GST value."];
    private static readonly string[] MissingBrandErrors = ["Shopify Vendor is required unless an active tenant default brand is configured."];
    private static readonly string[] MissingUnitErrors = ["Mapped unit is not available to this tenant."];
    [HttpGet("profile"), HasPermission(Permissions.Supplier.View)]
    public async Task<IActionResult> GetProfile(Guid supplierId, CancellationToken token)
    {
        if (!await SupplierExists(supplierId, token)) return NotFound();
        var tenant = TenantId;
        var profile = await db.SupplierCatalogImportProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.SupplierId == supplierId, token);
        return Ok(profile is null ? new SupplierCatalogProfileDto(supplierId, "SHOPIFY", ShopifyCatalogImportParser.DefaultMappings, new Dictionary<string,bool>{{"productName",true},{"cost",true},{"description",true},{"images",true},{"weight",true},{"status",false}}, false, null, null, null) : ToDto(profile));
    }

    [HttpGet("template"), HasPermission(Permissions.Product.Create)]
    public async Task<IActionResult> Template(Guid supplierId, CancellationToken token)
    {
        if (!await SupplierExists(supplierId, token)) return NotFound();
        return File(ShopifyCatalogImportTemplate.Create(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "shopify-supplier-catalog-template.xlsx");
    }

    [HttpPut("profile"), HasPermission(Permissions.Supplier.Edit)]
    public async Task<IActionResult> SaveProfile(Guid supplierId, SupplierCatalogProfileInput input, CancellationToken token)
    {
        if (!await SupplierExists(supplierId, token)) return NotFound();
        if (!string.Equals(input.Format,"SHOPIFY",StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message="Only the Shopify supplier profile is configured here." });
        if (input.DefaultGstPercentage is < 0 or > 100) return BadRequest(new { message="GST must be explicitly configured from 0 to 100 percent." });
        if (input.FieldMappings is null || input.SupplierOwnedFields is null || input.FieldMappings.Count > 30 || input.FieldMappings.Any(x=>x.Key.Length>40||x.Value.Length>150)) return BadRequest(new { message="Field mappings are invalid or too large." });
        var tenant=TenantId;
        var brands=await products.GetBrandsAsync(token);
        var units=await products.GetUnitsAsync(token);
        if(input.DefaultBrandId is Guid brand && !brands.Any(x=>x.BrandId==brand&&x.IsActive&&!x.IsDeleted)) return BadRequest(new { message="Default brand must be active and available to this tenant." });
        if(input.DefaultUnitId is Guid unit && !units.Any(x=>x.UnitId==unit)) return BadRequest(new { message="Default unit is not available to this tenant." });
        var profile=await db.SupplierCatalogImportProfiles.SingleOrDefaultAsync(x=>x.TenantId==tenant&&x.SupplierId==supplierId,token);
        if(profile is null){ profile=new SupplierCatalogImportProfile{TenantId=tenant,SupplierId=supplierId}; db.SupplierCatalogImportProfiles.Add(profile); }
        profile.Format="SHOPIFY"; profile.FieldMappingsJson=JsonSerializer.Serialize(input.FieldMappings); profile.SupplierOwnedFieldsJson=JsonSerializer.Serialize(input.SupplierOwnedFields); profile.AutoCreateCategories=input.AutoCreateCategories;
        profile.DefaultUnitId=input.DefaultUnitId; profile.DefaultBrandId=input.DefaultBrandId; profile.DefaultGstPercentage=input.DefaultGstPercentage;
        profile.ModifiedOn=DateTimeOffset.UtcNow; profile.ModifiedBy=user.Username;
        await db.SaveChangesAsync(token);
        return Ok(ToDto(profile));
    }

    [HttpPost("preview"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10*1024*1024)]
    public async Task<IActionResult> Preview(Guid supplierId, [FromForm] IFormFile file, CancellationToken token)
    {
        if (!await SupplierExists(supplierId, token)) return NotFound();
        if(file.Length is <=0 or >10*1024*1024) return BadRequest(new { message="The import file must be between 1 byte and 10 MB." });
        var profile=await db.SupplierCatalogImportProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.TenantId==TenantId&&x.SupplierId==supplierId,token);
        IReadOnlyDictionary<string,string>? mappings=profile is null?null:JsonSerializer.Deserialize<Dictionary<string,string>>(profile.FieldMappingsJson);
        await using var stream=new MemoryStream(); await file.CopyToAsync(stream,token);
        IReadOnlyCollection<ShopifyCatalogRow> rows;
        try { rows=ShopifyCatalogImportParser.Parse(stream.ToArray(),file.FileName,mappings); }
        catch(InvalidDataException ex){return BadRequest(new { message=ex.Message });}
        var tenant=TenantId;
        var synchronizeExistingStatus=profile is not null
            && (JsonSerializer.Deserialize<Dictionary<string,bool>>(profile.SupplierOwnedFieldsJson)?.GetValueOrDefault("status") ?? false);
        var linkedCatalog=await db.SupplierCatalogProducts.AsNoTracking()
            .Where(x=>x.TenantId==tenant&&x.SupplierId==supplierId&&x.ProductId.HasValue)
            .Select(x=>new{x.SupplierSku,x.SourceHandle,x.SourceRowIdentity,ProductId=x.ProductId!.Value}).ToArrayAsync(token);
        var linkedProductIds=linkedCatalog.Select(x=>x.ProductId).Distinct().ToArray();
        var currentProductStatuses=await db.Products.AsNoTracking()
            .Where(x=>x.TenantId==tenant&&linkedProductIds.Contains(x.ProductId)&&!x.IsDeleted)
            .Select(x=>new{x.ProductId,x.IsActive}).ToDictionaryAsync(x=>x.ProductId,x=>x.IsActive,token);
        var categories=(await products.GetCategoriesAsync(token)).Where(x=>x.IsActive&&!x.IsDeleted).ToArray();
        var brands=await products.GetBrandsAsync(token); var units=await products.GetUnitsAsync(token);
        var defaultBrand=profile?.DefaultBrandId is Guid defaultBrandId?brands.SingleOrDefault(x=>x.BrandId==defaultBrandId):null;
        var preview=rows.Select(row=>{
            var physicalWeight=row.WeightGrams.HasValue&&row.WeightUnit is not null
                ? ShopifyProductWeightProjection.ToProductWeight(row.WeightGrams.Value,row.WeightUnit)
                : ((decimal Value,string Unit)?)null;
            var erpCategoryName=ShopifyCategoryMappingPolicy.FromTags(row.Tags);
            var matches=categories.Where(c=>ShopifyCategoryMappingPolicy.Matches(row.Tags,c.CategoryName)).ToArray();
            var selected=matches.FirstOrDefault(x=>x is not null);
            var subcategoryExists=selected is not null&&(string.IsNullOrWhiteSpace(row.ProductType)||categories.Any(c=>c.ParentCategoryId==selected.ProductCategoryId&&string.Equals(ShopifyCatalogImportParser.Normalize(c.CategoryName),ShopifyCatalogImportParser.Normalize(row.ProductType),StringComparison.OrdinalIgnoreCase)));
            var categoryStatus=erpCategoryName is null?"NEEDS_MAPPING":selected is not null&&subcategoryExists?"EXISTING":profile?.AutoCreateCategories==true?"NEW":"NEEDS_MAPPING";
            var brandMapping=ShopifyBrandMappingPolicy.Resolve(row.BrandName,brands,defaultBrand,profile?.DefaultBrandId.HasValue==true);
            var rowBrand=brandMapping.Brand;
            var rowUnit=string.IsNullOrWhiteSpace(row.UnitName)?null:units.FirstOrDefault(x=>string.Equals(ShopifyCatalogImportParser.Normalize(x.UnitName),ShopifyCatalogImportParser.Normalize(row.UnitName),StringComparison.OrdinalIgnoreCase)||string.Equals(ShopifyCatalogImportParser.Normalize(x.ShortName),ShopifyCatalogImportParser.Normalize(row.UnitName),StringComparison.OrdinalIgnoreCase));
            var resolvedGst=ShopifyCatalogPricingPolicy.ResolveGstPercentage(row,profile?.DefaultGstPercentage);
            var gstSource=ShopifyCatalogPricingPolicy.GstSource(row,profile?.DefaultGstPercentage);
            var missingCategory=categoryStatus=="NEEDS_MAPPING"
                ?erpCategoryName is null?["Shopify Tags (ERP category)"]:[ $"ERP category mapping: {erpCategoryName}" ]
                :Array.Empty<string>();
            var missing=row.MissingFields.Concat(missingCategory).Concat(resolvedGst is null?["GST percentage"]:[]).Concat(rowUnit is null&&profile?.DefaultUnitId is null?["Import unit"]:[]).Concat(brandMapping.Status=="MISSING"?["Brand mapping"]:[]).Distinct().ToArray();
            var errs=row.Errors.Concat(ShopifyCatalogPricingPolicy.Validate(row)).ToList();
            if(brandMapping.Status is "INVALID" or "INACTIVE" or "AMBIGUOUS") errs.Add(brandMapping.Message!);
            if(string.IsNullOrWhiteSpace(row.SupplierSku)&&string.IsNullOrWhiteSpace(row.Handle)) errs.Add("Supplier SKU or source handle is required for controlled matching.");
            var sourceIdentity=!string.IsNullOrWhiteSpace(row.SupplierSku)?"sku:"+row.SupplierSku:!string.IsNullOrWhiteSpace(row.Handle)?"handle:"+row.Handle:"row:"+row.RowNumber;
            var linkedMatches=linkedCatalog.Where(x=>(row.SupplierSku is not null&&string.Equals(x.SupplierSku,row.SupplierSku,StringComparison.OrdinalIgnoreCase))
                ||(row.Handle is not null&&string.Equals(x.SourceHandle,row.Handle,StringComparison.OrdinalIgnoreCase)&&(row.SupplierSku is null||x.SupplierSku is null))
                ||string.Equals(x.SourceRowIdentity,sourceIdentity,StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            bool? currentIsActive=null;
            if(linkedMatches.Length==1&&currentProductStatuses.TryGetValue(linkedMatches[0].ProductId,out var linkedIsActive))currentIsActive=linkedIsActive;
            var isLinkedProduct=currentIsActive.HasValue;
            var proposedIsActive=row.ProposedIsActive.HasValue
                ?ShopifyCatalogStatusPolicy.ResolveForPersistence(row.ProposedIsActive,isLinkedProduct,currentIsActive??false,synchronizeExistingStatus)
                :(bool?)null;
            var statusAction=!row.ProposedIsActive.HasValue?"INVALID STATUS":linkedMatches.Length>1?"AMBIGUOUS LINK"
                :!isLinkedProduct?$"CREATE {(proposedIsActive==true?"ACTIVE":"INACTIVE")}"
                :currentIsActive==row.ProposedIsActive?"NO CHANGE"
                :synchronizeExistingStatus?$"WILL {(proposedIsActive==true?"ACTIVATE":"DEACTIVATE")}"
                :$"STATUS SYNC OFF — WOULD {(row.ProposedIsActive==true?"ACTIVATE":"DEACTIVATE")}";
            return new { row.RowNumber,row.Title,row.SupplierSku,row.Handle,row.Tags,erpCategoryName,row.TagOptions,ProductType=row.ProductType,categoryStatus,categoryMatches=matches.Select(x=>new{x.ProductCategoryId,x.CategoryName,x.ParentCategoryId}),vendor=row.BrandName,brandName=brandMapping.NormalizedVendor??rowBrand?.BrandName,brandStatus=brandMapping.Status,brandMessage=brandMapping.Message,brandMatch=rowBrand?.BrandName,unitMatch=rowUnit?.UnitName,row.ProductCost,row.CombinedShippingPackagingCost,erpCostPrice=ShopifyCatalogPricingPolicy.CalculateErpCostPrice(row),gstPricingType=ShopifyCatalogPricingPolicy.GstPricingType,gstPercentage=resolvedGst,gstSource,workbookGstPercentage=row.GstPercentage,row.SupplierAvailableQuantity,row.SellingPrice,sellingPriceMissing=row.SellingPrice is null,row.CompareAtPrice,row.Mrp,row.ImageUrl,primaryImageUrl=row.ImageUrl,imageCount=row.Images.Count,primaryImageFallback=row.PrimaryImageFallback,images=row.Images,row.AdditionalImageUrls,weightGrams=row.WeightGrams,weightValue=physicalWeight?.Value,weightUnit=row.WeightUnit,weightUnitWasDefaulted=row.WeightUnitWasDefaulted,weightUnitStorageSupported=true,descriptionHtml=ProductDescriptionSanitizer.Sanitize(row.DescriptionHtml,100000),row.DescriptionText,shopifyStatus=row.ShopifyStatus,proposedErpStatus=row.ProposedIsActive.HasValue?(row.ProposedIsActive==true?"ACTIVE":"INACTIVE"):null,currentErpStatus=currentIsActive.HasValue?(currentIsActive==true?"ACTIVE":"INACTIVE"):null,effectiveErpStatus=proposedIsActive.HasValue?(proposedIsActive==true?"ACTIVE":"INACTIVE"):null,statusAction,statusSyncEnabled=synchronizeExistingStatus,missingFields=missing,errors=errs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),draft=true,storefrontVisible=false };
        }).ToArray();
        return Ok(new { supplierId,format="SHOPIFY",rows=preview,totalRows=preview.Length,existingCategoryCount=preview.Count(x=>x.categoryStatus=="EXISTING"),newCategoryCount=preview.Count(x=>x.categoryStatus=="NEW"),needsMappingCount=preview.Count(x=>x.categoryStatus=="NEEDS_MAPPING") });
    }

    [HttpPost("import"), HasPermission(Permissions.Product.Create), RequestSizeLimit(14*1024*1024)]
    public async Task<IActionResult> Import(Guid supplierId,[FromForm] IFormFile file,[FromForm] string selectionsJson,CancellationToken token)
    {
        if(!await SupplierExists(supplierId,token))return NotFound();
        if(file is null||file.Length is <=0 or >10*1024*1024)return BadRequest(new{message="The import file must be between 1 byte and 10 MB."});
        var profile=await db.SupplierCatalogImportProfiles.SingleOrDefaultAsync(x=>x.TenantId==TenantId&&x.SupplierId==supplierId,token);
        if(profile is null)return BadRequest(new{message="Save the supplier import profile before importing."});
        var ownedFields=JsonSerializer.Deserialize<Dictionary<string,bool>>(profile.SupplierOwnedFieldsJson)??new();
        await using var source=new MemoryStream();await file.CopyToAsync(source,token);
        IReadOnlyCollection<ShopifyCatalogRow> rows;
        try{rows=ShopifyCatalogImportParser.Parse(source.ToArray(),file.FileName,JsonSerializer.Deserialize<Dictionary<string,string>>(profile.FieldMappingsJson));}
        catch(InvalidDataException ex){return BadRequest(new{message=ex.Message});}
        var tenant=TenantId;var categories=(await products.GetCategoriesAsync(token)).Where(x=>x.IsActive&&!x.IsDeleted).ToList();
        var units=await products.GetUnitsAsync(token);
        var unit=profile.DefaultUnitId is Guid unitId?units.SingleOrDefault(x=>x.UnitId==unitId&&x.IsActive&&!x.IsDeleted):null;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var errors=new List<object>();var imported=0;var seenIdentifiers=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await AcquireTenantBrandImportLock(tenant,token);
            // Read tenant-visible brands under the import transaction so concurrent imports
            // cannot both decide that the same Vendor is missing.
            var brands=(await products.GetBrandsAsync(token)).Where(x=>!x.IsDeleted).ToList();
            var defaultBrand=profile.DefaultBrandId is Guid brandId?brands.SingleOrDefault(x=>x.BrandId==brandId):null;
            foreach(var row in rows)
            {
                var rowErrors=row.Errors.Concat(ShopifyCatalogPricingPolicy.Validate(row)).ToList();
                if(string.IsNullOrWhiteSpace(row.Title))rowErrors.Add("Product name is required.");
                var brandMapping=ShopifyBrandMappingPolicy.Resolve(row.BrandName,brands,defaultBrand,profile.DefaultBrandId.HasValue);
                if(brandMapping.Status is "INVALID" or "INACTIVE" or "AMBIGUOUS" or "MISSING")rowErrors.Add(brandMapping.Message??MissingBrandErrors[0]);
                var primaryTag=ShopifyCategoryMappingPolicy.FromTags(row.Tags);
                if(primaryTag is null)rowErrors.Add("Shopify Tags is required to determine the ERP category.");
                if(string.IsNullOrWhiteSpace(row.SupplierSku)&&string.IsNullOrWhiteSpace(row.Handle))rowErrors.Add("Supplier SKU or source handle is required.");
                var gst=ShopifyCatalogPricingPolicy.ResolveGstPercentage(row,profile.DefaultGstPercentage);
                var sellingPrice=row.SellingPrice;
                if(sellingPrice is <0)rowErrors.Add("Selling price must be non-negative.");
                if(rowErrors.Count>0){errors.Add(new{row=row.RowNumber,errors=rowErrors});continue;}
                var rowBrand=brandMapping.Brand;
                var rowUnit=string.IsNullOrWhiteSpace(row.UnitName)?unit:units.FirstOrDefault(x=>string.Equals(ShopifyCatalogImportParser.Normalize(x.UnitName),ShopifyCatalogImportParser.Normalize(row.UnitName),StringComparison.OrdinalIgnoreCase)||string.Equals(ShopifyCatalogImportParser.Normalize(x.ShortName),ShopifyCatalogImportParser.Normalize(row.UnitName),StringComparison.OrdinalIgnoreCase));
                var category=await ResolveCategory(primaryTag!,null);
                if(category is null){errors.Add(new{row=row.RowNumber,errors=MissingCategoryErrors});continue;}
                if(!string.IsNullOrWhiteSpace(row.ProductType))
                {
                    var child=await ResolveCategory(ShopifyCatalogImportParser.Normalize(row.ProductType),category.ProductCategoryId);
                    if(child is null){errors.Add(new{row=row.RowNumber,errors=MissingSubcategoryErrors});continue;}
                    category=child;
                }
                var sourceIdentity=!string.IsNullOrWhiteSpace(row.SupplierSku)?"sku:"+row.SupplierSku:!string.IsNullOrWhiteSpace(row.Handle)?"handle:"+row.Handle:"row:"+row.RowNumber;
                var dedupeKey=!string.IsNullOrWhiteSpace(row.SupplierSku)?"sku:"+row.SupplierSku:!string.IsNullOrWhiteSpace(row.Handle)?"handle:"+row.Handle:sourceIdentity;
                if(!seenIdentifiers.Add(dedupeKey)){errors.Add(new{row=row.RowNumber,errors=DuplicateIdentifierErrors});continue;}
                var catalogMatches=await db.SupplierCatalogProducts.Where(x=>x.TenantId==tenant&&x.SupplierId==supplierId&&((row.SupplierSku!=null&&x.SupplierSku==row.SupplierSku)||(row.Handle!=null&&x.SourceHandle==row.Handle&&(row.SupplierSku==null||x.SupplierSku==null))||x.SourceRowIdentity==sourceIdentity)).Take(2).ToListAsync(token);
                if(catalogMatches.Count>1){errors.Add(new{row=row.RowNumber,errors=AmbiguousIdentifierErrors});continue;}
                var catalog=catalogMatches.SingleOrDefault();
                Product? product=catalog?.ProductId is Guid existingId?await db.Products.Include(x=>x.Specifications).SingleOrDefaultAsync(x=>x.TenantId==tenant&&x.ProductId==existingId&&!x.IsDeleted,token):null;
                var isNewProduct=product is null;
                if(isNewProduct&&rowUnit is null){errors.Add(new{row=row.RowNumber,errors=MissingUnitErrors});continue;}
                if(gst is null){errors.Add(new{row=row.RowNumber,errors=MissingGstErrors});continue;}
                if(isNewProduct&&rowBrand is null&&brandMapping.Status=="WILL_CREATE")
                {
                    var created=await sender.Send(new CreateBrandCommand(new BrandInput(null,brandMapping.NormalizedVendor!,null,null,true),TenantPrivate:true),token);
                    rowBrand=await products.GetBrandAsync(created.BrandId,false,token);
                    if(rowBrand is null||!rowBrand.IsActive||rowBrand.IsDeleted)throw new InvalidOperationException("The new tenant brand could not be resolved after creation; the import was rolled back.");
                    brands.Add(rowBrand);
                }
                if(isNewProduct&&rowBrand is null){errors.Add(new{row=row.RowNumber,errors=MissingBrandErrors});continue;}
                if(product is null)
                {
                    var code="SUP-"+Hash(tenant.ToString("N")+"|"+supplierId.ToString("N")+"|"+(row.SupplierSku??sourceIdentity))[..42];
                    product=new Product{TenantId=tenant,ProductCode=code,ProductName=row.Title!,CategoryId=category.ProductCategoryId,BrandId=rowBrand!.BrandId,UnitId=rowUnit!.UnitId,GSTPercentage=gst.Value,GstPricingType=ShopifyCatalogPricingPolicy.GstPricingType,PurchasePrice=row.ProductCost!.Value,CombinedShippingPackagingCost=row.CombinedShippingPackagingCost!.Value,SellingPrice=sellingPrice!.Value,MRP=row.Mrp!.Value,IsActive=ShopifyCatalogStatusPolicy.ResolveForPersistence(row.ProposedIsActive,true,false,false),IsWhatsAppVisible=false,ImageUrl=row.ImageUrl,ShortDescription=row.DescriptionText,LongDescription=ProductDescriptionSanitizer.Sanitize(row.DescriptionHtml,100000),CreatedBy=user.Username};
                    db.Products.Add(product);
                }
                ShopifyCatalogPricingPolicy.ApplyRetailerPricing(product,row,gst.Value);
                if(row.WeightGrams.HasValue&&(isNewProduct||ownedFields.GetValueOrDefault("weight",true)))
                    ApplyPhysicalWeight(product,tenant,row.WeightGrams.Value,row.WeightUnit??"kg",user.Username);
                if(!isNewProduct)
                {
                    if(ownedFields.GetValueOrDefault("productName"))product.ProductName=row.Title!;
                    if(ownedFields.GetValueOrDefault("cost")){product.PurchasePrice=row.ProductCost??product.PurchasePrice;product.CombinedShippingPackagingCost=row.CombinedShippingPackagingCost;}
                    if(ownedFields.GetValueOrDefault("description")){product.ShortDescription=row.DescriptionText;product.LongDescription=ProductDescriptionSanitizer.Sanitize(row.DescriptionHtml,100000);}
                    if(ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(ownedFields.GetValueOrDefault("images"),row.Images))product.ImageUrl=row.ImageUrl;
                    if(ownedFields.GetValueOrDefault("status"))product.IsActive=ShopifyCatalogStatusPolicy.ResolveForPersistence(row.ProposedIsActive,false,product.IsActive,true);
                    // Supplier refreshes never write retailer-controlled selling price, MRP, activation or visibility.
                }
                if(catalog is null){catalog=new SupplierCatalogProduct{TenantId=tenant,SupplierId=supplierId,SourceRowIdentity=sourceIdentity};db.SupplierCatalogProducts.Add(catalog);}
                catalog.SupplierSku=row.SupplierSku;catalog.SourceHandle=row.Handle;catalog.ProductId=product.ProductId;catalog.OriginalTagsJson=JsonSerializer.Serialize(row.Tags is { } originalTags?new[]{originalTags}:Array.Empty<string>());catalog.ProductCost=row.ProductCost;catalog.CombinedShippingPackagingCost=row.CombinedShippingPackagingCost;catalog.SupplierAvailableQuantity=row.SupplierAvailableQuantity;catalog.DescriptionHtml=ProductDescriptionSanitizer.Sanitize(row.DescriptionHtml,100000);catalog.DescriptionText=row.DescriptionText;if(ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(isNewProduct||ownedFields.GetValueOrDefault("images"),row.Images))catalog.ImageUrlsJson=JsonSerializer.Serialize(row.Images.Select(image=>image.Url));catalog.LastImportedOn=DateTimeOffset.UtcNow;
                if(ShopifyCatalogImageProjection.ShouldReplaceSupplierImages(isNewProduct||ownedFields.GetValueOrDefault("images"),row.Images))
                {
                    db.ProductExternalImages.RemoveRange(db.ProductExternalImages.Where(x=>x.TenantId==tenant&&x.ProductId==product.ProductId&&x.SupplierId==supplierId));
                    db.ProductExternalImages.AddRange(ShopifyCatalogImageProjection.ToExternalImages(tenant,product.ProductId,supplierId,row.Images));
                }
                imported++;
            }
            var importId=Guid.NewGuid();db.SupplierCatalogImportHistory.Add(new SupplierCatalogImportHistory{ImportId=importId,TenantId=tenant,SupplierId=supplierId,Format="SHOPIFY",SourceFileHash=Convert.ToHexString(SHA256.HashData(source.ToArray())),ImportedRows=imported,ErrorRows=errors.Count,ImportedBy=user.Username});
            await db.SaveChangesAsync(token);await tx.CommitAsync(token);
            return Ok(new{importId,importedCount=imported,errorCount=errors.Count,errors});

            async Task<ProductCategory?> ResolveCategory(string name,Guid? parentId)
            {
                name=ShopifyCatalogImportParser.Normalize(name);
                var found=categories.FirstOrDefault(x=>x.ParentCategoryId==parentId&&string.Equals(ShopifyCatalogImportParser.Normalize(x.CategoryName),name,StringComparison.OrdinalIgnoreCase));
                found ??= (await products.GetCategoriesAsync(token)).FirstOrDefault(x=>x.IsActive&&!x.IsDeleted&&x.ParentCategoryId==parentId&&string.Equals(ShopifyCatalogImportParser.Normalize(x.CategoryName),name,StringComparison.OrdinalIgnoreCase));
                if(found is not null)return found;
                if(!profile.AutoCreateCategories)return null;
                var id=Guid.NewGuid();var categoryCode="IMP-"+Hash(tenant.ToString("N")+"|"+name.ToUpperInvariant()+"|"+(parentId?.ToString("N")??"root"))[..42];
                var created=new ProductCategory{ProductCategoryId=id,CategoryCode=categoryCode,CategoryName=name,ParentCategoryId=parentId,CreatedBy=user.Username,IsActive=true};
                products.Add(created);categories.Add(created);return created;
            }
        }
        catch(DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            return Conflict(new{message="A supplier SKU, source identity or category was created concurrently. Refresh the supplier catalogue and retry the affected rows."});
        }
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    private Guid TenantId => user.TenantId is Guid id && id!=Guid.Empty ? id : throw new UnauthorizedAccessException("Tenant context is required.");
    private async Task AcquireTenantBrandImportLock(Guid tenant,CancellationToken token)
    {
        var connection=(SqlConnection)db.Database.GetDbConnection();
        var transaction=(SqlTransaction)(db.Database.CurrentTransaction?.GetDbTransaction()??throw new InvalidOperationException("The Shopify import transaction is required for brand resolution."));
        await using var command=new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; SELECT @result;",connection,transaction);
        command.Parameters.Add("@resource",System.Data.SqlDbType.NVarChar,255).Value=$"ShopifyCatalogBrandImport:{tenant:N}";
        var result=Convert.ToInt32(await command.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture);
        if(result<0)throw new InvalidOperationException("The supplier import could not safely acquire the tenant brand creation lock. Retry the import.");
    }
    private static void ApplyPhysicalWeight(Product product,Guid tenantId,decimal weightGrams,string weightUnit,string? username)
    {
        var projected=ShopifyProductWeightProjection.ToProductWeight(weightGrams,weightUnit);
        product.Weight=projected.Value;
        var unitSpecification=product.Specifications.FirstOrDefault(x=>string.Equals(x.Name,ShopifyProductWeightProjection.WeightUnitSpecificationName,StringComparison.OrdinalIgnoreCase));
        if(unitSpecification is null)
            product.Specifications.Add(ShopifyProductWeightProjection.CreateUnitSpecification(tenantId,product.ProductId,projected.Unit,username));
        else
        {
            unitSpecification.Name=ShopifyProductWeightProjection.WeightUnitSpecificationName;
            unitSpecification.Value=projected.Unit;
            unitSpecification.IsDeleted=false;
            unitSpecification.IsActive=true;
            unitSpecification.ModifiedOn=DateTimeOffset.UtcNow;
            unitSpecification.ModifiedBy=username;
        }
    }
    private Task<bool> SupplierExists(Guid id,CancellationToken token)=>db.Suppliers.AnyAsync(x=>x.SupplierId==id&&EF.Property<Guid?>(x,nameof(SupplierCatalogImportProfile.TenantId))==TenantId&&!x.IsDeleted&&x.IsActive,token);
    private static SupplierCatalogProfileDto ToDto(SupplierCatalogImportProfile p)=>new(p.SupplierId,p.Format,JsonSerializer.Deserialize<Dictionary<string,string>>(p.FieldMappingsJson)??new(),JsonSerializer.Deserialize<Dictionary<string,bool>>(p.SupplierOwnedFieldsJson)??new(),p.AutoCreateCategories,p.DefaultUnitId,p.DefaultBrandId,p.DefaultGstPercentage);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if(current is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })return true;
        return false;
    }
}

public sealed record SupplierCatalogProfileInput(string Format,IReadOnlyDictionary<string,string> FieldMappings,IReadOnlyDictionary<string,bool> SupplierOwnedFields,bool AutoCreateCategories,Guid? DefaultUnitId,Guid? DefaultBrandId,decimal? DefaultGstPercentage);
public sealed record SupplierCatalogProfileDto(Guid SupplierId,string Format,IReadOnlyDictionary<string,string> FieldMappings,IReadOnlyDictionary<string,bool> SupplierOwnedFields,bool AutoCreateCategories,Guid? DefaultUnitId,Guid? DefaultBrandId,decimal? DefaultGstPercentage);
public sealed record ShopifyImportSelections(Dictionary<int,string> PrimaryCategories,Dictionary<int,decimal?> SellingPrices)
{ public ShopifyImportSelections():this(new(),new()){} }
