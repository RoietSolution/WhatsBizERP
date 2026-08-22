using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Commerce;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class CommerceCollectionRepository(ApplicationDbContext db, ICurrentUserService currentUser) : ICommerceCollectionRepository
{
    private IQueryable<CommerceCollection> TenantCollections => currentUser.TenantId is Guid tenant ? db.CommerceCollections.Where(x => x.TenantId == tenant) : db.CommerceCollections.Where(_ => false);
    private IQueryable<CommerceCollectionProduct> TenantMemberships => currentUser.TenantId is Guid tenant ? db.CommerceCollectionProducts.Where(x => x.TenantId == tenant) : db.CommerceCollectionProducts.Where(_ => false);
    public async Task<(IReadOnlyCollection<CommerceCollection> Items, int TotalCount)> SearchAsync(string? search, bool? isActive, int page, int size, CancellationToken token)
    {
        var tenant = currentUser.TenantId ?? Guid.Empty;
        IQueryable<CommerceCollection> query = TenantCollections.Where(x => !x.IsDeleted).Include(x => x.Products.Where(product => product.TenantId == tenant));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search.Trim()));
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);
        var count = await query.CountAsync(token);
        return (await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Skip((page - 1) * size).Take(size).ToArrayAsync(token), count);
    }
    public Task<CommerceCollection?> GetAsync(Guid id, bool tracking, CancellationToken token)
    {
        var query = TenantCollections.Where(x => !x.IsDeleted && x.CollectionId == id);
        if (!tracking) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(token);
    }
    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken token) => TenantCollections.AnyAsync(x => !x.IsDeleted && x.Name == name.Trim() && (!excludingId.HasValue || x.CollectionId != excludingId), token);
    public Task<bool> SlugExistsAsync(string slug, Guid? excludingId, CancellationToken token) => TenantCollections.AnyAsync(x => !x.IsDeleted && x.Slug == slug.Trim() && (!excludingId.HasValue || x.CollectionId != excludingId), token);
    public async Task<IReadOnlyCollection<CommerceCollectionProduct>> ProductsAsync(Guid collectionId, CancellationToken token) => await TenantMemberships.Where(x => x.CollectionId == collectionId).Include(x => x.Product).ThenInclude(x => x.Category).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Product.ProductName).AsNoTracking().ToArrayAsync(token);
    public async Task<IReadOnlyCollection<Guid>> ExistingProductIdsAsync(Guid collectionId, IReadOnlyCollection<Guid> productIds, CancellationToken token) => await TenantMemberships.Where(x => x.CollectionId == collectionId && productIds.Contains(x.ProductId)).Select(x => x.ProductId).ToArrayAsync(token);
    public async Task<bool> ProductsBelongToTenantAsync(IReadOnlyCollection<Guid> productIds, CancellationToken token) { var tenant = currentUser.TenantId ?? Guid.Empty; return productIds.Count == await db.Products.CountAsync(x => x.TenantId == tenant && productIds.Contains(x.ProductId) && x.IsActive && !x.IsDeleted, token); }
    public void Add(CommerceCollection collection) => db.CommerceCollections.Add(collection);
    public void AddProducts(IEnumerable<CommerceCollectionProduct> products) => db.CommerceCollectionProducts.AddRange(products);
    public void RemoveProducts(IEnumerable<CommerceCollectionProduct> products) => db.CommerceCollectionProducts.RemoveRange(products);
    public Task SaveAsync(CancellationToken token) => db.SaveChangesAsync(token);
}
