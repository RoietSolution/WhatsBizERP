using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductImageProjectionTests
{
    [Fact]
    public async Task ProductListAndDetailsResolveSameTenantPrimaryImageForActiveAndInactiveProducts()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        var category = new ProductCategory { CategoryCode = "CAT", CategoryName = "Category" };
        var brand = new Brand { BrandCode = "BR", BrandName = "Brand" };
        var unit = new UnitOfMeasure { UnitCode = "EA", UnitName = "Each", ShortName = "ea" };
        var active = new Product { TenantId = tenantId, ProductName = "Active", ProductCode = "ACTIVE", Category = category, Brand = brand, Unit = unit };
        var inactive = new Product { TenantId = tenantId, ProductName = "Inactive", ProductCode = "INACTIVE", IsActive = false, ImageUrl = null, Category = category, Brand = brand, Unit = unit };
        var other = new Product { TenantId = Guid.NewGuid(), ProductName = "Other", ProductCode = "OTHER", Category = category, Brand = brand, Unit = unit };
        db.Products.AddRange(active, inactive, other);
        var activeImageId = Guid.NewGuid();
        var inactiveImageId = Guid.NewGuid();
        db.ProductImages.AddRange(
            new ProductImage { ProductImageId = activeImageId, TenantId = tenantId, ProductId = active.ProductId, IsPrimary = true },
            new ProductImage { ProductImageId = inactiveImageId, TenantId = tenantId, ProductId = inactive.ProductId, IsPrimary = true },
            new ProductImage { TenantId = other.TenantId, ProductId = other.ProductId, IsPrimary = true });
        await db.SaveChangesAsync();
        var repository = new ProductRepository(db, new CurrentUser(tenantId));

        var activeRow = (await repository.SearchAsync(null, true, null, null, "productName", false, 1, 20, default)).Items.Single();
        var inactiveRow = (await repository.SearchAsync(null, false, null, null, "productName", false, 1, 20, default)).Items.Single();
        activeRow.ImageUrl.Should().Be($"/api/products/{active.ProductId}/images/{activeImageId}");
        inactiveRow.ImageUrl.Should().Be($"/api/products/{inactive.ProductId}/images/{inactiveImageId}");
        (await repository.GetAsync(inactive.ProductId, false, default))!.ImageUrl.Should().Be(inactiveRow.ImageUrl);

        inactive.IsActive = true;
        await db.SaveChangesAsync();
        var activeAgain = (await repository.SearchAsync("Inactive", true, null, null, "productName", false, 1, 20, default)).Items.Single();
        activeAgain.ImageUrl.Should().Be(inactiveRow.ImageUrl);
        inactive.IsActive = false;
        await db.SaveChangesAsync();
        var inactiveAgain = (await repository.SearchAsync("Inactive", false, null, null, "productName", false, 1, 20, default)).Items.Single();
        inactiveAgain.ImageUrl.Should().Be(inactiveRow.ImageUrl);
        (await repository.SearchAsync(null, null, null, null, "productName", false, 1, 20, default)).Items
            .Should().NotContain(x => x.ProductId == other.ProductId);
    }

    private sealed class CurrentUser(Guid tenant) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenant;
        public string? Username => "image-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
