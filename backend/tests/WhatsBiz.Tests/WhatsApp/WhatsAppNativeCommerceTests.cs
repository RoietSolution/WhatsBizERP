using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppNativeCommerceTests
{
    [Fact]
    public void MenuUsesMetaListWithStableActionIds()
    {
        var message = new WhatsAppCommerceOutboundMessage(WhatsAppCommerceMessageKinds.InteractiveMenu, "Welcome", "Retailer", [
            new(WhatsAppCommerceActionIds.Browse,"Browse Products"), new(WhatsAppCommerceActionIds.Search,"Search Products"),
            new(WhatsAppCommerceActionIds.Cart,"My Cart"), new(WhatsAppCommerceActionIds.Orders,"My Orders")], FallbackText:"MENU fallback");

        var json = Payload(message);

        json.GetProperty("type").GetString().Should().Be("interactive");
        json.GetProperty("interactive").GetProperty("type").GetString().Should().Be("list");
        var rows = json.GetProperty("interactive").GetProperty("action").GetProperty("sections")[0].GetProperty("rows");
        rows.EnumerateArray().Select(x => x.GetProperty("id").GetString()).Should().Equal(
            WhatsAppCommerceActionIds.Browse, WhatsAppCommerceActionIds.Search, WhatsAppCommerceActionIds.Cart, WhatsAppCommerceActionIds.Orders);
    }

    [Fact]
    public void BrowseUsesNativeSingleProductWhenMapped()
    {
        var product = MappedProduct("retailer-1");
        var json = Payload(new(WhatsAppCommerceMessageKinds.Product,"View product",Products:[product],FallbackText:"fallback"));

        json.GetProperty("interactive").GetProperty("type").GetString().Should().Be("product");
        json.GetProperty("interactive").GetProperty("action").GetProperty("catalog_id").GetString().Should().Be("catalog-A");
        json.GetProperty("interactive").GetProperty("action").GetProperty("product_retailer_id").GetString().Should().Be("retailer-1");
    }

    [Fact]
    public void BrowseUsesNativeProductListForMappedProductsFromOneCatalog()
    {
        var json = Payload(new(WhatsAppCommerceMessageKinds.ProductList,"Select",Header:"Products",Products:[MappedProduct("one"),MappedProduct("two")],FallbackText:"fallback"));

        json.GetProperty("interactive").GetProperty("type").GetString().Should().Be("product_list");
        json.GetProperty("interactive").GetProperty("action").GetProperty("sections")[0].GetProperty("product_items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void MissingOrMixedCatalogMappingFallsBackToText()
    {
        var missing = MappedProduct("one") with { CatalogId = null };
        var mixed = MappedProduct("two") with { CatalogId = "catalog-B" };

        Payload(new(WhatsAppCommerceMessageKinds.Product,"Select",Products:[missing],FallbackText:"safe fallback"))
            .GetProperty("type").GetString().Should().Be("text");
        Payload(new(WhatsAppCommerceMessageKinds.ProductList,"Select",Products:[MappedProduct("one"),mixed],FallbackText:"safe fallback"))
            .GetProperty("type").GetString().Should().Be("text");
    }

    [Fact]
    public void CartUsesSafeContinueAndCheckoutButtons()
    {
        var json=Payload(new(WhatsAppCommerceMessageKinds.InteractiveButtons,"Cart total ₹100",Actions:[
            new(WhatsAppCommerceActionIds.ContinueShopping,"Continue Shopping"),new(WhatsAppCommerceActionIds.Checkout,"Checkout")],FallbackText:"CART fallback"));

        json.GetProperty("interactive").GetProperty("type").GetString().Should().Be("button");
        json.GetProperty("interactive").GetProperty("action").GetProperty("buttons").EnumerateArray()
            .Select(x=>x.GetProperty("reply").GetProperty("id").GetString()).Should().Equal(
                WhatsAppCommerceActionIds.ContinueShopping,WhatsAppCommerceActionIds.Checkout);
    }

    [Theory]
    [InlineData(WhatsAppCommerceActionIds.Browse,"browse")]
    [InlineData(WhatsAppCommerceActionIds.Search,"search")]
    [InlineData(WhatsAppCommerceActionIds.Cart,"cart")]
    [InlineData(WhatsAppCommerceActionIds.Orders,"order status")]
    [InlineData(WhatsAppCommerceActionIds.Checkout,"confirm")]
    public void InteractiveActionsRouteToExistingCommands(string action,string command) =>
        WhatsAppInboundCommerceHandler.ResolveCommand(new(null,action,null,[])).Should().Be(command);

    [Fact]
    public void ExistingTextCommandsRemainUnchanged() =>
        WhatsAppInboundCommerceHandler.ResolveCommand(new("SEARCH saree",null,null,[])).Should().Be("SEARCH saree");

    [Fact]
    public void ParserAcceptsSupportedInteractiveReply()
    {
        var item = WhatsAppService.ParseWebhook(Webhook(new { interactive = new { type="list_reply", list_reply=new { id=WhatsAppCommerceActionIds.Cart,title="My Cart" } } })).Single().Events.Single();

        item.InteractiveActionId.Should().Be(WhatsAppCommerceActionIds.Cart);
        item.Products.Should().BeEmpty();
    }

    [Fact]
    public void ParserAcceptsNativeOrderItemsForTenantValidationInHandler()
    {
        var item = WhatsAppService.ParseWebhook(Webhook(new { order=new { catalog_id="catalog-A",product_items=new[] { new { product_retailer_id="sku-A",quantity=2 } } } })).Single().Events.Single();

        item.CatalogId.Should().Be("catalog-A");
        item.Products.Should().ContainSingle().Which.Should().Be(new WhatsAppCommerceInboundProduct("sku-A",2));
    }

    [Fact]
    public void ParserRejectsUnknownActionsAndMalformedNativeItemsSafely()
    {
        var action = WhatsAppService.ParseWebhook(Webhook(new { interactive=new { type="button_reply",button_reply=new { id="tenant-supplied-action" } } })).Single().Events.Single();
        var nonStringAction = WhatsAppService.ParseWebhook(Webhook(new { interactive=new { type="button_reply",button_reply=new { id=42 } } })).Single().Events.Single();
        var order = WhatsAppService.ParseWebhook(Webhook(new { order=new { catalog_id="catalog-A",product_items=new[] { new { product_retailer_id="sku-A",quantity=0 } } } })).Single().Events.Single();

        action.InteractiveActionId.Should().BeNull();
        nonStringAction.InteractiveActionId.Should().BeNull();
        order.CatalogId.Should().BeNull();
        order.Products.Should().BeEmpty();
    }

    [Fact]
    public void HandlerKeepsTenantMappingCartPosPaymentAndIdempotencyBoundaries()
    {
        var source = File.ReadAllText(Path.Combine(Root(),"backend/src/WhatsBiz.Infrastructure/WhatsAppCommerce/WhatsAppInboundCommerceHandler.cs"));
        source.Should().Contain("m.TenantId=@tenant").And.Contain("m.CatalogId=@catalog").And.Contain("m.ExternalProductId=@external");
        source.Should().Contain("integration.WhatsAppCommerceCartLines").And.Contain("pos.PostForTenant").And.Contain("GetEnabledMethodsForTenantAsync");
        source.Should().Contain("INSERT INTO integration.WhatsAppCommerceInbound").And.Contain("e.Number is 2601 or 2627");
        source.Should().Contain("phoneId.Equals(webhookPhone,StringComparison.Ordinal)");
    }

    [Fact]
    public void LiveCollectionEligibilityIsExplicitAndTenantMappingIsUsed()
    {
        var source = File.ReadAllText(Path.Combine(Root(),"backend/src/WhatsBiz.Infrastructure/WhatsAppCommerce/WhatsAppCommerceService.cs"));
        source.Should().Contain("config.Mode.Equals(\"LIVE\"").And.Contain("ProductChannelMappings m ON m.TenantId=c.TenantId");
        source.Should().Contain("m.Provider='META'").And.Contain("m.SyncStatus='MAPPED'");
    }

    [Fact]
    public void SearchStateAndCheckoutReuseExistingPersistentFlow()
    {
        var source=File.ReadAllText(Path.Combine(Root(),"backend/src/WhatsBiz.Infrastructure/WhatsAppCommerce/WhatsAppInboundCommerceHandler.cs"));
        source.Should().Contain("SetState(tenantId, conversation.ConversationId, \"SEARCH\"").And.Contain("conversation.State.Equals(\"SEARCH\"");
        source.Should().Contain("return await ProductList(tenantId, conversation.WarehouseId, inbound.Text.Trim()");
        source.Should().Contain("WhatsAppCommerceActionIds.Checkout => \"confirm\"").And.Contain("return await Confirm(tenantId, conversation, messageId, token)");
    }

    private static WhatsAppCommerceProductMessage MappedProduct(string external) => new(Guid.NewGuid(),external,external,100,null,"catalog-A",external);
    private static JsonElement Payload(WhatsAppCommerceOutboundMessage message) => JsonSerializer.SerializeToElement(
        MetaCloudApiWhatsAppProvider.BuildCommercePayload(new("v25.0","phone","secret","919900000001",message)));
    private static byte[] Webhook(object content) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        @object="whatsapp_business_account",entry=new[] { new { id="waba-A",changes=new[] { new { value=new
        {
            metadata=new { phone_number_id="phone-A" },messages=new[] { Merge(content) }
        } } } } }
    }));
    private static Dictionary<string,object?> Merge(object content)
    {
        var values=JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(content))!;
        values["id"]="message-A";values["from"]="919900000001";values["type"]=values.ContainsKey("order")?"order":"interactive";values["timestamp"]="1700000000";
        return values;
    }
    private static string Root([CallerFilePath] string file="") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../../../"));
}
