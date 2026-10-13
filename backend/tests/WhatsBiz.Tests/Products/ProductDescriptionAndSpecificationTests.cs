using FluentAssertions;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductDescriptionAndSpecificationTests
{
    [Fact]
    public void SanitizerKeepsApprovedFormattingAndRemovesActiveContent()
    {
        var result = ProductDescriptionSanitizer.Sanitize("<h2>Fresh</h2><p><strong>Good</strong> <a href='javascript:alert(1)' onclick='bad()'>value</a></p><script>alert(1)</script>", 1000);

        result.Should().Contain("<h2>Fresh</h2>").And.Contain("<strong>Good</strong>");
        result.Should().NotContain("script").And.NotContain("onclick").And.NotContain("javascript:");
    }

    [Fact]
    public void LegacyPlainTextDescriptionRemainsVisibleAsText()
    {
        var result = ProductDescriptionSanitizer.Sanitize("Legacy product description", 1000);

        result.Should().Be("Legacy product description");
    }

    [Fact]
    public void SpecificationsSynchronizeInRequestedOrderAndTenant()
    {
        var tenant = Guid.NewGuid();
        var product = new Product { TenantId = tenant };
        var input = new WhatsBiz.Application.Features.Products.DTOs.ProductInput(
            "P", null, "Product", null, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            0, 1, 2, 2, 0, 1, 0, null, null, null, null, false, false, true,
            Specifications: [new("Material", "Cotton"), new("Size", "M")]);

        CreateProductCommandHandler.SyncSpecifications(product, input, "tester");

        product.Specifications.Should().HaveCount(2);
        product.Specifications.Select(x => x.Name).Should().Equal("Material", "Size");
        product.Specifications.Should().OnlyContain(x => x.TenantId == tenant);
    }

    [Fact]
    public void CreateAndUpdateMappingPreservesFormattedDescriptionsAndSpecifications()
    {
        var tenant = Guid.NewGuid();
        var product = new Product { TenantId = tenant };
        var create = new WhatsBiz.Application.Features.Products.DTOs.ProductInput(
            "P-1", null, "Formatted", "<p><strong>Short</strong></p>", "<h2>Details</h2><ul><li>One</li></ul>",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, 0, 1, 2, 2, 0, 1, 0, null, null, null, null, false, false, true,
            Specifications: [new("Material", "Cotton"), new("Size", "M")]);

        CreateProductCommandHandler.Apply(product, create);
        CreateProductCommandHandler.SyncSpecifications(product, create, "tester");

        product.ShortDescription.Should().Contain("<strong>Short</strong>");
        product.LongDescription.Should().Contain("<h2>Details</h2>");
        product.Specifications.Should().HaveCount(2);

        var update = create with { ShortDescription = "<p>Updated</p>", Specifications = [new("Colour", "Blue")] };
        CreateProductCommandHandler.Apply(product, update);
        CreateProductCommandHandler.SyncSpecifications(product, update, "tester");

        product.ShortDescription.Should().Be("<p>Updated</p>");
        product.Specifications.Should().ContainSingle(x => x.Name == "Colour" && !x.IsDeleted);
        product.Specifications.Where(x => x.Name == "Material" || x.Name == "Size").Should().OnlyContain(x => x.IsDeleted);
    }

    [Fact]
    public void UpdateSynchronizationExplicitlyReportsNewSpecificationsForAddedTracking()
    {
        var tenant = Guid.NewGuid();
        var product = new Product { TenantId = tenant };
        var existing = new ProductSpecification { TenantId = tenant, ProductId = product.ProductId, Name = "Material", Value = "Cotton" };
        product.Specifications.Add(existing);
        var input = new WhatsBiz.Application.Features.Products.DTOs.ProductInput(
            "P", null, "Product", null, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            0, 1, 2, 2, 0, 1, 0, null, null, null, null, false, false, true,
            Specifications: [new("Material", "Linen"), new("Size", "M")]);
        var added = new List<ProductSpecification>();

        CreateProductCommandHandler.SyncSpecifications(product, input, "tester", added.Add);

        added.Should().ContainSingle(x => x.Name == "Size" && x.ProductId == product.ProductId && x.TenantId == tenant);
        existing.Value.Should().Be("Linen");
        existing.IsDeleted.Should().BeFalse();
    }
}
