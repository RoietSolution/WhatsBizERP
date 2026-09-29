using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Infrastructure.Payments;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Payments;

public sealed class PaymentArchitectureTests
{
    [Theory]
    [InlineData("shop@upi",true)]
    [InlineData("guturgo.01@okaxis",true)]
    [InlineData("missing-at",false)]
    [InlineData("two@@upi",false)]
    [InlineData("bad value@upi",false)]
    public void DirectUpiVpaValidationIsDeterministic(string value,bool expected) => PaymentValidation.IsValidVpa(value).Should().Be(expected);

    [Fact]
    public async Task DirectUpiUsesServerAmountAndRemainsPendingVerification()
    {
        var gateway=new DirectUpiPaymentGateway();
        var result=await gateway.CreatePaymentAsync(new(PaymentProviders.DirectUpi,null,null,null,false,"retailer@upi","Retailer Store"),
            new(Guid.NewGuid(),Guid.NewGuid(),"KD-1042",280.50m,"INR","KD-UNIQUE-1",null,null),default);
        result.PaymentAction.Should().StartWith("upi://pay?").And.Contain("pa=retailer%40upi").And.Contain("am=280.50").And.Contain("tr=KD-UNIQUE-1");
        (await gateway.GetPaymentStatusAsync(new(PaymentProviders.DirectUpi,null,null,null,false,"retailer@upi","Retailer Store"),"KD-UNIQUE-1",default)).Status.Should().Be(CommercePaymentStatuses.PendingVerification);
    }

    [Fact]
    public async Task CodNeverReportsPaidWhenAttemptIsCreated()
    {
        var gateway=new CashOnDeliveryPaymentGateway();
        var result=await gateway.CreatePaymentAsync(new(PaymentProviders.Cod,null,null,null,false,null,null),new(Guid.NewGuid(),Guid.NewGuid(),"KD-1",100,"INR","COD-1",null,null),default);
        result.PaymentAction.Should().Be("Cash on Delivery");
        (await gateway.GetPaymentStatusAsync(new(PaymentProviders.Cod,null,null,null,false,null,null),"COD-1",default)).Status.Should().Be(CommercePaymentStatuses.CodPending);
    }

