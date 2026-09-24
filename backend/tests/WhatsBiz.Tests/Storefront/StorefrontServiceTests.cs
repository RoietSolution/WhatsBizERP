using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Domain.Warehouses;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontServiceTests
{
    private static readonly string[] SensitiveFieldNames = ["TenantId", "PurchasePrice", "Cost", "QuantityOnHand", "QuantityAvailable", "SupplierId"];
    [Fact]
    public async Task StoreKeyResolvesOnlyAnActiveTenantAndRejectsInvalidKeys()
    {
        await using var db = CreateDb();
        var active = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "GUTURGO", Name = "GuturGo", IsActive = true };
        var inactive = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "CLOSED", Name = "Closed Store", IsActive = false };
        db.Tenants.AddRange(active, inactive);
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());

        (await service.GetStoreAsync("guturgo", default))!.StoreKey.Should().Be("guturgo");
        (await service.GetStoreAsync("closed", default)).Should().BeNull();
        (await service.GetStoreAsync("unknown", default)).Should().BeNull();
        (await service.GetStoreAsync("../GUTURGO", default)).Should().BeNull();
    }

    [Fact]
    public async Task ProductAndCategoryQueriesAreTenantScopedAndHonorVisibility()
    {
        await using var db = CreateDb();
        var first = await AddCatalogAsync(db, "FIRST", "Store One", withStock: false);
        var other = await AddCatalogAsync(db, "OTHER", "Store Two", withStock: false);
        var hidden = await AddProductAsync(db, first, "Hidden", isVisible: false, isActive: true);
        var inactive = await AddProductAsync(db, first, "Inactive", isVisible: true, isActive: false);
        db.SaveChanges();
        var service = new StorefrontService(db, new NoProductImages());

        var products = (await service.GetProductsAsync("FIRST", default))!;
        products.Should().ContainSingle().Which.Id.Should().Be(first.Product.ProductId);
        (await service.GetProductAsync("FIRST", other.Product.ProductId, default)).Should().BeNull();
        (await service.GetProductAsync("FIRST", hidden.Product.ProductId, default)).Should().BeNull();
        (await service.GetProductAsync("FIRST", inactive.Product.ProductId, default)).Should().BeNull();
        (await service.GetCategoriesAsync("FIRST", default))!.Should().ContainSingle().Which.Id.Should().Be(first.Category.ProductCategoryId);
    }

    [Fact]
    public async Task AvailabilityUsesTenantInventoryAndPublicDtoOmitsSensitiveFields()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "STOCKED", "Stocked Store", withStock: true);
        var empty = await AddProductAsync(db, catalog, "No Stock", isVisible: true, isActive: true);
        empty.Product.CategoryId = catalog.Category.ProductCategoryId;
        empty.Product.Category = catalog.Category;
        empty.Product.Unit = catalog.Unit;
        empty.Product.Brand = catalog.Brand;
        var otherTenant = await AddCatalogAsync(db, "OTHER", "Other Store", withStock: false);
        var otherWarehouse = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = "WH-OTHER", WarehouseName = "Other Store", IsActive = true };
        db.Warehouses.Add(otherWarehouse);
        db.Entry(otherWarehouse).Property<Guid?>("TenantId").CurrentValue = otherTenant.Tenant.TenantId;
        await AddBalanceAsync(db, catalog.Tenant.TenantId, catalog.Product.ProductId, catalog.Warehouse!, onHand: 5, reserved: 2);
        await AddBalanceAsync(db, otherTenant.Tenant.TenantId, empty.Product.ProductId, otherWarehouse, onHand: 50, reserved: 0);
        db.SaveChanges();
        var service = new StorefrontService(db, new NoProductImages());

        var products = (await service.GetProductsAsync("STOCKED", default))!.ToDictionary(item => item.Id);
        products[catalog.Product.ProductId].Availability.Should().Be("IN_STOCK");
        products[empty.Product.ProductId].Availability.Should().Be("OUT_OF_STOCK");
        var publicFields = typeof(StorefrontProductDto).GetProperties().Select(property => property.Name);
        publicFields.Should().NotContain(name => SensitiveFieldNames.Contains(name));
    }

    private static async Task<CatalogFixture> AddCatalogAsync(ApplicationDbContext db, string key, string name, bool withStock)
    {
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = key, Name = name, IsActive = true };
        var category = new ProductCategory { ProductCategoryId = Guid.NewGuid(), CategoryCode = $"CAT-{key}", CategoryName = "Pantry", IsActive = true };
        var unit = new UnitOfMeasure { UnitId = Guid.NewGuid(), UnitCode = $"UNIT-{key}", UnitName = "Each", ShortName = "ea", IsActive = true };
        var brand = new Brand { BrandId = Guid.NewGuid(), BrandCode = $"BR-{key}", BrandName = "Local", IsActive = true };
        var product = new Product { ProductId = Guid.NewGuid(), TenantId = tenant.TenantId, ProductCode = $"P-{key}", ProductName = $"Product {key}", CategoryId = category.ProductCategoryId, Category = category, UnitId = unit.UnitId, Unit = unit, BrandId = brand.BrandId, Brand = brand, SellingPrice = 25, MRP = 30, IsActive = true, IsWhatsAppVisible = true };
        db.Tenants.Add(tenant);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasure.Add(unit);
        db.Brands.Add(brand);
        db.Products.Add(product);
        AddTenantLink(db, "TenantProductCategory", "TenantId", tenant.TenantId, "ProductCategoryId", category.ProductCategoryId);
        AddTenantLink(db, "TenantUnitOfMeasure", "TenantId", tenant.TenantId, "UnitId", unit.UnitId);
        Warehouse? warehouse = null;
        if (withStock)
        {
            warehouse = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = $"WH-{key}", WarehouseName = name, IsActive = true };
            db.Warehouses.Add(warehouse);
            db.Entry(warehouse).Property<Guid?>("TenantId").CurrentValue = tenant.TenantId;
        }
        return await Task.FromResult(new CatalogFixture(tenant, category, unit, brand, product, warehouse));
    }

    private static async Task<ProductFixture> AddProductAsync(ApplicationDbContext db, CatalogFixture catalog, string name, bool isVisible, bool isActive)
    {
        var product = new Product { ProductId = Guid.NewGuid(), TenantId = catalog.Tenant.TenantId, ProductCode = $"P-{Guid.NewGuid():N}", ProductName = name, CategoryId = catalog.Category.ProductCategoryId, Category = catalog.Category, UnitId = catalog.Unit.UnitId, Unit = catalog.Unit, BrandId = catalog.Brand.BrandId, Brand = catalog.Brand, SellingPrice = 8, MRP = 8, IsActive = isActive, IsWhatsAppVisible = isVisible };
        db.Products.Add(product);
        return await Task.FromResult(new ProductFixture(product));
    }

    private static async Task AddBalanceAsync(ApplicationDbContext db, Guid tenantId, Guid productId, Warehouse warehouse, decimal onHand, decimal reserved)
    {
        var product = db.Products.Local.SingleOrDefault(item => item.ProductId == productId) ?? await db.Products.SingleAsync(item => item.ProductId == productId);
        var balance = new InventoryBalance { InventoryBalanceId = Guid.NewGuid(), ProductId = productId, Product = product, WarehouseId = warehouse.WarehouseId, Warehouse = warehouse, QuantityOnHand = onHand, QuantityReserved = reserved };
        db.InventoryBalances.Add(balance);
        db.Entry(balance).Property<Guid?>("TenantId").CurrentValue = tenantId;
    }

    private static void AddTenantLink(ApplicationDbContext db, string typeName, string tenantProperty, Guid tenantId, string idProperty, Guid id)
    {
        var type = typeof(ApplicationDbContext).Assembly.GetType($"WhatsBiz.Infrastructure.Persistence.{typeName}", throwOnError: true)!;
        var row = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty(tenantProperty, BindingFlags.Public | BindingFlags.Instance)!.SetValue(row, tenantId);
        type.GetProperty(idProperty, BindingFlags.Public | BindingFlags.Instance)!.SetValue(row, id);
        db.Add(row);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record CatalogFixture(Tenant Tenant, ProductCategory Category, UnitOfMeasure Unit, Brand Brand, Product Product, Warehouse? Warehouse);
    private sealed record ProductFixture(Product Product);

    private sealed class NoProductImages : IProductImageStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken) => Task.FromResult<ProductImageStorageContent?>(null);
        public Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
