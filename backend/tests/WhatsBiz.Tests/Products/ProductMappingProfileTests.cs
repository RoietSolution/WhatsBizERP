using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Mappings;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductMappingProfileTests
{
    private readonly IMapper mapper;

    public ProductMappingProfileTests()
    {
        var configuration = new MapperConfiguration(configuration => configuration.AddProfile<ProductMappingProfile>(), NullLoggerFactory.Instance);
        configuration.AssertConfigurationIsValid();
        mapper = configuration.CreateMapper();
    }

    [Fact]
    public void ProductMapsNavigationNamesToListAndDetailDtos()
    {
        var product = CreateProduct();

        var listItem = mapper.Map<ProductListItemDto>(product);
        var detail = mapper.Map<ProductDto>(product);

        Assert.Equal("Category", listItem.CategoryName);
        Assert.Equal("Brand", listItem.BrandName);
        Assert.Equal("Unit", listItem.UnitName);
        Assert.Equal("Category", detail.CategoryName);
        Assert.Equal("Brand", detail.BrandName);
        Assert.Equal("Unit", detail.UnitName);
    }

    [Fact]
    public void ProductCategoryMapsWithAnEmptyChildrenCollection()
    {
        var category = mapper.Map<ProductCategoryDto>(new ProductCategory());

        Assert.Empty(category.Children);
    }

    private static Product CreateProduct() => new()
    {
        Category = new ProductCategory { CategoryName = "Category" },
        Brand = new Brand { BrandName = "Brand" },
        Unit = new UnitOfMeasure { UnitName = "Unit" }
    };
}
