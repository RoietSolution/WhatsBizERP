using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifyCategoryBindingTests
{
    private static readonly Guid ParentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChildId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherChildId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("  kitchen   & dining ")]
    [InlineData(" KITCHEN-DINING ")]
    [InlineData("KITCHEN01")]
    public void MatchesActiveCategoryByNameOrCodeWithWhitespaceNormalization(string input)
    {
        var category = new ProductCategory { ProductCategoryId = ChildId, CategoryName = "Kitchen & Dining", CategoryCode = "KITCHEN01", IsActive = true };

        ShopifyCategoryBinding.Match(input, [category]).Should().BeEquivalentTo(
            new ShopifyCategoryMatch("MATCHED", ChildId, ShopifyCategoryBinding.Options([category])));
    }

    [Theory]
    [InlineData("Home & Kitchen > Cookware")]
    [InlineData(" home & kitchen / cookware ")]
    [InlineData("HOME & KITCHEN|COOKWARE")]
    public void MatchesParentChildFullHierarchyWhenUnambiguous(string input)
    {
        var categories = ActiveTree();

        var match = ShopifyCategoryBinding.Match(input, categories);

        match.Status.Should().Be("MATCHED");
        match.ProductCategoryId.Should().Be(ChildId);
    }

    [Fact]
    public void DoesNotAutomaticallyChooseBetweenDuplicateActiveCategoryNames()
    {
        var categories = ActiveTree();

        var match = ShopifyCategoryBinding.Match("Cookware", categories);

        match.Status.Should().Be("AMBIGUOUS");
        match.ProductCategoryId.Should().BeNull();
        match.Candidates.Select(x => x.ProductCategoryId).Should().BeEquivalentTo([ChildId, OtherChildId]);
    }

    [Fact]
    public void LeavesGenuinelyUnmatchedCategoryForManualMapping()
    {
        ShopifyCategoryBinding.Match("Unlisted", ActiveTree()).Status.Should().Be("UNMATCHED");
    }

    private static ProductCategory[] ActiveTree()
    {
        var parent = new ProductCategory { ProductCategoryId = ParentId, CategoryName = "Home & Kitchen", CategoryCode = "HOME", IsActive = true };
        return
        [
            parent,
            new ProductCategory { ProductCategoryId = ChildId, CategoryName = "Cookware", CategoryCode = "COOK-1", ParentCategoryId = ParentId, IsActive = true },
            new ProductCategory { ProductCategoryId = Guid.Parse("44444444-4444-4444-4444-444444444444"), CategoryName = "Home", CategoryCode = "HOME-2", IsActive = true },
            new ProductCategory { ProductCategoryId = OtherChildId, CategoryName = "Cookware", CategoryCode = "COOK-2", ParentCategoryId = Guid.Parse("44444444-4444-4444-4444-444444444444"), IsActive = true }
        ];
    }
}
