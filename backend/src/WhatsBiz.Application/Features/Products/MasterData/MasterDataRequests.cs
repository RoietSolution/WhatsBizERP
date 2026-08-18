using MediatR;
using WhatsBiz.Application.Features.Products.DTOs;

namespace WhatsBiz.Application.Features.Products.MasterData;

public sealed record GetProductCategoriesQuery(string? Search = null, bool? IsActive = null) : IRequest<IReadOnlyCollection<ProductCategoryDto>>;
public sealed record CreateProductCategoryCommand(ProductCategoryInput Input) : IRequest<ProductCategoryDto>;
public sealed record UpdateProductCategoryCommand(Guid Id, ProductCategoryInput Input) : IRequest<ProductCategoryDto>;
public sealed record DeleteProductCategoryCommand(Guid Id) : IRequest;
public sealed record ExportProductCategoriesQuery : IRequest<byte[]>;
public sealed record DownloadProductCategoryTemplateQuery : IRequest<byte[]>;
public sealed record ImportProductCategoriesCommand(byte[] Content) : IRequest<ImportProductMasterResult>;
public sealed record GetBrandsQuery(string? Search = null, bool? IsActive = null) : IRequest<IReadOnlyCollection<BrandDto>>;
public sealed record CreateBrandCommand(BrandInput Input) : IRequest<BrandDto>;
public sealed record UpdateBrandCommand(Guid Id, BrandInput Input) : IRequest<BrandDto>;
public sealed record DeleteBrandCommand(Guid Id) : IRequest;
public sealed record ExportBrandsQuery : IRequest<byte[]>;
public sealed record DownloadBrandTemplateQuery : IRequest<byte[]>;
public sealed record ImportBrandsCommand(byte[] Content) : IRequest<ImportProductMasterResult>;
public sealed record GetUnitsOfMeasureQuery(string? Search = null, bool? IsActive = null) : IRequest<IReadOnlyCollection<UnitOfMeasureDto>>;
public sealed record CreateUnitOfMeasureCommand(UnitOfMeasureInput Input) : IRequest<UnitOfMeasureDto>;
public sealed record UpdateUnitOfMeasureCommand(Guid Id, UnitOfMeasureInput Input) : IRequest<UnitOfMeasureDto>;
public sealed record DeleteUnitOfMeasureCommand(Guid Id) : IRequest;
public sealed record ExportUnitsOfMeasureQuery : IRequest<byte[]>;
public sealed record DownloadUnitOfMeasureTemplateQuery : IRequest<byte[]>;
public sealed record ImportUnitsOfMeasureCommand(byte[] Content) : IRequest<ImportProductMasterResult>;
