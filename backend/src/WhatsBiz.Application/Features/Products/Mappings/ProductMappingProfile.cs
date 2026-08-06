using AutoMapper;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Application.Features.Products.Mappings;

public sealed class ProductMappingProfile : Profile
{
    public ProductMappingProfile()
    {
        CreateMap<Product, ProductListItemDto>();
        CreateMap<Product, ProductDto>();
        CreateMap<ProductCategory, ProductCategoryDto>().ForMember(destination => destination.Children, options => options.Ignore());
        CreateMap<Brand, BrandDto>();
        CreateMap<UnitOfMeasure, UnitOfMeasureDto>();
    }
}
