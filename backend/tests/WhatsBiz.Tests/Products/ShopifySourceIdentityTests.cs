using FluentAssertions;
using WhatsBiz.Api.Controllers;

namespace WhatsBiz.Tests.Products;

public sealed class ShopifySourceIdentityTests
{
    [Theory]
    [InlineData(" GuturGo ", "guturgo")]
    [InlineData("Fashion   Supplier", "fashion supplier")]
    [InlineData(" SUPPLIER@EXAMPLE.COM ", "supplier@example.com")]
    [InlineData("MYSHOP.MYSHOPIFY.COM", "myshop.myshopify.com")]
    [InlineData("https://MyShop.MyShopify.com/", "myshop.myshopify.com")]
    public void AcceptsAndCanonicalizesSupportedSourceIdentifiers(string value, string expected)
    {
        ShopifySourceIdentity.TryNormalize(value, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://user:password@example.com")]
    [InlineData("http://shop.example.com")]
    [InlineData("shop.example.com/products")]
    [InlineData("supplier@example.com\nInjected")]
    public void RejectsEmptyOrUnsafeSourceIdentifiers(string value)
    {
        ShopifySourceIdentity.TryNormalize(value, out _).Should().BeFalse();
    }
}
