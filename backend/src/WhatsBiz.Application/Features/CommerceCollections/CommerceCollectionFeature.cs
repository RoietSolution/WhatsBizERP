#pragma warning disable CA1711, CA1725
using FluentValidation;
using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Commerce;

namespace WhatsBiz.Application.Features.CommerceCollections;

public sealed record CollectionListItemDto(Guid CollectionId, string Name, string Slug, string? Description, bool IsActive, int ProductCount, int DisplayOrder, DateTimeOffset? StartDate, DateTimeOffset? EndDate);
public sealed record CollectionProductDto(Guid ProductId, string ProductCode, string ProductName, string CategoryName, decimal SellingPrice, string? ImageUrl, bool IsActive, int DisplayOrder);
public sealed record CollectionDetailDto(Guid CollectionId, string Name, string Slug, string? Description, bool IsActive, int DisplayOrder, DateTimeOffset? StartDate, DateTimeOffset? EndDate, IReadOnlyCollection<CollectionProductDto> Products);
public sealed record CollectionInput(string Name, string? Description, bool IsActive, int DisplayOrder, DateTimeOffset? StartDate, DateTimeOffset? EndDate);
public sealed record AddCollectionProductsInput(IReadOnlyCollection<Guid> ProductIds);
public sealed record PagedCollections(IReadOnlyCollection<CollectionListItemDto> Items, int TotalCount, int PageNumber, int PageSize);

public sealed record GetCollections(string? Search, bool? IsActive, int PageNumber = 1, int PageSize = 20) : IRequest<PagedCollections>;
public sealed record GetCollection(Guid CollectionId) : IRequest<CollectionDetailDto>;
public sealed record CreateCollection(CollectionInput Input) : IRequest<CollectionDetailDto>;
public sealed record UpdateCollection(Guid CollectionId, CollectionInput Input) : IRequest<CollectionDetailDto>;
public sealed record DeleteCollection(Guid CollectionId) : IRequest;
public sealed record GetCollectionProducts(Guid CollectionId) : IRequest<IReadOnlyCollection<CollectionProductDto>>;
public sealed record AddCollectionProducts(Guid CollectionId, AddCollectionProductsInput Input) : IRequest<IReadOnlyCollection<CollectionProductDto>>;
public sealed record RemoveCollectionProduct(Guid CollectionId, Guid ProductId) : IRequest;

public sealed class CollectionInputValidator : AbstractValidator<CollectionInput>
{
    public CollectionInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.EndDate is null || x.StartDate is null || x.EndDate >= x.StartDate).WithMessage("End date must be on or after start date.");
    }
}
public sealed class CreateCollectionValidator : AbstractValidator<CreateCollection> { public CreateCollectionValidator() => RuleFor(x => x.Input).SetValidator(new CollectionInputValidator()); }
public sealed class UpdateCollectionValidator : AbstractValidator<UpdateCollection> { public UpdateCollectionValidator() => RuleFor(x => x.Input).SetValidator(new CollectionInputValidator()); }
public sealed class AddCollectionProductsValidator : AbstractValidator<AddCollectionProducts> { public AddCollectionProductsValidator() => RuleFor(x => x.Input.ProductIds).NotNull().Must(x => x.Count <= 500).WithMessage("A maximum of 500 products can be added at once."); }

