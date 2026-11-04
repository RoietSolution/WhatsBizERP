using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Products;

public sealed class ProductCategoryImportPersistenceTests
{
    [Fact]
    public async Task NewCategoryAndTenantAssociationArePersistedTogether()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var handler = CreateHandler(db, user, new CategoryImportRow(1, "FOOD", "Food", null, 10, null, true));

        var result = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        result.ImportedCount.Should().Be(1);
        result.Errors.Should().BeEmpty();
        (await new ProductRepository(db, user).GetCategoriesAsync(default)).Should().ContainSingle()
            .Which.CategoryCode.Should().Be("FOOD");
    }

    [Fact]
    public async Task ExistingTenantCategoryIsNotRecreatedOrRemapped()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var repository = new ProductRepository(db, user);
        var existing = new ProductCategory { CategoryCode = "FOOD", CategoryName = "Existing Food" };
        repository.Add(existing);
        await repository.SaveChangesAsync(default);
        var handler = CreateHandler(db, user, new CategoryImportRow(1, " food ", "Imported Name", null, 20, null, true));

        var result = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        result.ImportedCount.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Contain("already exists");
        var categories = await repository.GetCategoriesAsync(default);
        categories.Should().ContainSingle();
        categories.Single().ProductCategoryId.Should().Be(existing.ProductCategoryId);
        categories.Single().CategoryName.Should().Be("Existing Food");
    }

    [Fact]
    public async Task ExistingMasterCategoryIsReusedForTheTenantAssociation()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var master = new ProductCategory { CategoryCode = "FOOD", CategoryName = "Master Food" };
        db.ProductCategories.Add(master);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user, new CategoryImportRow(1, "FOOD", "Imported Food", null, 10, null, true));

        var result = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        result.ImportedCount.Should().Be(1);
        var categories = await new ProductRepository(db, user).GetCategoriesAsync(default);
        categories.Should().ContainSingle();
        categories.Single().ProductCategoryId.Should().Be(master.ProductCategoryId);
        categories.Single().CategoryName.Should().Be("Master Food");
    }

    [Fact]
    public async Task ParentAndChildImportResolvesForwardReferenceAndPersistsBothLinks()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var handler = CreateHandler(db, user,
            new CategoryImportRow(1, "CHILD", "Child", null, 20, " ROOT ", true),
            new CategoryImportRow(2, "ROOT", "Root", null, 10, null, true));

        var result = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        result.ImportedCount.Should().Be(2);
        result.Errors.Should().BeEmpty();
        var categories = await new ProductRepository(db, user).GetCategoriesAsync(default);
        var root = categories.Single(x => x.CategoryCode == "ROOT");
        categories.Single(x => x.CategoryCode == "CHILD").ParentCategoryId.Should().Be(root.ProductCategoryId);
    }

    [Fact]
    public async Task DuplicateCodesAfterTrimmingAreReportedWithoutDuplicateAssociation()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var handler = CreateHandler(db, user,
            new CategoryImportRow(1, "FOOD", "Food", null, 10, null, true),
            new CategoryImportRow(2, " food ", "Duplicate", null, 20, null, true));

        var first = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);
        var second = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        first.ImportedCount.Should().Be(1);
        first.Errors.Should().ContainSingle();
        second.ImportedCount.Should().Be(0);
        (await new ProductRepository(db, user).GetCategoriesAsync(default)).Should().ContainSingle();
    }

    [Fact]
    public async Task MissingAndSelfParentReferencesAreRejectedWithoutPersistingRows()
    {
        await using var db = CreateDb();
        var user = new TestUser();
        var handler = CreateHandler(db, user,
            new CategoryImportRow(1, "ORPHAN", "Orphan", null, 10, "MISSING", true),
            new CategoryImportRow(2, "SELF", "Self", null, 20, " self ", true));

        var result = await handler.Handle(new ImportProductCategoriesCommand(new byte[] { 1 }), default);

        result.ImportedCount.Should().Be(0);
        result.Errors.Should().HaveCount(2);
        (await new ProductRepository(db, user).GetCategoriesAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public void TenantCategoryAssociationDeclaresTheMasterCategoryForeignKeyForInsertOrdering()
    {
        using var db = CreateDb();
        var mapping = db.Model.GetEntityTypes().Single(x => x.GetTableName() == "TenantProductCategories");
        var categoryId = mapping.FindProperty("ProductCategoryId");

        mapping.FindForeignKeys([categoryId!])
            .Should().ContainSingle(fk => fk.PrincipalEntityType.ClrType == typeof(ProductCategory));
    }

    private static ProductMasterSpreadsheetHandlers CreateHandler(ApplicationDbContext db, TestUser user, params CategoryImportRow[] rows)
        => new(new ProductRepository(db, user), new CategorySpreadsheet(rows), user);

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestUser : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId { get; } = Guid.NewGuid();
        public string? Username => "category-import-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class CategorySpreadsheet(IReadOnlyCollection<CategoryImportRow> categories) : IProductMasterSpreadsheetService
    {
        public IReadOnlyCollection<CategoryImportRow> ReadCategories(byte[] content) => categories;
        public byte[] ExportCategories(IReadOnlyCollection<ProductCategory> rows) => throw new NotSupportedException();
        public byte[] ExportBrands(IReadOnlyCollection<Brand> rows) => throw new NotSupportedException();
        public byte[] ExportUnits(IReadOnlyCollection<UnitOfMeasure> rows) => throw new NotSupportedException();
        public byte[] CategoryTemplate() => throw new NotSupportedException();
        public byte[] BrandTemplate() => throw new NotSupportedException();
        public byte[] UnitTemplate() => throw new NotSupportedException();
        public IReadOnlyCollection<BrandImportRow> ReadBrands(byte[] content) => throw new NotSupportedException();
        public IReadOnlyCollection<UnitImportRow> ReadUnits(byte[] content) => throw new NotSupportedException();
    }
}
