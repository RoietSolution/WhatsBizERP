using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.POS;

public sealed class POSBarcodeLookupTests
{
    [Fact]
    public async Task ExactBarcodeLookupReturnsOnlyCurrentTenantActiveProduct()
    {
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await using var db = CreateDb();
        var expected = Product(tenant, "8901234567890", "Current tenant");
        db.Products.AddRange(
            expected,
            Product(otherTenant, "OTHER-BARCODE", "Other tenant"),
            Product(tenant, "INACTIVE", "Inactive", active: false),
            Product(tenant, "DELETED", "Deleted", deleted: true));
        await db.SaveChangesAsync();

        var repository = new POSRepository(db, new CurrentUser(tenant));

        (await repository.Products(null, "8901234567890", null, 20, default))
            .Should().ContainSingle(x => x.Product.ProductId == expected.ProductId);
        (await repository.Products(null, "OTHER-BARCODE", null, 20, default)).Should().BeEmpty();
        (await repository.Products(null, "INACTIVE", null, 20, default)).Should().BeEmpty();
        (await repository.Products(null, "DELETED", null, 20, default)).Should().BeEmpty();
        (await repository.Products(null, "UNKNOWN", null, 20, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExactBarcodeLookupSupportsExistingCustomProductBarcodes()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var product = Product(tenant, null, "Custom barcode product");
        db.Products.Add(product);
        db.ProductBarcodes.Add(new ProductBarcode
        {
            ProductId = product.ProductId,
            Barcode = "CUSTOM-128-001",
            IsPrimary = true,
        });
        await db.SaveChangesAsync();

        var result = await new POSRepository(db, new CurrentUser(tenant))
            .Products(null, " CUSTOM-128-001 ", null, 1, default);

        result.Should().ContainSingle();
        result.Single().Product.ProductId.Should().Be(product.ProductId);
        result.Single().MatchedBarcode.Should().Be("CUSTOM-128-001");
    }

    [Fact]
    public async Task WarehouseLookupUsesExistingAvailableAndNegativeStockRules()
    {
        var tenant = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
        await using var db = CreateDb();
        var product = Product(tenant, "STOCKED", "Stocked product");
        db.Products.Add(product);
        db.InventoryBalances.Add(new InventoryBalance
        {
            ProductId = product.ProductId,
            WarehouseId = warehouse,
            QuantityOnHand = 6,
            QuantityReserved = 2,
        });
        db.InventorySettings.Add(new InventorySettings
        {
            InventorySettingsId = Guid.NewGuid(),
            NegativeStockAllowed = false,
        });
        await db.SaveChangesAsync();

        var result = (await new POSRepository(db, new CurrentUser(tenant))
            .Products(null, "STOCKED", warehouse, 1, default)).Single();

        result.AvailableQuantity.Should().Be(4);
        result.NegativeStockAllowed.Should().BeFalse();
    }

    [Fact]
    public void DatabaseMigrationMakesPrimaryBarcodeUniquePerTenant()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "WhatsBiz.Database", "Scripts", "V21-POSMobileBarcodeScanner.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var sql = File.ReadAllText(Path.Combine(root!.FullName, "database", "WhatsBiz.Database", "Scripts", "V21-POSMobileBarcodeScanner.sql"));
        sql.Should().Contain("DROP INDEX [UX_Products_Barcode]")
            .And.Contain("([TenantId], [Barcode])")
            .And.Contain("[Barcode] IS NOT NULL AND [IsDeleted] = 0");
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Product Product(Guid tenant, string? barcode, string name, bool active = true, bool deleted = false) => new()
    {
        TenantId = tenant,
        ProductCode = Guid.NewGuid().ToString("N"),
        Barcode = barcode,
        ProductName = name,
        CategoryId = Guid.NewGuid(),
        BrandId = Guid.NewGuid(),
        UnitId = Guid.NewGuid(),
        IsActive = active,
        IsDeleted = deleted,
    };

    private sealed class CurrentUser(Guid tenant) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenant;
        public string? Username => "pos-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
