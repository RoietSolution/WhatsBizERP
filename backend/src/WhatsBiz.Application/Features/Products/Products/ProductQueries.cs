using MediatR;
using WhatsBiz.Application.Features.Products.DTOs;

namespace WhatsBiz.Application.Features.Products.Products;

public sealed record GetProductsQuery(string? Search = null, bool? IsActive = null, string SortBy = "productName", bool Descending = false, int PageNumber = 1, int PageSize = 20) : IRequest<PagedResult<ProductListItemDto>>;
public sealed record GetProductByIdQuery(Guid ProductId) : IRequest<ProductDto>;
public sealed record ExportProductsQuery(string? Search = null, bool? IsActive = null) : IRequest<byte[]>;
public sealed record DownloadProductTemplateQuery : IRequest<byte[]>;
public sealed record GetProductImageQuery(Guid ProductId, bool Thumbnail = false) : IRequest<ProductImageFile?>;
public sealed record GetProductImagesQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductImageDto>>;
public sealed record GetProductImageByIdQuery(Guid ProductId, Guid ImageId, bool Thumbnail = false) : IRequest<ProductImageFile?>;
public sealed record ProductImageFile(string FileName, string ContentType, byte[] Content);
