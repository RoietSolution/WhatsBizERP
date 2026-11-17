using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Features;
using WhatsBiz.Domain.Tenants;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed record CatalogAction(string Action, string ContentVersion, string? ReviewVersion, string? Reason);
public sealed record CatalogBatchItem(Guid ProductId, string Status, string? Message = null);
public sealed record CatalogBulkDecisionItem(Guid ProductId, string ContentVersion, string? ReviewVersion);
public sealed record CatalogBulkDecisionInput(IReadOnlyCollection<CatalogBulkDecisionItem> Items, string Action, string? Reason);
public sealed record DirectoryInput(string BusinessType, string ShortDescription, bool OptedIn, string? Version, string? BusinessTypeCode=null);
public sealed record CatalogModification(string? UserName, string Action, string? HttpMethod, bool Succeeded, DateTimeOffset OccurredOn);

public sealed class PublicCatalogService(ApplicationDbContext db, ICurrentUserService user)
{
    private string Actor => user.UserId?.ToString() ?? throw new System.UnauthorizedAccessException();
    private bool Owner => user.Roles.Contains("ApplicationOwner", StringComparer.Ordinal);
    public Guid Retailer => user.TenantId ?? throw new System.UnauthorizedAccessException("A retailer context is required.");
    public void RequireOwner() { if (!Owner) throw new System.UnauthorizedAccessException("Application Owner access required."); }

    public async Task<object> Directory(CancellationToken ct)
    {
        var rows = await (from e in db.Set<StoreDirectoryEntry>().AsNoTracking()
            join t in db.Tenants.AsNoTracking() on e.TenantId equals t.TenantId
            join b in db.Set<BusinessType>() on e.BusinessTypeCode equals b.Code into choices
            from b in choices.DefaultIfEmpty()
            where t.IsActive && e.OptedIn && e.OwnerApproved
            orderby t.DisplayName ?? t.Name
            select new { StoreKey = t.TenantKey, Name = t.DisplayName == null || t.DisplayName.Trim()=="" ? t.Name : t.DisplayName, BusinessType=b==null?e.BusinessType:b.Name,
                e.ShortDescription, LogoMediaId = db.StorefrontConfigurations.Where(c => c.TenantId == t.TenantId).Select(c => c.LogoMediaId).FirstOrDefault() }).ToArrayAsync(ct);
        return rows.Where(r=>ValidStoreKey(r.StoreKey)).Select(r => new { StoreKey=r.StoreKey.ToLowerInvariant(), r.Name, r.BusinessType, r.ShortDescription,
            LogoUrl = r.LogoMediaId.HasValue ? $"/api/store/{Uri.EscapeDataString(r.StoreKey.ToLowerInvariant())}/presentation/logo?v={r.LogoMediaId.Value:N}" : null });
    }

