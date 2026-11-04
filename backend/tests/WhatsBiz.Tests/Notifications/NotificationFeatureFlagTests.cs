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
    public async Task TwoFactorTransactionalSmsUsesConfiguredSenderAndReportsAcceptedResponse()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, "{\"Status\":\"Success\",\"Details\":\"session-123\"}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerNotifications:Sms:Provider"] = "2Factor",
            ["CustomerNotifications:Sms:Endpoint"] = "https://api.2factor.in/v3/sms/send",
            ["CustomerNotifications:Sms:ApiKey"] = "test-key",
            ["CustomerNotifications:Sms:SenderId"] = "TESTID",
            ["CustomerNotifications:Sms:MessageType"] = "transactional",
            ["CustomerNotifications:Sms:ApiKeyHeader"] = "Authorization",
            ["CustomerNotifications:Sms:ApiKeyScheme"] = "Bearer"
        }).Build();
        var provider = new SmsProvider(new ClientFactory(http), configuration, new Options(new FeatureOptions { Sms = new() { Enabled = true } }), NullLogger<SmsProvider>.Instance);

        var result = await provider.Send("+919999999999", "Order confirmation", Guid.NewGuid(), default);

        Assert.True(result.Succeeded);
        Assert.Equal("session-123", result.ProviderMessageId);
        Assert.Equal("https://api.2factor.in/v3/sms/send", http.LastUri?.ToString());
        Assert.Equal("Bearer test-key", http.LastAuthorization);
        using var payload = System.Text.Json.JsonDocument.Parse(http.LastBody);
        Assert.Equal("TESTID", payload.RootElement.GetProperty("sender_id").GetString());
        Assert.Equal("+919999999999", payload.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("Order confirmation", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal("transactional", payload.RootElement.GetProperty("message_type").GetString());
    }

    [Fact]
    public async Task TwoFactorTransactionalSmsTreatsProviderRejectionAsFailure()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, "{\"Status\":\"Error\",\"Details\":\"rejected\"}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerNotifications:Sms:Provider"] = "2Factor",
            ["CustomerNotifications:Sms:Endpoint"] = "https://api.2factor.in/v3/sms/send",
            ["CustomerNotifications:Sms:ApiKey"] = "test-key",
            ["CustomerNotifications:Sms:SenderId"] = "TESTID",
            ["CustomerNotifications:Sms:MessageType"] = "transactional"
        }).Build();
        var provider = new SmsProvider(new ClientFactory(http), configuration, new Options(new FeatureOptions { Sms = new() { Enabled = true } }), NullLogger<SmsProvider>.Instance);

        var result = await provider.Send("+919999999999", "message", Guid.NewGuid(), default);

        Assert.False(result.Succeeded);
        Assert.Equal("2Factor rejected the SMS request.", result.ErrorMessage);
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

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{\"messageId\":\"test-id\"}") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string LastBody { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json") });
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
