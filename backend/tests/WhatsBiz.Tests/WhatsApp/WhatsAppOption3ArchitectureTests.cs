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
                new {id="waba-A",changes=new[]{new{value=new{tenantId=Guid.NewGuid(),metadata=new{phone_number_id="phone-A"},messages=new[]{new{id="message-A",from="1",type="text",timestamp="1700000000"}}}}}},
                new {id="waba-B",changes=new[]{new{value=new{tenantId=Guid.NewGuid(),metadata=new{phone_number_id="phone-B"},messages=new[]{new{id="message-B",from="2",type="text",timestamp="1700000001"}}}}}}
            }
        }));
        var envelopes=WhatsAppService.ParseWebhook(body);
        envelopes.Should().HaveCount(2);
        envelopes.Select(x=>(x.WabaId,x.PhoneNumberId,x.Events.Single().MetaMessageId)).Should().Equal(("waba-A","phone-A","message-A"),("waba-B","phone-B","message-B"));
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
    public void MigrationPreventsDuplicatePhoneAndWabaAssignmentsAndIndexesWebhookResolution()
    {
        var sourceFile = SourceFile();
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!,"../../../../"));
        var sql=File.ReadAllText(Path.Combine(root,"database/WhatsBiz.Database/Scripts/V14-WhatsAppOption3TenantConnections.sql"));
        sql.Should().Contain("UX_WhatsAppConfigurations_PhoneNumberId").And.Contain("UX_WhatsAppConfigurations_WabaId").And.Contain("IX_WhatsAppConfigurations_WebhookResolution");
        sql.Should().Contain("BEGIN TRANSACTION").And.Contain("SET XACT_ABORT ON");
    }

    private static MetaCloudApiWhatsAppProvider Provider(RecordingHandler handler)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WhatsApp:Meta:GraphBaseUrl","https://graph.facebook.test"}}).Build();
        return new(new Factory(handler),config,NullLogger<MetaCloudApiWhatsAppProvider>.Instance);
    }
    private static string SourceFile([CallerFilePath] string sourceFile = "") => sourceFile;
    private sealed class Factory(RecordingHandler handler):IHttpClientFactory { public HttpClient CreateClient(string name)=>new(handler,false); }
    private sealed class RecordingHandler:HttpMessageHandler
    {
        public List<(string Path,string? Authorization)> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Requests.Add((request.RequestUri!.AbsolutePath,request.Headers.Authorization?.ToString()));return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"messages\":[{\"id\":\"wamid.test\"}]}",Encoding.UTF8,"application/json")});}
    }
}
