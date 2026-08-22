using WhatsBiz.Domain.Commerce;

namespace WhatsBiz.Application.Common.Interfaces;

public interface ICommerceCollectionRepository
{
    Task<(IReadOnlyCollection<CommerceCollection> Items, int TotalCount)> SearchAsync(string? search, bool? isActive, int page, int size, CancellationToken token);
    Task<CommerceCollection?> GetAsync(Guid id, bool tracking, CancellationToken token);
    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken token);
    Task<bool> SlugExistsAsync(string slug, Guid? excludingId, CancellationToken token);
    Task<IReadOnlyCollection<CommerceCollectionProduct>> ProductsAsync(Guid collectionId, CancellationToken token);
    Task<IReadOnlyCollection<Guid>> ExistingProductIdsAsync(Guid collectionId, IReadOnlyCollection<Guid> productIds, CancellationToken token);
    Task<bool> ProductsBelongToTenantAsync(IReadOnlyCollection<Guid> productIds, CancellationToken token);
    void Add(CommerceCollection collection);
    void AddProducts(IEnumerable<CommerceCollectionProduct> products);
    void RemoveProducts(IEnumerable<CommerceCollectionProduct> products);
    Task SaveAsync(CancellationToken token);
}