public sealed class CommerceCollectionHandlers(ICommerceCollectionRepository repository, ICurrentUserService currentUser) :
    IRequestHandler<GetCollections, PagedCollections>, IRequestHandler<GetCollection, CollectionDetailDto>,
    IRequestHandler<CreateCollection, CollectionDetailDto>, IRequestHandler<UpdateCollection, CollectionDetailDto>,
    IRequestHandler<DeleteCollection>, IRequestHandler<GetCollectionProducts, IReadOnlyCollection<CollectionProductDto>>,
    IRequestHandler<AddCollectionProducts, IReadOnlyCollection<CollectionProductDto>>, IRequestHandler<RemoveCollectionProduct>
{
    public async Task<PagedCollections> Handle(GetCollections q, CancellationToken t)
    {
        var (items, count) = await repository.SearchAsync(q.Search, q.IsActive, Math.Max(1, q.PageNumber), Math.Clamp(q.PageSize, 1, 200), t);
        return new(items.Select(List).ToArray(), count, q.PageNumber, Math.Clamp(q.PageSize, 1, 200));
    }
    public async Task<CollectionDetailDto> Handle(GetCollection q, CancellationToken t) => Detail(await Require(q.CollectionId, false, t), await repository.ProductsAsync(q.CollectionId, t));
    public async Task<CollectionDetailDto> Handle(CreateCollection q, CancellationToken t)
    {
        var input = q.Input; var tenant = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        var name = input.Name.Trim(); var slug = Slug(name);
        if (await repository.NameExistsAsync(name, null, t) || await repository.SlugExistsAsync(slug, null, t)) throw new BusinessRuleException("A collection with this name already exists.");
        var collection = new CommerceCollection { TenantId = tenant, Name = name, Slug = slug, Description = Clean(input.Description), IsActive = input.IsActive, DisplayOrder = input.DisplayOrder, StartDate = input.StartDate, EndDate = input.EndDate, CreatedBy = currentUser.Username };
        repository.Add(collection); await repository.SaveAsync(t); return Detail(collection, []);
    }
    public async Task<CollectionDetailDto> Handle(UpdateCollection q, CancellationToken t)
    {
        var collection = await Require(q.CollectionId, true, t); var input = q.Input; var name = input.Name.Trim(); var slug = Slug(name);
        if (await repository.NameExistsAsync(name, q.CollectionId, t) || await repository.SlugExistsAsync(slug, q.CollectionId, t)) throw new BusinessRuleException("A collection with this name already exists.");
        collection.Name = name; collection.Slug = slug; collection.Description = Clean(input.Description); collection.IsActive = input.IsActive; collection.DisplayOrder = input.DisplayOrder; collection.StartDate = input.StartDate; collection.EndDate = input.EndDate; collection.ModifiedOn = DateTimeOffset.UtcNow; collection.ModifiedBy = currentUser.Username;
        await repository.SaveAsync(t); return Detail(await Require(q.CollectionId, false, t), await repository.ProductsAsync(q.CollectionId, t));
    }
    public async Task Handle(DeleteCollection q, CancellationToken t)
    {
        var collection = await Require(q.CollectionId, true, t); collection.IsDeleted = true; collection.IsActive = false; collection.ModifiedOn = DateTimeOffset.UtcNow; collection.ModifiedBy = currentUser.Username;
        var products = await repository.ProductsAsync(q.CollectionId, t); repository.RemoveProducts(products); await repository.SaveAsync(t);
    }
    public async Task<IReadOnlyCollection<CollectionProductDto>> Handle(GetCollectionProducts q, CancellationToken t) { _ = await Require(q.CollectionId, false, t); return (await repository.ProductsAsync(q.CollectionId, t)).Select(Product).ToArray(); }
    public async Task<IReadOnlyCollection<CollectionProductDto>> Handle(AddCollectionProducts q, CancellationToken t)
    {
        _ = await Require(q.CollectionId, false, t); var ids = q.Input.ProductIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (!await repository.ProductsBelongToTenantAsync(ids, t)) throw new BusinessRuleException("One or more selected products are unavailable.");
        var existing = (await repository.ExistingProductIdsAsync(q.CollectionId, ids, t)).ToHashSet(); var current = await repository.ProductsAsync(q.CollectionId, t); var nextOrder = current.Select(x => x.DisplayOrder).DefaultIfEmpty(-1).Max() + 1;
        repository.AddProducts(ids.Where(x => !existing.Contains(x)).Select((id, index) => new CommerceCollectionProduct { TenantId = currentUser.TenantId!.Value, CollectionId = q.CollectionId, ProductId = id, DisplayOrder = nextOrder + index, CreatedBy = currentUser.Username }));
        await repository.SaveAsync(t); return (await repository.ProductsAsync(q.CollectionId, t)).Select(Product).ToArray();
    }
    public async Task Handle(RemoveCollectionProduct q, CancellationToken t) { _ = await Require(q.CollectionId, false, t); var rows = await repository.ProductsAsync(q.CollectionId, t); repository.RemoveProducts(rows.Where(x => x.ProductId == q.ProductId)); await repository.SaveAsync(t); }
    private async Task<CommerceCollection> Require(Guid id, bool tracking, CancellationToken t) => await repository.GetAsync(id, tracking, t) ?? throw new EntityNotFoundException("Collection was not found.");
    private static CollectionListItemDto List(CommerceCollection x) => new(x.CollectionId, x.Name, x.Slug, x.Description, x.IsActive, x.Products.Count, x.DisplayOrder, x.StartDate, x.EndDate);
    private static CollectionDetailDto Detail(CommerceCollection x, IReadOnlyCollection<CommerceCollectionProduct> rows) => new(x.CollectionId, x.Name, x.Slug, x.Description, x.IsActive, x.DisplayOrder, x.StartDate, x.EndDate, rows.Select(Product).ToArray());
    private static CollectionProductDto Product(CommerceCollectionProduct x) => new(x.ProductId, x.Product.ProductCode, x.Product.ProductName, x.Product.Category.CategoryName, x.Product.SellingPrice, x.Product.ImageUrl, x.Product.IsActive, x.DisplayOrder);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Replace("--", "-");
}
