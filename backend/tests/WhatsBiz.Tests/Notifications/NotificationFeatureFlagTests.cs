using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.Notifications;

namespace WhatsBiz.Tests.Notifications;

public sealed class NotificationFeatureFlagTests
{
    [Fact]
    public async Task WhatsAppDisabledDoesNotCallProvider()
    {
        var http = new RecordingHandler();
        var provider = WhatsApp(http, new FeatureOptions());

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.False(result.Succeeded);
        Assert.StartsWith("FEATURE_DISABLED", result.ErrorMessage);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task WhatsAppEnabledCallsProvider()
    {
        var http = new RecordingHandler();
        var provider = WhatsApp(http, new FeatureOptions { WhatsApp = new() { Enabled = true } });

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.True(result.Succeeded);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task SmsDisabledDoesNotCallProvider()
    {
        var http = new RecordingHandler();
        var provider = Sms(http, new FeatureOptions());

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.False(result.Succeeded);
        Assert.StartsWith("FEATURE_DISABLED", result.ErrorMessage);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task SmsEnabledCallsProvider()
    {
        var http = new RecordingHandler();
        var provider = Sms(http, new FeatureOptions { Sms = new() { Enabled = true } });

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.True(result.Succeeded);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task EnabledProviderFailureReturnsFailureInsteadOfThrowing()
    {
        var http = new RecordingHandler(HttpStatusCode.BadGateway);
        var provider = WhatsApp(http, new FeatureOptions { WhatsApp = new() { Enabled = true } });

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.False(result.Succeeded);
        Assert.Contains("HTTP 502", result.ErrorMessage);
        Assert.Equal(1, http.Calls);
    }

    private static WhatsAppProvider WhatsApp(RecordingHandler handler, FeatureOptions features) =>
        new(new ClientFactory(handler), Configuration("WhatsApp"), new Options(features), NullLogger<WhatsAppProvider>.Instance);

    private static SmsProvider Sms(RecordingHandler handler, FeatureOptions features) =>
        new(new ClientFactory(handler), Configuration("Sms"), new Options(features), NullLogger<SmsProvider>.Instance);

    private static IConfiguration Configuration(string channel) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"CustomerNotifications:{channel}:Endpoint"] = "https://provider.example/send",
        [$"CustomerNotifications:{channel}:AccessToken"] = "test-token"
    }).Build();

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"messageId\":\"test-id\"}", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Options(FeatureOptions value) : IOptionsMonitor<FeatureOptions>
    {
        public FeatureOptions CurrentValue => value;
        public FeatureOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FeatureOptions, string?> listener) => null;
    }
}
