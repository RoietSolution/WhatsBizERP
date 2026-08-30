using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Mappings;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Products;

public sealed class ProductMasterCodeGenerationTests
{
    [Fact]
    public void BlankTechnicalCodesAreAcceptedByMasterDataValidation()
    {
        var category = new ProductCategoryInput(null, "Retail", null, 0, null, true);
        var brand = new BrandInput("", "Acme", null, null, true);
        var unit = new UnitOfMeasureInput(null, "Packet", "PKT", 0, true);
        new ProductCategoryInputValidator().Validate(category).IsValid.Should().BeTrue();
        new BrandInputValidator().Validate(brand).IsValid.Should().BeTrue();
        new UnitInputValidator().Validate(unit).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task BlankCategoryBrandAndUnitCodesAreGeneratedFromTheirIds()
    {
        await using var db = CreateDb();
        var repository = new ProductRepository(db, new CurrentUser());
        var currentUser = new CurrentUser();
        var mapper = Mapper();

        var category = await new CreateProductCategoryCommandHandler(repository, currentUser)
            .Handle(new CreateProductCategoryCommand(new(null, "Retail", null, 0, null, true)), default);
        var brand = await new CreateBrandCommandHandler(repository, currentUser, mapper)
            .Handle(new CreateBrandCommand(new("", "Acme", null, null, true)), default);
        var unit = await new CreateUnitOfMeasureCommandHandler(repository, currentUser, mapper)
            .Handle(new CreateUnitOfMeasureCommand(new(null, "Packet", "PKT", 0, true)), default);

        category.CategoryCode.Should().Be($"CAT-{category.ProductCategoryId:N}");
        brand.BrandCode.Should().Be($"BR-{brand.BrandId:N}");
        unit.UnitCode.Should().Be($"UOM-{unit.UnitId:N}");
    }

    [Fact]
    public async Task BlankCodeOnEditPreservesTheExistingGeneratedCode()
    {
        await using var db = CreateDb();
        var repository = new ProductRepository(db, new CurrentUser());
        var currentUser = new CurrentUser();
        var mapper = Mapper();
        var created = await new CreateBrandCommandHandler(repository, currentUser, mapper)
            .Handle(new CreateBrandCommand(new(null, "Original", null, null, true)), default);

        var updated = await new UpdateBrandCommandHandler(repository, currentUser, mapper)
            .Handle(new UpdateBrandCommand(created.BrandId, new(null, "Renamed", null, null, true)), default);

        updated.BrandCode.Should().Be(created.BrandCode);
        updated.BrandName.Should().Be("Renamed");
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IMapper Mapper()
    {
        var configuration = new MapperConfiguration(x => x.AddProfile<ProductMappingProfile>(), NullLoggerFactory.Instance);
        return configuration.CreateMapper();
    }

    private sealed class CurrentUser : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => Guid.NewGuid();
        public string? Username => "master-code-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
