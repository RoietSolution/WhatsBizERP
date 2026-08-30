using FluentValidation;
using WhatsBiz.Application.Features.Products.DTOs;

namespace WhatsBiz.Application.Features.Products.MasterData;

public sealed class ProductCategoryInputValidator : AbstractValidator<ProductCategoryInput> { public ProductCategoryInputValidator() { RuleFor(x => x.CategoryCode).MaximumLength(50); RuleFor(x => x.CategoryName).NotEmpty().MaximumLength(200); RuleFor(x => x.Description).MaximumLength(1000); RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0); } }
public sealed class CreateProductCategoryCommandValidator : AbstractValidator<CreateProductCategoryCommand> { public CreateProductCategoryCommandValidator() => RuleFor(x => x.Input).SetValidator(new ProductCategoryInputValidator()); }
public sealed class UpdateProductCategoryCommandValidator : AbstractValidator<UpdateProductCategoryCommand> { public UpdateProductCategoryCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Input).SetValidator(new ProductCategoryInputValidator()); RuleFor(x => x).Must(x => x.Input.ParentCategoryId != x.Id).WithMessage("A category cannot be its own parent."); } }
public sealed class BrandInputValidator : AbstractValidator<BrandInput> { public BrandInputValidator() { RuleFor(x => x.BrandCode).MaximumLength(50); RuleFor(x => x.BrandName).NotEmpty().MaximumLength(200); RuleFor(x => x.Description).MaximumLength(1000); RuleFor(x => x.Logo).MaximumLength(500); } }
public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand> { public CreateBrandCommandValidator() => RuleFor(x => x.Input).SetValidator(new BrandInputValidator()); }
public sealed class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand> { public UpdateBrandCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Input).SetValidator(new BrandInputValidator()); } }
public sealed class UnitInputValidator : AbstractValidator<UnitOfMeasureInput> { public UnitInputValidator() { RuleFor(x => x.UnitCode).MaximumLength(50); RuleFor(x => x.UnitName).NotEmpty().MaximumLength(200); RuleFor(x => x.ShortName).NotEmpty().MaximumLength(20); RuleFor(x => x.DecimalPlaces).InclusiveBetween((byte)0, (byte)6); } }
public sealed class CreateUnitOfMeasureCommandValidator : AbstractValidator<CreateUnitOfMeasureCommand> { public CreateUnitOfMeasureCommandValidator() => RuleFor(x => x.Input).SetValidator(new UnitInputValidator()); }
public sealed class UpdateUnitOfMeasureCommandValidator : AbstractValidator<UpdateUnitOfMeasureCommand> { public UpdateUnitOfMeasureCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Input).SetValidator(new UnitInputValidator()); } }
