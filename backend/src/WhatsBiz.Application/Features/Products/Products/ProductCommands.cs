using MediatR;
using WhatsBiz.Application.Features.Products.DTOs;

namespace WhatsBiz.Application.Features.Products.Products;

public sealed record CreateProductCommand(ProductInput Input) : IRequest<ProductDto>;
public sealed record UpdateProductCommand(Guid ProductId, ProductInput Input) : IRequest<ProductDto>;
public sealed record DeleteProductCommand(Guid ProductId) : IRequest;
public sealed record ImportProductsCommand(byte[] Content) : IRequest<ImportProductsResult>;
public sealed record UploadProductImageCommand(Guid ProductId, string FileName, string ContentType, byte[] Content) : IRequest<ProductImageDto>;
public sealed record DeleteProductImageCommand(Guid ProductId) : IRequest;
