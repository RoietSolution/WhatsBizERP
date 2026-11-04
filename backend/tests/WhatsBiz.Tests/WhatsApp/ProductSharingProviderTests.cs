using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class ProductSharingProviderTests
{
    private static WhatsAppSingleProductRequest Request(bool template) => new("v23.0", "phone", "fixture-token", "919999999999",
        "Product\n₹118\nView Product: https://shop.example.test/a/products/123", "https://api.example.test/image.jpg?v=image-version",
        "https://shop.example.test/a/products/123", "123", template ? "single_product" : null, "en_US",
        ["Product", "₹118", "Description", "Store"], "waba", "https://shop.example.test/a/products/");

    [Fact]
    public async Task SessionSendIsAnImageCaptionWithDeepLinkNotANativeCatalogProduct()
    {
        var provider = Provider([Accepted()], out var handler);
        var result = await provider.SendSingleProductAsync(Request(false), default);
        result.Succeeded.Should().BeTrue(); handler.Bodies.Should().ContainSingle();
        result.SafeMessage.Should().Contain("no URL button was sent");
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        body.RootElement.GetProperty("type").GetString().Should().Be("image");
        body.RootElement.GetProperty("image").GetProperty("caption").GetString().Should().Contain(Request(false).ProductUrl);
        body.RootElement.TryGetProperty("interactive", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ApprovedTemplateUsesImageHeaderFourParametersAndDynamicButtonSuffix()
    {
        var provider = Provider([Template("APPROVED"), Accepted()], out var handler);
        var result = await provider.SendSingleProductAsync(Request(true), default);
        result.Succeeded.Should().BeTrue(); handler.Methods.Should().Equal("GET", "POST");
        using var json = JsonDocument.Parse(handler.Bodies.Single());
        var parts = json.RootElement.GetProperty("template").GetProperty("components");
        parts[0].GetProperty("parameters")[0].GetProperty("image").GetProperty("link").GetString().Should().Be(Request(true).ImageUrl);
        parts[1].GetProperty("parameters").GetArrayLength().Should().Be(4);
        parts[2].GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be("123");
    }

    [Theory]
    [InlineData("PENDING", "IMAGE", "https://shop.example.test/a/products/{{1}}")]
    [InlineData("REJECTED", "IMAGE", "https://shop.example.test/a/products/{{1}}")]
    [InlineData("APPROVED", "TEXT", "https://shop.example.test/a/products/{{1}}")]
    [InlineData("APPROVED", "IMAGE", "https://another.example.test/{{1}}")]
    public async Task UnapprovedOrIncompatibleTemplatesNeverSend(string status, string format, string url)
    {
        var provider = Provider([Template(status, format, url)], out var handler);
        (await provider.SendSingleProductAsync(Request(true), default)).Succeeded.Should().BeFalse();
        handler.Methods.Should().Equal("GET");
    }

    [Fact]
    public async Task ProviderRejectionIsSafeAndNeverAutomaticallyRetried()
    {
        var provider = Provider([new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("private diagnostic") }], out var handler);
        var result = await provider.SendSingleProductAsync(Request(false), default);
        result.Succeeded.Should().BeFalse(); result.SafeMessage.Should().Contain("HTTP 400").And.NotContain("private diagnostic").And.NotContain("fixture-token");
        handler.Methods.Should().ContainSingle();
    }

    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK) { Content = new StringContent("""{"messages":[{"id":"wamid.fixture"}]}""") };
    private static HttpResponseMessage Template(string status, string format = "IMAGE", string url = "https://shop.example.test/a/products/{{1}}") =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { name = "single_product", status, category = "MARKETING", language = "en_US",
            components = new object[] { new { type = "HEADER", format }, new { type = "BODY", text = "{{1}} {{2}} {{3}} {{4}}" }, new { type = "BUTTONS", buttons = new[] { new { type = "URL", url } } } } } } })) };
    private static MetaCloudApiWhatsAppProvider Provider(HttpResponseMessage[] responses, out Handler handler)
    {
        handler = new Handler(responses);
        return new(new Factory(new HttpClient(handler)), new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["WhatsApp:Meta:GraphBaseUrl"] = "https://graph.example.test" }).Build(), NullLogger<MetaCloudApiWhatsAppProvider>.Instance);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<string> Bodies { get; } = []; public List<string> Methods { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Methods.Add(request.Method.Method);
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(token));
            return queue.Dequeue();
        }
    }
}
