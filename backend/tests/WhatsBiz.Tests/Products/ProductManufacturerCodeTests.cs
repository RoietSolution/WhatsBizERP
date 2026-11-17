using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Mappings;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductManufacturerCodeTests
{
    [Fact]
    public async Task CreatePersistsPrimaryEanAndAdditionalQrExactly()
    {
        const string qr = " https://manufacturer.example/products/ABC?lot=26 ";
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, tenant);
        var handler = Handler(db, tenant);

        var result = await handler.Handle(new CreateProductCommand(Input(references) with
        {
            Barcode = "8901234567890",
            BarcodeType = BarcodeTypes.Ean13,
            AdditionalBarcodes = [new(qr, BarcodeTypes.Qr)]
        }), default);

        result.Barcode.Should().Be("8901234567890");
        result.BarcodeType.Should().Be(BarcodeTypes.Ean13);
        result.AdditionalBarcodes.Should().ContainSingle(x => x.Barcode == qr && x.BarcodeType == BarcodeTypes.Qr);
        (await db.ProductBarcodes.SingleAsync()).TenantId.Should().Be(tenant);
    }

    [Fact]
    public async Task DuplicateCodesForCurrentProductDoNotCreateDuplicateRows()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, tenant);
        var handler = Handler(db, tenant);

        await handler.Handle(new CreateProductCommand(Input(references) with
        {
            Barcode = "PRIMARY",
            BarcodeType = BarcodeTypes.Custom,
            AdditionalBarcodes = [
                new("PRIMARY", BarcodeTypes.Custom),
                new("SECONDARY", BarcodeTypes.Code128),
                new("SECONDARY", BarcodeTypes.Code128)
            ]
        }), default);

        (await db.ProductBarcodes.ToArrayAsync()).Should().ContainSingle(x => x.Barcode == "SECONDARY");
    }

    [Fact]
    public async Task DuplicateIdentifierInSameTenantNamesOwningProduct()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, tenant);
        var first = Product(tenant, references, "Existing product", "SHARED");
        db.Products.Add(first);
        await db.SaveChangesAsync();

        var action = () => Handler(db, tenant).Handle(new CreateProductCommand(Input(references) with
        {
            AdditionalBarcodes = [new("SHARED", BarcodeTypes.Code128)]
        }), default);

        await action.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Existing product*");
    }

    [Fact]
    public async Task SameManufacturerIdentifierIsAllowedAcrossTenants()
    {
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, firstTenant, secondTenant);
        var first = Product(firstTenant, references, "First retailer product", null);
        db.Products.Add(first);
        db.ProductBarcodes.Add(new ProductBarcode { TenantId = firstTenant, ProductId = first.ProductId, Barcode = "MANUFACTURER-SHARED", BarcodeType = BarcodeTypes.Code128 });
        await db.SaveChangesAsync();

        var result = await Handler(db, secondTenant).Handle(new CreateProductCommand(Input(references) with
        {
            AdditionalBarcodes = [new("MANUFACTURER-SHARED", BarcodeTypes.Code128)]
        }), default);

        result.AdditionalBarcodes.Should().ContainSingle(x => x.Barcode == "MANUFACTURER-SHARED");
        (await db.ProductBarcodes.CountAsync(x => x.Barcode == "MANUFACTURER-SHARED")).Should().Be(2);
    }

    [Fact]
    public async Task ProductDeletionRetainsImageAndBusinessIdentifiersForRecovery()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, tenant);
        var product = Product(tenant, references, "Deleted product", null);
        db.Products.Add(product);
        db.ProductBarcodes.Add(new ProductBarcode
        {
            TenantId = tenant,
            ProductId = product.ProductId,
            Barcode = "REUSABLE-AFTER-DELETE",
            BarcodeType = BarcodeTypes.Code128
        });
        await db.SaveChangesAsync();

        var currentUser = new CurrentUser(tenant);
        var cleanup = new ProductImageCleanupQueue(db, Options.Create(new ProductImageStorageOptions { DeletionRetentionDays = 30 }));
        var image = new ProductImage { TenantId = tenant, ProductId = product.ProductId, StorageProvider = ProductImageStorageProviders.Database, ImageData = [7], ThumbnailData = [8] };
        db.ProductImages.Add(image);
        await db.SaveChangesAsync();
        await new DeleteProductCommandHandler(new ProductRepository(db, currentUser), cleanup, currentUser)
            .Handle(new DeleteProductCommand(product.ProductId), default);

        var barcode = await db.ProductBarcodes.SingleAsync();
        barcode.IsDeleted.Should().BeFalse();
        barcode.IsActive.Should().BeTrue();
        (await db.ProductImages.SingleAsync()).ImageData.Should().Equal(7);
        (await db.ProductImageCleanupJobs.CountAsync(x => x.ProductImageId == image.ProductImageId && x.Status == "PENDING")).Should().Be(1);
        (await db.ProductImageCleanupJobs.CountAsync(x => x.ProductImageId == null && x.Status == "PENDING")).Should().Be(1);
    }

    [Fact]
    public async Task ProductCanBeRestoredBeforeCleanupAndPendingJobsAreCancelled()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDb();
        var references = SeedReferences(db, tenant);
        var product = Product(tenant, references, "Recoverable product", null);
        product.IsActive = false;
        db.Products.Add(product);
        db.ProductImages.Add(new ProductImage { TenantId = tenant, ProductId = product.ProductId, StorageProvider = ProductImageStorageProviders.Database, ImageData = [4] });
        await db.SaveChangesAsync();
        var currentUser = new CurrentUser(tenant);
        var cleanup = new ProductImageCleanupQueue(db, Options.Create(new ProductImageStorageOptions { DeletionRetentionDays = 30 }));
        var repository = new ProductRepository(db, currentUser);

        await new DeleteProductCommandHandler(repository, cleanup, currentUser).Handle(new DeleteProductCommand(product.ProductId), default);
        await new RestoreProductCommandHandler(repository, cleanup, currentUser).Handle(new RestoreProductCommand(product.ProductId), default);

        product.IsDeleted.Should().BeFalse();
        product.IsActive.Should().BeFalse("the original activation state must be restored");
        (await db.ProductImages.SingleAsync()).ImageData.Should().Equal(4);
        (await db.ProductImageCleanupJobs.Where(x => x.Source == "PRODUCT_DELETE").AllAsync(x => x.Status == "CANCELLED")).Should().BeTrue();
    }

    [Fact]
    public async Task ProductDeactivationDoesNotScheduleCleanupOrChangeImages()
    {
        var tenant = Guid.NewGuid(); await using var db = CreateDb(); var refs = SeedReferences(db, tenant);
        var product = Product(tenant, refs, "Inactive product", null); var image = new ProductImage { TenantId = tenant, ProductId = product.ProductId, ImageData = [1, 2], ThumbnailData = [3] };
        db.Products.Add(product); db.ProductImages.Add(image); await db.SaveChangesAsync();
        await new SetProductStatusCommandHandler(new ProductRepository(db, new CurrentUser(tenant)), new CurrentUser(tenant)).Handle(new SetProductStatusCommand(product.ProductId, false), default);
        (await db.ProductImages.SingleAsync()).ImageData.Should().Equal(1, 2); (await db.ProductImages.SingleAsync()).IsDeleted.Should().BeFalse();
        (await db.ProductImageCleanupJobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemovingGalleryImageRetainsBytesAndSchedulesDelayedIdempotentCleanup()
    {
        var tenant = Guid.NewGuid(); await using var db = CreateDb(); var refs = SeedReferences(db, tenant);
        var product = Product(tenant, refs, "Gallery product", null); var image = new ProductImage { TenantId = tenant, ProductId = product.ProductId, IsPrimary = true, ImageData = [9], ThumbnailData = [8] };
        db.Products.Add(product); db.ProductImages.Add(image); await db.SaveChangesAsync();
        var user = new CurrentUser(tenant); var queue = new ProductImageCleanupQueue(db, Options.Create(new ProductImageStorageOptions { DeletionRetentionDays = 12 }));
        await new DeleteProductImageCommandHandler(new ProductRepository(db, user), queue, user).Handle(new DeleteProductImageCommand(product.ProductId, image.ProductImageId), default);
        var deleted = await db.ProductImages.SingleAsync(); var job = await db.ProductImageCleanupJobs.SingleAsync();
        deleted.IsDeleted.Should().BeTrue(); deleted.ImageData.Should().Equal(9); deleted.ThumbnailData.Should().Equal(8);
        job.Status.Should().Be("PENDING"); job.DeleteAfterUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(11)); job.DeleteAfterUtc.Should().BeBefore(DateTimeOffset.UtcNow.AddDays(13));
    }

    [Fact]
    public async Task LongQrContentIsRejectedAtSafeLimit()
    {
        var references = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var input = Input(references) with
        {
            AdditionalBarcodes = [new(new string('Q', 451), BarcodeTypes.Qr)]
        };

        var validation = await new ProductInputValidator().ValidateAsync(input);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(x => x.PropertyName.Contains(nameof(ProductBarcodeInput.Barcode)));
    }

    [Fact]
    public void MigrationSeparatesColumnCreationAndValidatesTenantOwnedIndexesWithoutDeletingData()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "WhatsBiz.Database", "Scripts", "V22-ProductManufacturerCodes.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var sql = File.ReadAllText(Path.Combine(root!.FullName, "database", "WhatsBiz.Database", "Scripts", "V22-ProductManufacturerCodes.sql"));
        var tenantAdd = sql.IndexOf("ADD TenantId UNIQUEIDENTIFIER NULL", StringComparison.Ordinal);
        var batchBoundary = sql.IndexOf("GO", tenantAdd, StringComparison.Ordinal);
        var tenantBackfill = sql.IndexOf("SET TenantId = p.TenantId", StringComparison.Ordinal);
        sql.Should().Contain("SET QUOTED_IDENTIFIER ON")
            .And.Contain("ADD TenantId UNIQUEIDENTIFIER NULL")
            .And.Contain("WHERE b.TenantId IS NULL")
            .And.Contain("b.TenantId <> p.TenantId")
            .And.Contain("UX_ProductBarcodes_Tenant_Barcode")
            .And.Contain("ON master.ProductBarcodes(TenantId, Barcode)")
            .And.Contain("WHERE IsActive = 1 AND IsDeleted = 0")
            .And.Contain("sys.index_columns")
            .And.Contain("sys.foreign_key_columns")
            .And.NotContain("DELETE FROM master.ProductBarcodes");
        tenantAdd.Should().BeGreaterThan(-1);
        batchBoundary.Should().BeGreaterThan(tenantAdd).And.BeLessThan(tenantBackfill);
    }

    private static CreateProductCommandHandler Handler(ApplicationDbContext db, Guid tenant) =>
        new(new ProductRepository(db, new CurrentUser(tenant)), new CurrentUser(tenant), Mapper());

    private static IMapper Mapper()
    {
        var configuration = new MapperConfiguration(x => x.AddProfile<ProductMappingProfile>(), NullLoggerFactory.Instance);
        return configuration.CreateMapper();
    }

    private static (Guid Category, Guid Brand, Guid Unit) SeedReferences(ApplicationDbContext db, params Guid[] tenants)
    {
        var category = new ProductCategory { CategoryCode = Guid.NewGuid().ToString("N"), CategoryName = "Category" };
        var brand = new Brand { BrandCode = Guid.NewGuid().ToString("N"), BrandName = "Brand" };
        var unit = new UnitOfMeasure { UnitCode = Guid.NewGuid().ToString("N"), UnitName = "Each", ShortName = "EA" };
        db.ProductCategories.Add(category);
        db.Brands.Add(brand);
        db.UnitsOfMeasure.Add(unit);
        foreach (var tenant in tenants)
        {
            AddTenantLink(db, "TenantProductCategory", "ProductCategoryId", tenant, category.ProductCategoryId);
            AddTenantLink(db, "TenantBrand", "BrandId", tenant, brand.BrandId);
            AddTenantLink(db, "TenantUnitOfMeasure", "UnitId", tenant, unit.UnitId);
        }
        db.SaveChanges();
        return (category.ProductCategoryId, brand.BrandId, unit.UnitId);
    }

    private static void AddTenantLink(ApplicationDbContext db, string entityName, string keyName, Guid tenantId, Guid keyValue)
    {
        var type = db.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == entityName).ClrType;
        var link = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("TenantId")!.SetValue(link, tenantId);
        type.GetProperty(keyName)!.SetValue(link, keyValue);
        db.Add(link);
    }

    private static ProductInput Input((Guid Category, Guid Brand, Guid Unit) refs) =>
        new("P-001", null, "New product", null, null, refs.Category, refs.Brand, refs.Unit, null, null, 18, 10, 12, 15, 0, 100, 5, null, null, null, null, false, false, true, BarcodeTypes.Code128, []);

    private static Product Product(Guid tenant, (Guid Category, Guid Brand, Guid Unit) refs, string name, string? barcode) => new()
    {
        TenantId = tenant,
        ProductCode = Guid.NewGuid().ToString("N"),
        ProductName = name,
        Barcode = barcode,
        CategoryId = refs.Category,
        BrandId = refs.Brand,
        UnitId = refs.Unit
    };

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CurrentUser(Guid tenant) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenant;
        public string? Username => "product-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
