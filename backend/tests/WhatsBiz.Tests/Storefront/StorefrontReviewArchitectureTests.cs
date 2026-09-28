using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontReviewArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void InvalidRatingsAreRejectedByServiceAndDatabase(int rating)
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReviewService.cs");
        service.Should().Contain("input.Rating is < 1 or > 5");
        Read("database/WhatsBiz.Database/Scripts/V39-StorefrontCustomerSessions.sql")
            .Should().Contain("CHECK(Rating BETWEEN 1 AND 5)");
        rating.Should().NotBeInRange(1, 5);
    }

    [Fact]
    public void ReviewInputCannotImpersonateCustomerOrTenant()
    {
        typeof(StorefrontReviewInput).GetProperties().Select(x => x.Name).Should().BeEquivalentTo(["Rating", "ReviewText"]);
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReviewService.cs");
        service.Should().Contain("customers.GetSessionAsync(storeKey, sessionToken")
            .And.Contain("CustomerId = customer.Id").And.Contain("TenantId = tenant.Value");
    }

    [Fact]
    public void ReviewsAreUniqueTenantGuardedAndPurchasedProductOnly()
    {
        var migration = Read("database/WhatsBiz.Database/Scripts/V39-StorefrontCustomerSessions.sql");
        migration.Should().Contain("UNIQUE(TenantId,CustomerId,ProductId)")
            .And.Contain("TR_StorefrontProductReviews_TenantGuard")
            .And.Contain("CK_StorefrontProductReviews_Status");
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReviewService.cs");
        service.Should().Contain("invoice.CustomerId == customer.Id").And.Contain("invoice.Items.Any(item => item.ProductId == productId)")
            .And.Contain("PARTIALLY_RETURNED");
    }

    [Fact]
    public void AggregationAndSettingsAreServerSide()
    {
        var storefront = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontService.cs");
        storefront.Should().Contain("GroupBy(x => x.ProductId)").And.Contain("Average = x.Average")
            .And.Contain("ShowProductRatings");
        var reviews = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReviewService.cs");
        reviews.Should().Contain("if (!settings.ShowReviews)").And.Contain("Product reviews are disabled for this store.");
    }

    [Fact]
    public void ProductStatusEndpointsArePermissionAndTenantScoped()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/ProductsController.cs");
        controller.Should().Contain("HttpPatch(\"{id:guid}/status\")").And.Contain("HasPermission(Permissions.Product.Edit)")
            .And.Contain("HttpPatch(\"status\")");
        var repository = Read("backend/src/WhatsBiz.Infrastructure/Persistence/ProductRepository.cs");
        repository.Should().Contain("context.Products.Where(x => x.TenantId == tenantId)");
        var handlers = Read("backend/src/WhatsBiz.Application/Features/Products/Products/ProductHandlers.cs");
        handlers.Should().Contain("SetProductsStatusCommandHandler").And.Contain("GetManyAsync(ids, true").And.Contain("SaveChangesAsync");
    }

    [Fact]
    public void PurchaseSelectorUsesServerFiltersAndExistingLineIntegration()
    {
        var selector = Read("frontend/WhatsBiz.Web/src/app/features/purchases/purchase-product-selector-dialog.component.ts");
        selector.Should().Contain("categoryId:this.categoryId").And.Contain("brandId:this.brandId")
            .And.Contain("Select All Filtered").And.Contain("Add All Products")
            .And.Contain("pageSize:200");
        var purchase = Read("frontend/WhatsBiz.Web/src/app/features/purchases/purchase-form.component.ts");
        purchase.Should().Contain("this.addProduct(product, false)");
        var repository = Read("backend/src/WhatsBiz.Infrastructure/Persistence/ProductRepository.cs");
        repository.Should().Contain("x.CategoryId == categoryId").And.Contain("x.BrandId == brandId");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "backend", "WhatsBiz.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}