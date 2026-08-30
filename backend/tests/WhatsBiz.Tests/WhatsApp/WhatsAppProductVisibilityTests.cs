using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppProductVisibilityTests
{
    [Fact]
    public void ProductVisibilityDefaultsToEnabledAndCanBeDisabledFromProductMaster()
    {
        new Product().IsWhatsAppVisible.Should().BeTrue();
        var product = new Product();

        typeof(CreateProductCommandHandler)
            .GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [product, Input(isWhatsAppVisible: false)]);

        product.IsWhatsAppVisible.Should().BeFalse();
        product.IsActive.Should().BeTrue("WhatsApp visibility must not deactivate the ERP product");
    }

    [Fact]
    public async Task WhatsAppHiddenProductRemainsAvailableToTenantScopedPosBarcodeLookup()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var product = new Product
        {
            TenantId = tenantId,
            ProductCode = "ERP-ONLY-1",
            Barcode = "8900000000001",
            ProductName = "ERP only product",
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            UnitId = Guid.NewGuid(),
            IsWhatsAppVisible = false,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var result = await new POSRepository(db, new CurrentUser(tenantId))
            .Products(null, product.Barcode, null, 1, default);

        result.Should().ContainSingle(x => x.Product.ProductId == product.ProductId);
    }

    [Fact]
    public void MigrationIsIdempotentAndPreservesExistingProductVisibility()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "WhatsBiz.Database", "Scripts", "V23-WhatsAppProductVisibility.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var sql = File.ReadAllText(Path.Combine(root!.FullName, "database", "WhatsBiz.Database", "Scripts", "V23-WhatsAppProductVisibility.sql"));

        sql.Should().Contain("QUOTED_IDENTIFIER ON")
            .And.Contain("COL_LENGTH(N'master.Products', N'IsWhatsAppVisible') IS NULL")
            .And.Contain("DEFAULT (1) WITH VALUES")
            .And.Contain("WHERE IsWhatsAppVisible IS NULL")
            .And.NotContain("DELETE FROM master.Products");
    }

    private static ProductInput Input(bool isWhatsAppVisible) => new(
        "ERP-ONLY-1", "8900000000001", "ERP only product", null, null,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, 18, 10, 12, 15,
        0, 100, 5, null, null, null, null, false, false, true,
        IsWhatsAppVisible: isWhatsAppVisible);

    private sealed class CurrentUser(Guid tenantId) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Username => "pos-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
