using FluentValidation;
using WhatsBiz.Application.Features.Products.DTOs;

namespace WhatsBiz.Application.Features.Products.Products;

public sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    public ProductInputValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Barcode).MaximumLength(100);
        RuleFor(x => x.ProductName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.BrandId).NotEmpty();
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.GSTPercentage).InclusiveBetween(0, 100);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(x => x.PurchasePrice);
        RuleFor(x => x.MRP).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaximumStock).GreaterThanOrEqualTo(x => x.MinimumStock);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Weight).GreaterThanOrEqualTo(0).When(x => x.Weight.HasValue);
        RuleFor(x => x.Length).GreaterThanOrEqualTo(0).When(x => x.Length.HasValue);
        RuleFor(x => x.Width).GreaterThanOrEqualTo(0).When(x => x.Width.HasValue);
        RuleFor(x => x.Height).GreaterThanOrEqualTo(0).When(x => x.Height.HasValue);
    }
}

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand> { public CreateProductCommandValidator() => RuleFor(x => x.Input).SetValidator(new ProductInputValidator()); }
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand> { public UpdateProductCommandValidator() { RuleFor(x => x.ProductId).NotEmpty(); RuleFor(x => x.Input).SetValidator(new ProductInputValidator()); } }
public sealed class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand> { public DeleteProductCommandValidator() => RuleFor(x => x.ProductId).NotEmpty(); }
public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery> { public GetProductByIdQueryValidator() => RuleFor(x => x.ProductId).NotEmpty(); }
public sealed class GetProductsQueryValidator : AbstractValidator<GetProductsQuery> { public GetProductsQueryValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 200); RuleFor(x => x.Search).MaximumLength(250); } }
public sealed class ImportProductsCommandValidator : AbstractValidator<ImportProductsCommand> { public ImportProductsCommandValidator() => RuleFor(x => x.Content).NotEmpty().Must(content => content.Length <= 10 * 1024 * 1024).WithMessage("The workbook cannot exceed 10 MB."); }
public sealed class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand> { private static readonly string[] Allowed = ["image/jpeg", "image/png", "image/webp"]; public UploadProductImageCommandValidator() { RuleFor(x => x.ProductId).NotEmpty(); RuleFor(x => x.FileName).NotEmpty().MaximumLength(255); RuleFor(x => x.ContentType).Must(contentType => Allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase)).WithMessage("Only JPEG, PNG, and WebP images are supported."); RuleFor(x => x.Content).NotEmpty().Must(content => content.Length <= 5 * 1024 * 1024).WithMessage("The image cannot exceed 5 MB."); } }
