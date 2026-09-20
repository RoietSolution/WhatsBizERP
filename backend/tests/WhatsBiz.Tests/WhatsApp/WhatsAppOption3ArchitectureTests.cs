using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppOption3ArchitectureTests
{
    [Fact]
    public async Task TenantAAndTenantBOutboundRequestsUseTheirOwnPhoneNumberIds()
    {
        var handler=new RecordingHandler();var provider=Provider(handler);
        await provider.SendTestMessageAsync(new("v20.0","phone-A","token-A","919900000001","A"),default);
        await provider.SendTestMessageAsync(new("v20.0","phone-B","token-B","919900000002","B"),default);
        handler.Requests.Select(x=>x.Path).Should().Equal("/v20.0/phone-A/messages","/v20.0/phone-B/messages");
        handler.Requests.Select(x=>x.Authorization).Should().Equal("Bearer token-A","Bearer token-B");
    }

    [Fact]
    public async Task DeliveryNotificationsUseTheSuppliedTenantPhoneAndCredential()
    {
        var handler=new RecordingHandler();var provider=Provider(handler);
        var result=await provider.SendTransactionalAsync(new("v22.0","tenant-phone","tenant-token","919900000001","OUT_FOR_DELIVERY","delivery_out","en","Order is out for delivery.",["ORD-1"]),default);
        result.Succeeded.Should().BeTrue();handler.Requests.Should().ContainSingle();handler.Requests[0].Should().Be(("/v22.0/tenant-phone/messages","Bearer tenant-token"));
    }

    [Fact]
    public void LiveAndMetaTestResolveToTheExistingMetaProviderWhileMockRemainsSeparate()
    {
        var mock=new MockWhatsAppProvider();var meta=Provider(new RecordingHandler());var resolver=new WhatsAppCommerceProviderResolver([mock,meta]);
        resolver.Resolve(WhatsAppProviderModes.Mock).Should().BeSameAs(mock);
        resolver.Resolve(WhatsAppProviderModes.MetaTest).Should().BeSameAs(meta);
        resolver.Resolve(WhatsAppProviderModes.Live).Should().BeSameAs(meta);
    }

    [Fact]
    public void MultiRetailerWebhookKeepsEveryChangeBoundToItsOwnTrustedMetaIdentifiers()
    {
        var body=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            tenantId=Guid.NewGuid(),
            @object="whatsapp_business_account",
            entry=new object[]
            {
                new {id="waba-A",changes=new[]{new{value=new{tenantId=Guid.NewGuid(),metadata=new{phone_number_id="phone-A"},contacts=new[]{new{wa_id="1",profile=new{name="Amit Kumar"}}},messages=new[]{new{id="message-A",from="1",type="text",timestamp="1700000000"}}}}}},
                new {id="waba-B",changes=new[]{new{value=new{tenantId=Guid.NewGuid(),metadata=new{phone_number_id="phone-B"},messages=new[]{new{id="message-B",from="2",type="text",timestamp="1700000001"}}}}}}
            }
        }));
        var envelopes=WhatsAppService.ParseWebhook(body);
        envelopes.Should().HaveCount(2);
        envelopes.Select(x=>(x.WabaId,x.PhoneNumberId,x.Events.Single().MetaMessageId)).Should().Equal(("waba-A","phone-A","message-A"),("waba-B","phone-B","message-B"));
        envelopes.First().Events.Single().ProfileName.Should().Be("Amit Kumar");
    }

    [Fact]
    public void PublicWebhookAndTenantSaveContractsCannotSupplyTenantId()
    {
        typeof(SaveWhatsAppConfigurationInput).GetProperties().Select(x=>x.Name).Should().NotContain("TenantId");
        typeof(WhatsAppService).GetMethod(nameof(WhatsAppService.ReceiveWebhookAsync))!.GetParameters().Select(x=>x.Name).Should().NotContain("tenantId");
    }

    [Fact]
    public void ApiResponsesExposeOnlySecretPresenceFlags()
    {
        typeof(WhatsAppConfigurationDto).GetProperties().Select(x=>x.Name).Should().NotContain(["AccessToken","WebhookVerifyToken","AppSecret"]);
        typeof(WhatsAppPlatformConfigurationDto).GetProperties().Select(x=>x.Name).Should().NotContain(["WebhookVerifyToken","AppSecret"]);
        typeof(RetailerWhatsAppConnectionDto).GetProperties().Select(x=>x.Name).Should().NotContain(["AccessToken","WebhookVerifyToken","AppSecret"]);
    }

    [Fact]
    public async Task SubscriptionDiagnosticReadsOnlyAndNeverReturnsAccessToken()
    {
        var handler = new SubscriptionHandler();
        var provider = Provider(handler);
        const string secret = "do-not-return-this-token";
        var result = await provider.GetSubscribedAppsAsync("v25.0", "waba-1234", secret, default);

        result.Succeeded.Should().BeTrue();
        result.ApplicationIds.Should().ContainSingle("1664327351823554");
        result.SubscribedFields.Should().ContainSingle("messages");
        result.MessagesSubscribed.Should().BeTrue();
        handler.Request.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/v25.0/waba-1234/subscribed_apps");
        handler.Request.Headers.Authorization!.Parameter.Should().Be(secret);
        JsonSerializer.Serialize(result).Should().NotContain(secret);
    }

    [Fact]
    public async Task PhoneAssetDiagnosticUsesReadOnlyGraphAndReturnsSafeAssets()
    {
        var handler = new PhoneAssetsHandler();
        var provider = Provider(handler);
        const string secret = "phone-asset-secret";
        var result = await provider.GetPhoneNumbersAsync("v25.0", "waba-1234", secret, default);

        result.Succeeded.Should().BeTrue();
        result.Assets.Should().ContainSingle();
        result.Assets.Single().Id.Should().Be("12345678909885");
        handler.Request.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/v25.0/waba-1234/phone_numbers");
        handler.Request.RequestUri.Query.Should().Contain("fields=");
        handler.Request.Headers.Authorization!.Parameter.Should().Be(secret);
        JsonSerializer.Serialize(result).Should().NotContain(secret);

        var mapped = WhatsAppService.MapPhoneAsset(result.Assets.Single(), "12345678909885", "+91 9981");
        mapped.PhoneNumberId.Should().Be("***9885");
        mapped.DisplayPhoneNumber.Should().Be("***9981");
        mapped.MatchesConfiguredPhoneNumberId.Should().BeTrue();
        mapped.MatchesConfiguredDisplayNumber.Should().BeTrue();
    }

    [Fact]
    public async Task PhoneAssetDiagnosticRetriesWithMinimalFieldsWhenOptionalFieldsAreRejected()
    {
        var handler = new PhoneAssetsFallbackHandler();
        var result = await Provider(handler).GetPhoneNumbersAsync("v25.0", "waba-1234", "secret", default);

        result.Succeeded.Should().BeTrue();
        result.UsedMinimalFields.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Contain("id%2Cdisplay_phone_number");
    }

    [Fact]
    public async Task PhoneAssetDiagnosticReturnsSafeGraphFailure()
    {
        var result = await Provider(new PhoneAssetsFailureHandler()).GetPhoneNumbersAsync("v25.0", "waba-1234", "secret", default);

        result.Succeeded.Should().BeFalse();
        result.SafeError.Should().Contain("Meta Graph error 100");
        JsonSerializer.Serialize(result).Should().NotContain("secret");
    }

    [Fact]
    public async Task ConfiguredPhoneDiagnosticReadsOnlySafeCoexistenceFields()
    {
        var handler = new PhoneDetailsHandler();
        var result = await Provider(handler).GetPhoneNumberDetailsAsync("v25.0", "12345678909885", "secret", default);

        result.Succeeded.Should().BeTrue();
        result.IsOnBizApp.Should().BeTrue();
        result.PlatformType.Should().Be("CLOUD_API");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        var requestUri = handler.Requests[0].RequestUri!;
        requestUri.AbsolutePath.Should().Be("/v25.0/12345678909885");
        requestUri.Query.Should().Contain("is_on_biz_app");
        requestUri.Query.Should().Contain("platform_type");
        requestUri.Query.Should().Contain("quality_rating");
        typeof(WhatsAppConfiguredPhoneDiagnostic).GetProperties().Select(x => x.Name)
            .Should().Equal("PhoneNumberId", "IsOnBizApp", "PlatformType", "SafeError");
    }

    [Fact]
    public async Task ConfiguredPhoneDiagnosticFallsBackToCoreFieldsWhenOptionalFieldsAreRejected()
    {
        var handler = new PhoneDetailsFallbackHandler();
        var result = await Provider(handler).GetPhoneNumberDetailsAsync("v25.0", "12345678909885", "secret", default);

        result.Succeeded.Should().BeTrue();
        result.IsOnBizApp.Should().BeFalse();
        result.PlatformType.Should().Be("CLOUD_API");
        result.SafeError.Should().BeNull();
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(x => x.Method == HttpMethod.Get);
        handler.Requests[1].RequestUri!.Query.Should().Be("?fields=is_on_biz_app%2Cplatform_type");
    }

    [Fact]
    public async Task ConfiguredPhoneDiagnosticSanitizesGraphErrors()
    {
        const string fullPhoneNumberId = "12345678909885";
        const string secret = "phone-detail-secret";
        var result = await Provider(new PhoneDetailsFailureHandler(fullPhoneNumberId, secret))
            .GetPhoneNumberDetailsAsync("v25.0", fullPhoneNumberId, secret, default);

        result.Succeeded.Should().BeFalse();
        result.SafeError.Should().Be("Meta Graph error 100 (HTTP 400).");
        JsonSerializer.Serialize(result).Should().NotContain(fullPhoneNumberId).And.NotContain(secret);
    }

    [Fact]
    public void ContactContractsAreTenantImplicitAndDoNotExposeMessageContent()
    {
        typeof(IWhatsAppService).GetMethod(nameof(IWhatsAppService.GetContactsAsync))!.GetParameters().Select(x=>x.Name).Should().Contain("tenantId");
        typeof(LinkWhatsAppContactInput).GetProperties().Select(x=>x.Name).Should().Equal("CustomerId");
        typeof(WhatsAppContactDto).GetProperties().Select(x=>x.Name).Should().NotContain(["TenantId","MessageText","AccessToken"]);
    }

    [Fact]
    public void WhatsAppContactsMigrationIsTransactionalTenantScopedAndDuplicateSafe()
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!,"../../../../"));
        var sql=File.ReadAllText(Path.Combine(root,"database/WhatsBiz.Database/Scripts/V18-WhatsAppContacts.sql"));
        sql.Should().Contain("BEGIN TRANSACTION").And.Contain("UQ_WhatsAppContacts_TenantMobile").And.Contain("TenantId uniqueidentifier NOT NULL").And.Contain("WhatsAppContactEvents");
    }

    [Fact]
    public void MigrationPreventsDuplicatePhoneAndWabaAssignmentsAndIndexesWebhookResolution()
    {
        var sourceFile = SourceFile();
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!,"../../../../"));
        var sql=File.ReadAllText(Path.Combine(root,"database/WhatsBiz.Database/Scripts/V14-WhatsAppOption3TenantConnections.sql"));
        sql.Should().Contain("UX_WhatsAppConfigurations_PhoneNumberId").And.Contain("UX_WhatsAppConfigurations_WabaId").And.Contain("IX_WhatsAppConfigurations_WebhookResolution");
        sql.Should().Contain("BEGIN TRANSACTION").And.Contain("SET XACT_ABORT ON");
    }

    private static MetaCloudApiWhatsAppProvider Provider(HttpMessageHandler handler)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WhatsApp:Meta:GraphBaseUrl","https://graph.facebook.test"}}).Build();
        return new(new Factory(handler),config,NullLogger<MetaCloudApiWhatsAppProvider>.Instance);
    }
    private static string SourceFile([CallerFilePath] string sourceFile = "") => sourceFile;
    private sealed class Factory(HttpMessageHandler handler):IHttpClientFactory { public HttpClient CreateClient(string name)=>new(handler,false); }
    private sealed class RecordingHandler:HttpMessageHandler
    {
        public List<(string Path,string? Authorization)> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Requests.Add((request.RequestUri!.AbsolutePath,request.Headers.Authorization?.ToString()));return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"messages\":[{\"id\":\"wamid.test\"}]}",Encoding.UTF8,"application/json")});}
    }
    private sealed class SubscriptionHandler : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":[{\"whatsapp_business_api_data\":{\"id\":\"1664327351823554\"},\"subscribed_fields\":[\"messages\"]}]}", Encoding.UTF8, "application/json") });
        }
    }
    private sealed class PhoneAssetsHandler : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"id\":\"12345678909885\",\"display_phone_number\":\"+91 9981\",\"verified_name\":\"GuturGo\",\"quality_rating\":\"GREEN\",\"code_verification_status\":\"VERIFIED\",\"platform_type\":\"CLOUD_API\",\"name_status\":\"APPROVED\"}]}", Encoding.UTF8, "application/json")
            });
        }
    }
    private sealed class PhoneAssetsFallbackHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var rejected = request.RequestUri!.Query.Contains("verified_name", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(rejected ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(rejected ? "{\"error\":{\"code\":100,\"message\":\"Unsupported field\"}}" : "{\"data\":[{\"id\":\"12345678909885\",\"display_phone_number\":\"+91 9981\"}]}", Encoding.UTF8, "application/json")
            });
        }
    }
    private sealed class PhoneAssetsFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("{\"error\":{\"code\":100,\"message\":\"Invalid WABA\"}}", Encoding.UTF8, "application/json") });
    }
    private sealed class PhoneDetailsHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"is_on_biz_app\":true,\"platform_type\":\"CLOUD_API\"}", Encoding.UTF8, "application/json") });
        }
    }
    private sealed class PhoneDetailsFallbackHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var optionalFieldsRequested = request.RequestUri!.Query.Contains("quality_rating", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(optionalFieldsRequested ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(optionalFieldsRequested
                    ? "{\"error\":{\"code\":100,\"message\":\"Unsupported field\"}}"
                    : "{\"is_on_biz_app\":false,\"platform_type\":\"CLOUD_API\"}", Encoding.UTF8, "application/json")
            });
        }
    }
    private sealed class PhoneDetailsFailureHandler(string phoneNumberId, string accessToken) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"{{\"error\":{{\"code\":100,\"message\":\"Invalid object {phoneNumberId}; token {accessToken}\"}}}}", Encoding.UTF8, "application/json")
            });
    }
}