    [Fact]
    public async Task RazorpayPaymentLinkUsesServerAmountInMinorUnitsAndRetailerCredentials()
    {
        var handler=new CaptureHandler("{\"id\":\"plink_test\",\"short_url\":\"https://rzp.io/i/test\"}");
        var gateway=new RazorpayPaymentGateway(new ClientFactory(handler));
        var result=await gateway.CreatePaymentAsync(new(PaymentProviders.Razorpay,"rzp_test_key","secret", "webhook",true,null,null),new(Guid.NewGuid(),Guid.NewGuid(),"KD-1042",280m,"INR","KD-REF", "Customer", "+919999999999"),default);
        using var body=JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("amount").GetInt64().Should().Be(28000);
        body.RootElement.GetProperty("reference_id").GetString().Should().Be("KD-REF");
        handler.Authorization.Should().Be("Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes("rzp_test_key:secret")));
        result.PaymentLink.Should().Be("https://rzp.io/i/test");
    }

    [Theory]
    [InlineData("UPI", true, false)]
    [InlineData("NET_BANKING", false, true)]
    public async Task RazorpayPaymentLinkRestrictsCheckoutToSelectedMethod(string method, bool upi, bool netBanking)
    {
        var handler=new CaptureHandler("{\"id\":\"plink_test\",\"order_id\":\"order_test\",\"short_url\":\"https://rzp.io/i/test\"}");
        var gateway=new RazorpayPaymentGateway(new ClientFactory(handler));
        var result=await gateway.CreatePaymentAsync(new(PaymentProviders.Razorpay,"key","secret","webhook",true,null,null),
            new(Guid.NewGuid(),Guid.NewGuid(),"KD-1",100m,"INR","KD-REF",null,null,method),default);
        using var body=JsonDocument.Parse(handler.Body!);
        var methods=body.RootElement.GetProperty("options").GetProperty("checkout").GetProperty("method");
        methods.GetProperty("upi").GetBoolean().Should().Be(upi);
        methods.GetProperty("netbanking").GetBoolean().Should().Be(netBanking);
        result.ProviderOrderId.Should().Be("order_test");
        result.ProviderReference.Should().Be("plink_test");
    }
    [Fact]
    public async Task RazorpayRefundUsesCommittedAmountAndDurableCorrelationNote()
    {
        var refund=Guid.NewGuid(); var attempt=Guid.NewGuid();
        var handler=new CaptureHandler("{\"id\":\"rfnd_test\",\"payment_id\":\"pay_test\",\"status\":\"created\",\"amount\":99000,\"currency\":\"INR\"}");
        var gateway=new RazorpayPaymentGateway(new ClientFactory(handler));
        var result=await gateway.CreateRefundAsync(
            new(PaymentProviders.Razorpay,"synthetic-key","synthetic-secret",null,true,null,null),
            "pay_test",990m,"INR",refund,attempt,default);
        handler.Method.Should().Be(HttpMethod.Post);
        handler.Path.Should().EndWith("/payments/pay_test/refund");
        using var body=JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("amount").GetInt64().Should().Be(99000);
        body.RootElement.GetProperty("notes").GetProperty("refund_id").GetString()
            .Should().Be(refund.ToString("N"));
        body.RootElement.GetProperty("notes").GetProperty("attempt_id").GetString()
            .Should().Be(attempt.ToString("N"));
        result.Status.Should().Be("CREATED");
        result.ProviderRefundId.Should().Be("rfnd_test");
    }

    [Fact]
    public async Task RazorpayRefundReconciliationMatchesOnlyOurRefundMarker()
    {
        var refund=Guid.NewGuid(); var attempt=Guid.NewGuid();
        var body=JsonSerializer.Serialize(new { items=new[]
        {
            new { id="rfnd_other",payment_id="pay_test",status="processed",amount=99000,
                currency="INR",notes=new { refund_id=refund.ToString("N"),attempt_id=Guid.NewGuid().ToString("N") } },
            new { id="rfnd_ours",payment_id="pay_test",status="processed",amount=99000,
                currency="INR",notes=new { refund_id=refund.ToString("N"),attempt_id=attempt.ToString("N") } }
        } });
        var handler=new CaptureHandler(body);
        var gateway=new RazorpayPaymentGateway(new ClientFactory(handler));
        var result=await gateway.FindRefundAsync(
            new(PaymentProviders.Razorpay,"synthetic-key","synthetic-secret",null,true,null,null),
            "pay_test",null,refund,attempt,default);
        handler.Method.Should().Be(HttpMethod.Get);
        handler.Path.Should().EndWith("/payments/pay_test/refunds");
        result.Should().NotBeNull();
        result!.ProviderRefundId.Should().Be("rfnd_ours");
        result.Status.Should().Be("PROCESSED");
    }

    [Fact]
    public void RazorpayWebhookRequiresRawBodySignatureAndParsesCapturedPayment()
    {
        const string secret="webhook-secret";
        var raw=Encoding.UTF8.GetBytes("""{"event":"payment_link.paid","payload":{"payment_link":{"entity":{"id":"plink_1","amount_paid":28000,"currency":"INR"}},"payment":{"entity":{"id":"pay_1","order_id":"order_1","amount":28000,"currency":"INR"}}}}""");
        var signature=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),raw)).ToLowerInvariant();
        var gateway=new RazorpayPaymentGateway(new ClientFactory(new CaptureHandler("{}")));
        var result=gateway.VerifyWebhook(new(PaymentProviders.Razorpay,"key","secret",secret,false,null,null),raw,signature,"event-1");
        result.SignatureValid.Should().BeTrue();result.IsPaid.Should().BeTrue();result.ProviderReference.Should().Be("plink_1");result.ProviderPaymentId.Should().Be("pay_1");result.Amount.Should().Be(280m);
        gateway.VerifyWebhook(new(PaymentProviders.Razorpay,"key","secret",secret,false,null,null),raw,new string('0',64),"event-1").SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void PaymentApisAreProtectedExceptVerifiedProviderWebhook()
    {
        typeof(PaymentsController).GetMethods().Where(x=>x.GetCustomAttributes(typeof(HttpMethodAttribute),true).Length>0).Should().OnlyContain(x=>x.GetCustomAttributes(typeof(HasPermissionAttribute),true).Length==1);
        var webhook=typeof(PaymentWebhooksController).GetMethod(nameof(PaymentWebhooksController.Razorpay))!;
        webhook.GetCustomAttributes(typeof(AllowAnonymousAttribute),true).Should().ContainSingle();
        webhook.GetCustomAttributes(typeof(HttpPostAttribute),true).Should().ContainSingle();
    }

    [Fact]
    public void RefundActionsRequireRetailerPermissionsAndNoCustomerEndpointCanSettleMoney()
    {
        var methods=typeof(StorefrontRefundsController).GetMethods()
            .Where(x=>x.GetCustomAttributes(typeof(HttpMethodAttribute),true).Length>0).ToArray();
        methods.Should().HaveCount(5);
        methods.Should().OnlyContain(x=>x.GetCustomAttributes(typeof(HasPermissionAttribute),true).Length>0);
        methods.Should().OnlyContain(x=>x.GetCustomAttributes(typeof(AllowAnonymousAttribute),true).Length==0);
        var moneyOut=methods.Where(x=>x.Name is nameof(StorefrontRefundsController.StartRazorpay)
            or nameof(StorefrontRefundsController.Reconcile)
            or nameof(StorefrontRefundsController.ConfirmManual));
        moneyOut.Should().OnlyContain(x=>x.GetCustomAttributes(typeof(HasPermissionAttribute),true)
            .Cast<HasPermissionAttribute>().Any(a=>a.Policy!.EndsWith(Permissions.Finance.PaymentCreate,
                StringComparison.Ordinal)));
    }

    [Fact]
    public void V36EnforcesTenantOwnershipAttemptsEventsAndApplicationIdempotency()
    {
        var root=Root();var sql=File.ReadAllText(Path.Combine(root,"database","WhatsBiz.Database","Scripts","V36-TenantCommercePayments.sql"));
        sql.Should().Contain("TenantPaymentConfigurations").And.Contain("TenantPaymentProviders").And.Contain("CommercePayments").And.Contain("PaymentProviderEvents").And.Contain("PaymentApplications");
        sql.Should().Contain("UX_TenantPaymentProviders_OneDefault").And.Contain("UX_CommercePayments_TenantInvoiceAttempt").And.Contain("UX_CommercePayments_OneAppliedSettlement").And.Contain("UX_PaymentProviderEvents_ProviderEvent");
        sql.Should().Contain("TR_CommercePayments_TenantOwnership").And.Contain("TR_PaymentProviders_TenantGuard").And.Contain("KeySecretProtected").And.Contain("WebhookSecretProtected");
        File.ReadAllText(Path.Combine(root,"database","WhatsBiz.Database","Scripts","PostDeployment.sql")).Should().Contain("V36-TenantCommercePayments.sql");
    }

    [Fact]
    public void PublicSettingsContractNeverReturnsRazorpaySecrets()
    {
        var names=typeof(PaymentProviderSetting).GetProperties().Select(x=>x.Name).ToArray();
        names.Should().NotContain("KeySecret").And.NotContain("WebhookSecret");
        names.Should().Contain(["HasKeySecret","HasWebhookSecret","MaskedKeyId"]);
    }

    [Fact]
    public void PaymentSettingsEndpointsAreApplicationOwnerOnly()
    {
        var methods=typeof(PaymentsController).GetMethods().Where(x=>x.Name is nameof(PaymentsController.Settings) or nameof(PaymentsController.Razorpay) or nameof(PaymentsController.DirectUpi) or nameof(PaymentsController.Cod) or nameof(PaymentsController.Options)).ToArray();
        methods.Should().HaveCount(5);
        methods.Should().OnlyContain(x=>x.GetCustomAttributes(typeof(PlatformAuthorizeAttribute),true).Length==1&&x.GetCustomAttributes(typeof(HasPermissionAttribute),true).Cast<HasPermissionAttribute>().Single().Policy!.EndsWith(Permissions.Features.Manage,StringComparison.Ordinal));
        methods.SelectMany(x=>x.GetCustomAttributes(typeof(HttpMethodAttribute),true).Cast<HttpMethodAttribute>()).Select(x=>x.Template).Where(x=>x is not null).Should().OnlyContain(x=>x!.StartsWith("administration/tenants/{tenantId:guid}/settings",StringComparison.Ordinal));
    }

    [Fact]
    public void PaymentQueriesSeparateRetailerIsolationFromPlatformScopeAndUseServerPagination()
    {
        var root=Root();var source=File.ReadAllText(Path.Combine(root,"backend","src","WhatsBiz.Infrastructure","Payments","CommercePaymentService.cs"));
        source.Should().Contain("ListPayments(Tenant,null").And.Contain("ListPayments(null,query.TenantId");
        source.Should().Contain("p.TenantId=@tenant").And.Contain("p.CreatedAt>=@from").And.Contain("p.CreatedAt<DATEADD(day,1,@to)");
        source.Should().Contain("COUNT_BIG(*)").And.Contain("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY").And.Contain("ORDER BY p.CreatedAt DESC,p.PaymentId DESC");
        source.Should().Contain("WHERE p.TenantId=@tenant AND p.PaymentId=@id");
    }

    [Fact]
    public void StorefrontMethodsAreTenantGatedAndUseRazorpayOnlyForOnlineMethods()
    {
        var root = Root();
        var service = File.ReadAllText(Path.Combine(root, "backend", "src", "WhatsBiz.Infrastructure", "Payments", "CommercePaymentService.cs"));
        service.Should().Contain("new(\"COD\", PaymentProviders.Cod")
            .And.Contain("new(\"UPI\", PaymentProviders.Razorpay")
            .And.Contain("new(\"NET_BANKING\", PaymentProviders.Razorpay")
            .And.Contain("settings.OnlinePaymentEnabled && razorpay.IsEnabled && razorpay.IsConfigured")
            .And.Contain("settings.UpiEnabled").And.Contain("settings.NetBankingEnabled");
        var storefront = File.ReadAllText(Path.Combine(root, "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontService.cs"));
        storefront.Should().Contain("PaymentProviders.Razorpay => new(method.Code")
            .And.Contain("PaymentProviders.Cod => new(method.Code").And.NotContain("PaymentProviders.DirectUpi =>");
    }

    [Fact]
    public void OnlyVerifiedRazorpayWebhookSettlesAndProviderEventsAreIdempotent()
    {
        var root = Root();
        var service = File.ReadAllText(Path.Combine(root, "backend", "src", "WhatsBiz.Infrastructure", "Payments", "CommercePaymentService.cs"));
        service.Should().Contain("if(!verified.SignatureValid)")
            .And.Contain("PaymentProviderEvents WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("if(added==0)")
            .And.Contain("if(verified.IsPaid)await ApplySuccessfulPayment")
            .And.Contain("Status=CASE WHEN Status=N'PAID' THEN Status ELSE N'FAILED' END");
        var cart = File.ReadAllText(Path.Combine(root, "frontend", "KhataDhari.Customer", "src", "app", "pages", "cart.page.ts"));
        cart.Should().NotContain("status='PAID'").And.NotContain("paymentStatus='PAID'");
        var payments = File.ReadAllText(Path.Combine(root, "frontend", "WhatsBiz.Web", "src", "app", "features", "payments", "payment-list.component.html"));
        payments.Should().Contain("row.provider==='DIRECT_UPI' && row.status==='PENDING_VERIFICATION'");
    }
    private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"database")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Repository root not found.");}
    private sealed class ClientFactory(HttpMessageHandler handler):IHttpClientFactory{public HttpClient CreateClient(string name)=>new(handler,false){BaseAddress=new Uri("https://api.razorpay.com/v1/")};}
    private sealed class CaptureHandler(string response):HttpMessageHandler
    {public string? Body{get;private set;}public string? Authorization{get;private set;}public HttpMethod? Method{get;private set;}public string? Path{get;private set;}protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Body=request.Content is null?null:await request.Content.ReadAsStringAsync(token);Authorization=request.Headers.Authorization?.ToString();Method=request.Method;Path=request.RequestUri?.AbsolutePath;return new(HttpStatusCode.OK){Content=new StringContent(response)};}}
}
