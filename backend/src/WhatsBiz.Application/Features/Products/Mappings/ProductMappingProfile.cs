using AutoMapper;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Application.Features.Products.Mappings;

public sealed class ProductMappingProfile : Profile
{
    public ProductMappingProfile()
    {
        CreateMap<Product, ProductListItemDto>()
            .ForCtorParam(nameof(ProductListItemDto.CategoryName), options => options.MapFrom(source => source.Category.CategoryName))
            .ForCtorParam(nameof(ProductListItemDto.BrandName), options => options.MapFrom(source => source.Brand.BrandName))
            .ForCtorParam(nameof(ProductListItemDto.UnitName), options => options.MapFrom(source => source.Unit.UnitName));
        CreateMap<Product, ProductDto>()
            .ForCtorParam(nameof(ProductDto.CategoryName), options => options.MapFrom(source => source.Category.CategoryName))
            .ForCtorParam(nameof(ProductDto.BrandName), options => options.MapFrom(source => source.Brand.BrandName))
            .ForCtorParam(nameof(ProductDto.UnitName), options => options.MapFrom(source => source.Unit.UnitName))
            .ForCtorParam(nameof(ProductDto.AdditionalBarcodes), options => options.MapFrom(source => source.Barcodes.Where(x => x.IsActive && !x.IsDeleted && !x.IsPrimary && x.Barcode != source.Barcode)));
        CreateMap<ProductBarcode, ProductBarcodeDto>();
        CreateMap<ProductCategory, ProductCategoryDto>()
            .ForCtorParam(nameof(ProductCategoryDto.Children), options => options.MapFrom(_ => Array.Empty<ProductCategoryDto>()));
        CreateMap<Brand, BrandDto>();
        CreateMap<UnitOfMeasure, UnitOfMeasureDto>();
    }
}
