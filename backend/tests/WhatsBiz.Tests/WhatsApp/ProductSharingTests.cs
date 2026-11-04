using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class ProductSharingTests
{
    [Fact]
    public void DeepLinkIncludesTenantAndStableProductId()
    {
        var id = Guid.NewGuid();
        ProductSharingPolicy.ProductUrl("https://shop.example.test/", "Store_A", id)
            .Should().Be($"https://shop.example.test/store_a/products/{id:D}");
        ProductSharingPolicy.ProductUrl("https://shop.example.test/", "Store_B", id)
            .Should().NotBe(ProductSharingPolicy.ProductUrl("https://shop.example.test/", "Store_A", id));
    }

    [Fact]
    public void ManualMessageUsesOneVersionedMetadataLinkNotTheAngularPage()
    {
        var id = Guid.NewGuid();
        var product = ProductSharingPolicy.ProductUrl("https://shop.example.test/", "tenant", id);
        var version = ProductSharingPolicy.PreviewVersion("Product", "₹49.00", "Fresh", "Store", product, "image-v1");
        var preview = ProductSharingPolicy.PreviewUrl("https://api.example.test/", "tenant", id, version);
        var message = ProductSharingPolicy.Message("Product", "₹49.00", "Fresh https://other.example.test/", "Store", preview);
        message.Should().EndWith(preview).And.NotContain("View Product:").And.NotContain("Store").And.NotContain(product).And.NotContain("other.example");
        System.Text.RegularExpressions.Regex.Matches(message, "https://").Count.Should().Be(1);
        preview.Should().Contain($"/api/store/tenant/products/{id}/share?v=");
    }

    [Fact]
    public void ShareCommentIsPlainBoundedPerMessageTextWithNoExtraUrlOrBranding()
    {
        var message = ProductSharingPolicy.Message("Chaat", "₹49.00", "Fresh", "Retailer Brand", "https://example.test/share?v=1", "  <b>Try today</b>\r\nhttps://untrusted.test/  ");
        message.Should().Be("Chaat\n₹49.00\nFresh\nTry today\nhttps://example.test/share?v=1");
        message.Should().NotContain("Retailer Brand").And.NotContain("<b>");
        var action=()=>ProductSharingPolicy.NormalizeComment(new string('x',301));action.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void FixedTemplateCannotSilentlyDropAComment()
    {
        ProductSharingPolicy.CommentForFormat("Try today", true).Should().Be("Try today");
        ProductSharingPolicy.CommentForFormat(null, false).Should().BeEmpty();
        var action=()=>ProductSharingPolicy.CommentForFormat("Try today",false);action.Should().Throw<BusinessRuleException>().WithMessage("*no comment parameter*");
    }

    [Fact]
    public void ContentVersionsChangeForNewImagesOrMetadataButNotForRepeatedShares()
    {
        var first = ProductSharingPolicy.ImageVersion([1, 2, 3]);
        var replaced = ProductSharingPolicy.ImageVersion([1, 2, 4]);
        first.Should().NotBe(replaced);
        var original = ProductSharingPolicy.PreviewVersion("A", "49", "Fresh", "Store", "https://shop.example.test/a", first);
        original.Should().Be(ProductSharingPolicy.PreviewVersion("A", "49", "Fresh", "Store", "https://shop.example.test/a", first));
        original.Should().NotBe(ProductSharingPolicy.PreviewVersion("A", "50", "Fresh", "Store", "https://shop.example.test/a", first));
        original.Should().NotBe(ProductSharingPolicy.PreviewVersion("A", "49", "Fresh", "Store", "https://shop.example.test/a", replaced));
    }

    [Theory]
    [InlineData("http://shop.example.test")]
    [InlineData("//shop.example.test")]
    [InlineData("https://user:pass@shop.example.test")]
    [InlineData("https://shop.example.test/?next=https://elsewhere.test")]
    [InlineData("https://localhost")]
    [InlineData("javascript:alert(1)")]
    public void OnlyTrustedConfiguredHttpsRootsAreAccepted(string root)
    { var action = () => ProductSharingPolicy.HttpsRoot(root); action.Should().Throw<BusinessRuleException>(); }

    [Theory]
    [InlineData("../private")]
    [InlineData("store?next=evil")]
    [InlineData("")]
    public void StoreKeyCannotInjectDestination(string key)
    { var action = () => ProductSharingPolicy.ProductUrl("https://shop.example.test/", key, Guid.NewGuid()); action.Should().Throw<BusinessRuleException>(); }

    [Fact]
    public void DescriptionIsPlainBoundedTextAndPriceDoesNotRecalculateGst()
    {
        ProductSharingPolicy.PlainDescription("<p>Fresh &amp; tasty</p>").Should().Be("Fresh & tasty");
        ProductSharingPolicy.PlainDescription(new string('x', 400)).Should().HaveLength(240);
        ProductSharingPolicy.PriceLabel(118m, 18m, "INCLUSIVE").Should().Contain("118.00").And.Contain("incl. GST");
        ProductSharingPolicy.PriceLabel(100m, 18m, "EXCLUSIVE").Should().Contain("100.00").And.EndWith("+ GST");
    }

    [Theory]
    [InlineData("call123456789")]
    [InlineData("0123456789")]
    [InlineData("123")]
    [InlineData("1234567890123456")]
    public void InvalidRecipientsAreRejected(string recipient)
    { var action = () => ProductSharingPolicy.Recipient(recipient); action.Should().Throw<BusinessRuleException>(); }

    [Fact]
    public void CustomerServiceWindowUsesAuthoritativeUtcWithExclusiveCutoff()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        ProductSharingPolicy.WithinSession(now.AddHours(-23), now).Should().BeTrue();
        ProductSharingPolicy.WithinSession(now.AddHours(-24), now).Should().BeFalse();
        ProductSharingPolicy.WithinSession(now.AddMinutes(1), now).Should().BeFalse();
        ProductSharingPolicy.WithinSession(null, now).Should().BeFalse();
    }

    [Fact]
    public void DynamicTemplateGetsSuffixNotFullUrlAndCannotRedirect()
    {
        var id = Guid.NewGuid();
        var prefix = "https://shop.example.test/store/products/";
        ProductSharingPolicy.TemplateSuffix(prefix + id, prefix).Should().Be(id.ToString());
        var action = () => ProductSharingPolicy.TemplateSuffix(prefix + id, "https://different.example.test/");
        action.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public async Task MockReturnsSimulatedReferenceWithoutNetwork()
    {
        var request = new WhatsAppSingleProductRequest("", "", "", "919999999999", "Product ₹118\nView Product: https://shop.example.test/a/products/1",
            "https://api.example.test/image", "https://shop.example.test/a/products/1", "", null, "en_US", []);
        var result = await new MockWhatsAppProvider().SendSingleProductAsync(request, default);
        result.Succeeded.Should().BeTrue(); result.ProviderMessageId.Should().StartWith("mock-product-");
        result.SafeMessage.Should().Contain("no WhatsApp message was sent");
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task MissingConsentCustomerOrRequestIdCannotReachDatabaseOrProvider(bool consent, bool customer, bool request)
    {
        var tenant = Guid.NewGuid();
        var service = new WhatsAppCommerceService(new ConfigurationBuilder().Build(), null!, null!, null!, null!, new User(tenant));
        var action = () => service.SendProductShareAsync(tenant, Guid.NewGuid(), new(customer ? Guid.NewGuid() : Guid.Empty,
            request ? Guid.NewGuid() : Guid.Empty, consent), default);
        await action.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AnotherTenantCannotReachSharingDatabaseOrProvider()
    {
        var service = new WhatsAppCommerceService(new ConfigurationBuilder().Build(), null!, null!, null!, null!, new User(Guid.NewGuid()));
        var action = () => service.GetProductShareAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        await action.Should().ThrowAsync<EntityNotFoundException>();
    }

    private sealed class User(Guid tenant) : ICurrentUserService
    { public Guid? UserId=>Guid.NewGuid(); public Guid? TenantId=>tenant;public string? Username=>"fixture";public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[]; }
}
