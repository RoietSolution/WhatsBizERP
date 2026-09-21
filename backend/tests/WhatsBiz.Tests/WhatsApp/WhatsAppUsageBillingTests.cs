using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Infrastructure.WhatsApp;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppUsageBillingTests
{
    [Fact]
    public void SummaryAggregatesAllCategoriesAndKeepsUnknownVisible()
    {
        var rows = new[]
        {
            new WhatsAppUsageBillingService.SummaryRow("MARKETING",1.10m,"INR"),
            new WhatsAppUsageBillingService.SummaryRow("UTILITY",2.20m,"INR"),
            new WhatsAppUsageBillingService.SummaryRow("AUTHENTICATION",3.30m,"INR"),
            new WhatsAppUsageBillingService.SummaryRow("SERVICE",0m,"INR"),
            new WhatsAppUsageBillingService.SummaryRow("UNKNOWN",null,null)
        };
        var result = WhatsAppUsageBillingService.BuildSummary(new(2026,9,1),rows,"Retail",true);
        result.Categories.Select(x=>x.Category).Should().Equal(WhatsAppMessageCategories.All);
        result.Categories.Take(4).Should().OnlyContain(x=>x.DeliveredMessages==1);
        result.Categories.Single(x=>x.Category=="UNKNOWN").RateConfigured.Should().BeFalse();
        result.EstimatedMetaCharges.Should().BeNull("an unpriced delivered message prevents a complete total");
        result.KhataDhariSubscription.Should().Be(new WhatsAppSubscriptionSummary("Retail",true));
    }

    [Fact]
    public void SummaryDoesNotCombineCurrencies()
    {
        var result = WhatsAppUsageBillingService.BuildSummary(new(2026,9,1),
        [
            new("MARKETING",1m,"INR"),
            new("UTILITY",2m,"USD")
        ],"Retail",true);
        result.EstimatedMetaCharges.Should().BeNull();
        result.Currency.Should().BeNull();
        result.CurrencyBreakdown.Select(x=>x.Currency).Should().BeEquivalentTo(["INR","USD"]);
    }

    [Theory]
    [InlineData("+91 99810 00000","919981000000")]
    [InlineData("not-a-phone",null)]
    [InlineData("123",null)]
    public void RecipientNormalizationIsServerSideAndConservative(string input,string? expected) =>
        WhatsAppUsageBillingService.NormalizeRecipient(input).Should().Be(expected);

    [Fact]
    public void WebhookPricingFieldsArePreservedAndBillableIsOptional()
    {
        var priced = ParseStatus(new { billable=true, pricing_model="PMP", category="utility" });
        priced.PricingCategory.Should().Be("utility");
        priced.MetaBillable.Should().BeTrue();
        priced.PricingModel.Should().Be("PMP");
        var withoutPricing = ParseStatus(null);
        withoutPricing.MetaBillable.Should().BeNull();
        withoutPricing.PricingCategory.Should().BeNull();
    }

    [Fact]
    public void RetailerApiHasNoClientSuppliedTenantIdAndUsesProtectedGet()
    {
        var method=typeof(WhatsAppController).GetMethod(nameof(WhatsAppController.UsageSummary))!;
        method.GetParameters().Select(x=>x.Name).Should().NotContain("tenantId");
        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("usage/summary");
        method.GetCustomAttributes().Select(x=>x.GetType().Name).Should().Contain(["HasPermissionAttribute","RequireFeatureAttribute"]);
    }

    [Fact]
    public void V35IsIdempotentTenantScopedEffectiveDatedAndOverlapProtected()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"database/WhatsBiz.Database/Scripts/V35-WhatsAppUsageBilling.sql"));
        sql.Should().Contain("IF OBJECT_ID(N'integration.MetaWhatsAppPricing'")
            .And.Contain("IF OBJECT_ID(N'integration.WhatsAppMessageUsage'")
            .And.Contain("UQ_WhatsAppMessageUsage_TenantMessage UNIQUE(TenantId,MetaMessageId)")
            .And.Contain("IX_WhatsAppMessageUsage_TenantPeriodCategory")
            .And.Contain("EffectiveFrom").And.Contain("EffectiveTo")
            .And.Contain("TR_MetaWhatsAppPricing_NoOverlap")
            .And.NotContain("INSERT integration.MetaWhatsAppPricing");
        File.ReadAllText(Path.Combine(Root(),"database/WhatsBiz.Database/Scripts/PostDeployment.sql"))
            .Should().Contain(":r .\\V35-WhatsAppUsageBilling.sql");
    }

    [Fact]
    public void CalculationRequiresDeliveryAndEffectiveRateAndSendPathsShareRecorder()
    {
        var service=File.ReadAllText(Path.Combine(Root(),"backend/src/WhatsBiz.Infrastructure/WhatsApp/WhatsAppUsageBillingService.cs"));
        service.Should().Contain("DeliveredAt is not null").And.Contain("EffectiveFrom<=@at")
            .And.Contain("EffectiveTo>@at").And.Contain("EstimatedMetaCost IS NULL")
            .And.Contain("ProviderMode.Equals(WhatsAppProviderModes.Live");
        foreach(var file in new[]{"WhatsAppCommerce/WhatsAppCommerceService.cs","WhatsAppCommerce/WhatsAppInboundCommerceHandler.cs","Delivery/DeliveryService.cs"})
            File.ReadAllText(Path.Combine(Root(),"backend/src/WhatsBiz.Infrastructure",file)).Should().Contain("usageBilling.RecordAcceptedAsync");
    }

    private static WhatsAppService.WebhookTransportEvent ParseStatus(object? pricing)
    {
        object statusValue=pricing is null
            ? new { id="wamid.1", status="delivered", recipient_id="919900000001", timestamp="1700000000" }
            : new { id="wamid.1", status="delivered", recipient_id="919900000001", timestamp="1700000000", pricing };
        var body=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @object="whatsapp_business_account",entry=new[]{new{id="waba",changes=new[]{new{field="messages",value=new{metadata=new{phone_number_id="phone"},statuses=new[]{statusValue}}}}}}}));
        return WhatsAppService.ParseWebhook(body).Single().Events.Single();
    }
    private static string Root([CallerFilePath]string source="") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../../../"));
}