    public async Task<object> DirectorySettings(Guid tenant, CancellationToken ct)
    {
        if (!Owner && tenant != Retailer) throw new System.UnauthorizedAccessException();
        var e = await db.Set<StoreDirectoryEntry>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant, ct);
        var choice=e?.BusinessTypeCode is null?null:await db.Set<BusinessType>().AsNoTracking().SingleOrDefaultAsync(x=>x.Code==e.BusinessTypeCode,ct);
        return new { BusinessType = e?.BusinessType ?? "", ShortDescription = e?.ShortDescription ?? "", OptedIn = e?.OptedIn ?? false,
            BusinessTypeCode=e?.BusinessTypeCode, BusinessTypeRequiresCompletion=choice is null||!choice.IsActive,
            OwnerApproved = e?.OwnerApproved ?? false, Version = e is null ? null : Convert.ToBase64String(e.Version) };
    }

    public async Task SaveDirectory(Guid tenant, DirectoryInput input, CancellationToken ct)
    {
        if (!Owner && tenant != Retailer) throw new System.UnauthorizedAccessException();
        var description = BusinessTypePolicy.Description(input.ShortDescription);
        if (input.OptedIn && description.Length == 0) throw new BusinessRuleException("Enter a short business description before opting in.");
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var choice=await new BusinessTypeCatalogService(db,user).Resolve(input.BusinessTypeCode??input.BusinessType,input.OptedIn,ct);
        if (!await db.Tenants.AnyAsync(t => t.TenantId == tenant, ct)) throw new EntityNotFoundException("Retailer not found.");
        var e = await db.Set<StoreDirectoryEntry>().SingleOrDefaultAsync(x => x.TenantId == tenant, ct);
        CheckVersion(e?.Version, input.Version);
        if (e is null) { e = new() { TenantId = tenant }; db.Add(e); }
        e.BusinessType = choice?.Name??"";e.BusinessTypeCode=choice?.Code; e.ShortDescription = description; e.OptedIn = input.OptedIn;
        e.OwnerApproved = false; // Every edited public directory description is reviewed again.
        Audit(tenant, null, "DIRECTORY_EDIT", null, JsonSerializer.Serialize(input));
        await db.SaveChangesAsync(ct); if(transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<object> OwnerDirectory(CancellationToken ct)
    {
        RequireOwner();
        var rows=await (from t in db.Tenants.AsNoTracking() join e in db.Set<StoreDirectoryEntry>().AsNoTracking() on t.TenantId equals e.TenantId
            join b in db.Set<BusinessType>() on e.BusinessTypeCode equals b.Code into choices from b in choices.DefaultIfEmpty()
            let lastAction=db.Set<PublicCatalogAudit>().Where(a=>a.TenantId==t.TenantId&&a.ProductId==null&&a.Action.StartsWith("DIRECTORY_"))
                .OrderByDescending(a=>a.AuditId).Select(a=>a.Action).FirstOrDefault()
            select new { t.TenantId, Name=string.IsNullOrWhiteSpace(t.DisplayName)?t.Name:t.DisplayName!, t.TenantKey, t.IsActive,
                BusinessType=b==null?e.BusinessType:b.Name,e.BusinessTypeCode, e.ShortDescription, e.OptedIn, e.OwnerApproved, e.Version,LastAction=lastAction,
                LogoMediaId=db.StorefrontConfigurations.Where(c=>c.TenantId==t.TenantId).Select(c=>c.LogoMediaId).FirstOrDefault() }).ToArrayAsync(ct);
        return rows.Select(x=>new{x.TenantId,x.Name,x.TenantKey,x.IsActive,x.BusinessType,x.BusinessTypeCode,x.ShortDescription,x.OptedIn,x.OwnerApproved,x.Version,
            Status=x.OwnerApproved?"PUBLISHED":!x.OptedIn||!x.IsActive||x.LastAction=="DIRECTORY_UNPUBLISH"?"UNPUBLISHED":x.LastAction=="DIRECTORY_REJECT"?"REJECTED":"PENDING",
            LogoUrl=x.LogoMediaId.HasValue&&ValidStoreKey(x.TenantKey)?$"/api/store/{Uri.EscapeDataString(x.TenantKey.ToLowerInvariant())}/presentation/logo?v={x.LogoMediaId.Value:N}":null});
    }

    public async Task PublishDirectory(Guid tenant, bool publish, string version, string? action, string? reason, CancellationToken ct)
    {
        RequireOwner();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var e = await db.Set<StoreDirectoryEntry>().SingleOrDefaultAsync(x => x.TenantId == tenant, ct) ?? throw new EntityNotFoundException("Directory entry not found.");
        CheckVersion(e.Version, version);
        var store=await db.Tenants.SingleOrDefaultAsync(t=>t.TenantId==tenant&&t.IsActive,ct);
        var decision=(action??(publish?"APPROVE":"UNPUBLISH")).Trim().ToUpperInvariant();
        if(decision is not ("APPROVE" or "REJECT" or "UNPUBLISH"))throw new BusinessRuleException("Choose approve, reject or unpublish.");
        if(publish!= (decision=="APPROVE"))throw new BusinessRuleException("The directory decision does not match the requested publication state.");
        var cleanReason=Plain(reason??"",1000);
        if(decision=="REJECT"&&cleanReason.Length==0)throw new BusinessRuleException("Enter a reason before rejecting the directory request.");
        if (publish && (!e.OptedIn || store is null || !ValidStoreKey(store.TenantKey))) throw new BusinessRuleException("Only an active opted-in retailer with a valid storefront key may be published.");
        e.OwnerApproved = publish;
        Audit(tenant, null, decision=="APPROVE"?"DIRECTORY_PUBLISH":decision=="REJECT"?"DIRECTORY_REJECT":"DIRECTORY_UNPUBLISH", cleanReason.Length==0?null:cleanReason, null);
        await db.SaveChangesAsync(ct); if(transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<object> Queue(Guid? tenant, string? status, DateTimeOffset? since, DateTimeOffset? until, string? category, int page, CancellationToken ct)
    {
        RequireOwner();
        // Filtering effective state occurs in SQL, including content edits made by any import/image path.
        var query = from p in db.Products.AsNoTracking()
            join t in db.Tenants on p.TenantId equals t.TenantId
            join a in db.Set<ProductPublication>() on new { p.TenantId, p.ProductId } equals new { a.TenantId, a.ProductId } into approvals
            from a in approvals.DefaultIfEmpty()
            where !p.IsDeleted && (!tenant.HasValue || p.TenantId == tenant.Value)
            select new { p.ProductId, p.TenantId, StoreName = t.DisplayName ?? t.Name, p.ProductName, p.ProductCode,
                CategoryName = p.Category.CategoryName, p.MRP, p.SellingPrice, p.GSTPercentage, p.GstPricingType,
                PrimaryImageId=db.ProductImages.Where(i=>i.TenantId==p.TenantId&&i.ProductId==p.ProductId&&i.IsActive&&!i.IsDeleted)
                    .OrderByDescending(i=>i.IsPrimary).ThenBy(i=>i.ProductImageId).Select(i=>(Guid?)i.ProductImageId).FirstOrDefault(),
                SubmittedAt = a == null ? null : a.SubmittedAt,
                Status = a == null ? "DRAFT" : (a.Status == "APPROVED" || a.Status == "LEGACY" || a.Status == "PENDING_APPROVAL")
                    && a.ContentHash != ApplicationDbContext.ProductPublicContentHash(p.TenantId,p.ProductId) ? "CHANGES_PENDING" : a.Status,
                ContentVersion = ApplicationDbContext.ProductPublicContentHash(p.TenantId,p.ProductId), ReviewVersion = a == null ? null : a.Version };
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.CategoryName == category);
        if (since.HasValue) query = query.Where(x => x.SubmittedAt >= since);
        if (until.HasValue) query = query.Where(x => x.SubmittedAt <= until);
        var pendingTotal=await db.Set<ProductPublication>().AsNoTracking().CountAsync(x=>x.Status=="PENDING_APPROVAL"&&
            x.ContentHash==ApplicationDbContext.ProductPublicContentHash(x.TenantId,x.ProductId),ct);
        var items = await query.OrderBy(x => x.SubmittedAt).ThenBy(x => x.ProductId).Skip((Math.Max(page,1)-1)*30).Take(30).ToArrayAsync(ct);
        return new { Total = await query.CountAsync(ct), PendingTotal=pendingTotal,
            Items = items.Select(x => new { x.ProductId, x.TenantId, x.StoreName, x.ProductName, x.ProductCode, x.CategoryName, x.MRP, x.SellingPrice, x.GSTPercentage, x.GstPricingType, x.PrimaryImageId, x.SubmittedAt, x.Status,
                ContentVersion = Convert.ToBase64String(x.ContentVersion), ReviewVersion = x.ReviewVersion is null ? null : Convert.ToBase64String(x.ReviewVersion) }).ToArray() };
    }

    public Task<object> Queue(Guid? tenant, string? status, DateTimeOffset? since, DateTimeOffset? until, int page, CancellationToken ct)
        => Queue(tenant, status, since, until, null, page, ct);

    public async Task<IReadOnlyCollection<CatalogBatchItem>> SubmitMany(IReadOnlyCollection<Guid> productIds, bool commit, CancellationToken ct)
    {
        if (!PublicCatalogBatchPolicy.HasValidIds(productIds)) throw new BusinessRuleException("Select 1–100 unique products per submission.");
        var tenant = Retailer;
        var results = new List<CatalogBatchItem>(productIds.Count);
        foreach (var productId in productIds)
        {
            try
            {
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductId == productId && !x.IsDeleted, ct);
                if (product is null) { results.Add(new(productId, "NOT_FOUND", "Product is unavailable in this retailer account.")); continue; }
                var hash = await Hash(tenant, productId, ct);
                var publication = await db.Set<ProductPublication>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductId == productId, ct);
                var status = PublicCatalogPolicy.EffectiveStatus(publication?.Status, publication?.ContentHash, hash);
                var eligibility = PublicCatalogBatchPolicy.SubmissionStatus(product.IsActive, status);
                if (eligibility != "ELIGIBLE")
                {
                    var message = eligibility == "INACTIVE" ? "Inactive products cannot be submitted." : eligibility == "ALREADY_PENDING"
                        ? "This product is already awaiting review." : $"Current approval status is {status.Replace('_', ' ')}.";
                    results.Add(new(productId, eligibility, message)); continue;
                }
                if (!commit) { results.Add(new(productId, "ELIGIBLE")); continue; }
                await Decide(tenant, productId, new("SUBMIT", Convert.ToBase64String(hash), publication?.Version is null ? null : Convert.ToBase64String(publication.Version), null), ct);
                results.Add(new(productId, "SUBMITTED"));
            }
            catch (Exception ex) when (ex is EntityNotFoundException or BusinessRuleException or DbUpdateConcurrencyException or DbUpdateException)
            {
                var conflict = ex is not DbUpdateException || ex is DbUpdateConcurrencyException;
                results.Add(new(productId, conflict ? "CONFLICT" : "FAILED", ex is DbUpdateException ? "Approval could not be saved; retry after refreshing." : ex.Message));
            }
            finally { db.ChangeTracker.Clear(); }
        }
        return results;
    }

    public async Task<IReadOnlyCollection<CatalogBatchItem>> DecideMany(Guid tenant, CatalogBulkDecisionInput input, CancellationToken ct)
    {
        RequireOwner();
        if (input.Items is null || input.Items.Count is < 1 or > PublicCatalogBatchPolicy.MaximumItems
            || input.Items.Any(x => x.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(x.ContentVersion))
            || input.Items.Select(x => x.ProductId).Distinct().Count() != input.Items.Count)
            throw new BusinessRuleException("Select 1–100 unique products with current review revisions.");
        if (input.Action is not ("APPROVE" or "REJECT")) throw new BusinessRuleException("Bulk review supports Approve or Reject.");
        if (input.Action == "REJECT" && string.IsNullOrWhiteSpace(input.Reason)) throw new BusinessRuleException("Enter a rejection reason.");
        var results = new List<CatalogBatchItem>(input.Items.Count);
        foreach (var item in input.Items)
        {
            try
            {
                await Decide(tenant, item.ProductId, new(input.Action, item.ContentVersion, item.ReviewVersion, input.Reason), ct);
                results.Add(new(item.ProductId, input.Action == "APPROVE" ? "APPROVED" : "REJECTED"));
            }
            catch (Exception ex) when (ex is EntityNotFoundException or BusinessRuleException or DbUpdateConcurrencyException or DbUpdateException or FormatException)
            {
                var conflict = ex is DbUpdateConcurrencyException || ex is BusinessRuleException &&
                    (ex.Message.Contains("changed", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("current approval status", StringComparison.OrdinalIgnoreCase));
                var message = ex is DbUpdateException ? "Review could not be saved; reload this product before retrying."
                    : ex is FormatException ? "The review revision is invalid; reload the approval queue."
                    : ex.Message;
                results.Add(new(item.ProductId, conflict ? "CONFLICT" : "FAILED", message));
            }
            finally { db.ChangeTracker.Clear(); }
        }
        return results;
    }

    public async Task<object> Details(Guid tenant, Guid product, CancellationToken ct)
    {
        if (!Owner && tenant != Retailer) throw new System.UnauthorizedAccessException();
        var p = await db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Unit).Include(x => x.Specifications)
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductId == product && !x.IsDeleted, ct) ?? throw new EntityNotFoundException("Product not found.");
        var hash = await Hash(tenant,product,ct);
        var a = await db.Set<ProductPublication>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductId == product,ct);
        var history = await db.Set<PublicCatalogAudit>().AsNoTracking().Where(x => x.TenantId == tenant && x.ProductId == product).OrderByDescending(x => x.AuditId).Take(100)
            .Select(x => new { x.Action, x.Actor, x.Reason, x.OccurredAt }).ToArrayAsync(ct);
        var images = await db.ProductImages.AsNoTracking().Where(x => x.TenantId == tenant && x.ProductId == product && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary).Select(x => new { x.ProductImageId,x.FileName,x.IsPrimary }).ToArrayAsync(ct);
        var stock = await db.InventoryBalances.AsNoTracking().Where(x => x.ProductId == product && EF.Property<Guid?>(x,"TenantId") == tenant)
            .Select(x => new { x.WarehouseId, WarehouseName=x.Warehouse.WarehouseName, x.QuantityOnHand,x.QuantityReserved }).ToArrayAsync(ct);
        var scheduled = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM commerce.StorefrontAvailabilityScheduleProducts WHERE TenantId={tenant} AND ProductId={product}").SingleAsync(ct);
        // Product ownership was checked above. Product GUIDs are globally unique;
        // select only existing audited modification metadata, never request bodies.
        var path=$"/api/products/{product:D}";var assets=path+"/%";
        var modifications=await db.Database.SqlQuery<CatalogModification>($"SELECT TOP(100) UserName,Action,HttpMethod,Succeeded,OccurredOn FROM admin.AuditLogs WHERE (RequestPath={path} OR RequestPath LIKE {assets}) AND HttpMethod IN(N'POST',N'PUT',N'PATCH',N'DELETE') ORDER BY OccurredOn DESC").ToArrayAsync(ct);
        return new { p.ProductId,p.TenantId,p.ProductName,p.ProductCode,CategoryName=p.Category.CategoryName,UnitName=p.Unit.UnitName,
            p.SellingPrice,p.MRP,p.GSTPercentage,p.GstPricingType,p.ShortDescription,p.LongDescription,p.IsActive,p.IsWhatsAppVisible,
            Specifications=p.Specifications.Where(x=>x.IsActive&&!x.IsDeleted).Select(x=>new{x.Name,x.Value}),p.CreatedOn,p.CreatedBy,p.ModifiedOn,p.ModifiedBy,
            Status=PublicCatalogPolicy.EffectiveStatus(a?.Status,a?.ContentHash,hash), ContentVersion=Convert.ToBase64String(hash),
            ReviewVersion=a is null?null:Convert.ToBase64String(a.Version),a?.SubmittedAt,a?.DecidedAt,a?.Reviewer,a?.Reason,History=history,Images=images,Stock=stock,
            DirectScheduleAssignments=scheduled, ScheduleNote="Category-inherited schedules and current eligibility are evaluated separately by V67.", Modifications=modifications };
    }

    public async Task Decide(Guid tenant, Guid product, CatalogAction input, CancellationToken ct)
    {
        if (!Owner && tenant != Retailer) throw new System.UnauthorizedAccessException();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct) : null;
        // Locks prevent simultaneous product edits and approval of a different revision.
        var p = await db.Products.FromSqlInterpolated($"SELECT * FROM master.Products WITH(UPDLOCK,HOLDLOCK) WHERE TenantId={tenant} AND ProductId={product}").SingleOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Product not found.");
        if (p.IsDeleted) throw new BusinessRuleException("Deleted products cannot be reviewed.");
        var hash = await Hash(tenant,product,ct);
        if (Convert.ToBase64String(hash) != input.ContentVersion) throw new BusinessRuleException("Product content changed. Reload before reviewing or submitting.");
        var a = await db.Set<ProductPublication>().SingleOrDefaultAsync(x=>x.TenantId==tenant&&x.ProductId==product,ct);
        CheckVersion(a?.Version,input.ReviewVersion);
        var status=PublicCatalogPolicy.EffectiveStatus(a?.Status,a?.ContentHash,hash);
        var reason=Plain(input.Reason ?? "",1000);
        var next=PublicCatalogPolicy.Transition(status,input.Action,Owner,reason);
        if (a is null) { a=new(){TenantId=tenant,ProductId=product};db.Add(a); }
        a.Status=next;a.ContentHash=hash;a.Reason=reason.Length==0?null:reason;
        if(input.Action=="SUBMIT") {a.SubmittedAt=DateTimeOffset.UtcNow;a.DecidedAt=null;a.Reviewer=null;}
        else {a.DecidedAt=DateTimeOffset.UtcNow;a.Reviewer=Actor;}
        // Snapshot is private audit data; never serialized to public catalog endpoints.
        var snapshot = await Details(tenant,product,ct);
        Audit(tenant,product,input.Action,reason,JsonSerializer.Serialize(snapshot));
        await db.SaveChangesAsync(ct);if(transaction is not null)await transaction.CommitAsync(ct);
    }

    public async Task<object> LegacyPlan(Guid tenant,CancellationToken ct)
    {
        RequireOwner();
        return new { TenantId=tenant, LegacyCount=await db.Set<ProductPublication>().CountAsync(x=>x.TenantId==tenant&&x.Status=="LEGACY",ct),
            Message="Grandfathered, not approved. Submit individual products for review to adopt governance without a bulk catalog shutdown. No automatic approval or bulk unpublication." };
    }

    private async Task<byte[]> Hash(Guid tenant,Guid product,CancellationToken ct) => await db.Products.Where(x=>x.TenantId==tenant&&x.ProductId==product)
        .Select(x=>ApplicationDbContext.ProductPublicContentHash(tenant,product)).SingleAsync(ct);
    private static void CheckVersion(byte[]? actual,string? expected)
    { if ((actual is null ? null : Convert.ToBase64String(actual)) != expected) throw new BusinessRuleException("Configuration changed. Reload and try again."); }
    private static string Plain(string text,int max)
    { var value=text.Trim();if(value.Length>max||value.Contains('<')||value.Contains('>')||value.Any(c=>char.IsControl(c)&&c!='\n'))throw new BusinessRuleException($"Enter plain text up to {max} characters.");return value; }
    private static bool ValidStoreKey(string value)=>Regex.IsMatch(value,"^[A-Za-z0-9_-]{1,100}$",RegexOptions.CultureInvariant);
    private void Audit(Guid tenant,Guid? product,string action,string? reason,string? snapshot) => db.Add(new PublicCatalogAudit
        {TenantId=tenant,ProductId=product,Action=action,Actor=Actor,Reason=reason,ContentSnapshot=snapshot,OccurredAt=DateTimeOffset.UtcNow});
}
