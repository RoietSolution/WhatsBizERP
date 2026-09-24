using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/payments")]
public sealed class PaymentsController(ICommercePaymentService payments, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("methods"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<IReadOnlyCollection<EnabledPaymentMethod>> Methods(CancellationToken token)=>payments.GetEnabledMethodsAsync(token);
    [HttpPost("attempts"), HasPermission(Permissions.POS.Create), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentAttemptResult> Create(CreatePaymentAttemptInput input,CancellationToken token)=>payments.CreateAttemptAsync(input,Actor,token);
    [HttpGet("{paymentId:guid}"), HasPermission(Permissions.Finance.PaymentView), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<CommercePaymentDto> Get(Guid paymentId,CancellationToken token)=>payments.GetPaymentAsync(paymentId,token);
    [HttpGet, HasPermission(Permissions.Finance.PaymentView), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PagedPaymentsDto> List([FromQuery] PaymentQuery query,CancellationToken token)=>payments.ListAsync(query,token);
    [HttpPost("{paymentId:guid}/verify-direct-upi"), HasPermission(Permissions.Finance.PaymentCreate), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<CommercePaymentDto> Verify(Guid paymentId,VerifyDirectUpiInput input,CancellationToken token)=>payments.VerifyDirectUpiAsync(paymentId,input,currentUser.UserId??throw new UnauthorizedAccessException(),Actor,token);

    [HttpGet("administration"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PagedPaymentsDto> Administration([FromQuery] PlatformPaymentQuery query,CancellationToken token)=>payments.ListForAdministrationAsync(query,token);
    [HttpGet("administration/{paymentId:guid}"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<CommercePaymentDto> AdministrationPayment(Guid paymentId,CancellationToken token)=>payments.GetPaymentForAdministrationAsync(paymentId,token);

    [HttpGet("administration/tenants/{tenantId:guid}/settings"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PaymentSettingsDto> Settings(Guid tenantId,CancellationToken token)=>payments.GetSettingsForTenantAsync(tenantId,token);
    [HttpPut("administration/tenants/{tenantId:guid}/settings/razorpay"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PaymentSettingsDto> Razorpay(Guid tenantId,SaveRazorpayConfiguration input,CancellationToken token)=>payments.SaveRazorpayForTenantAsync(tenantId,input,Actor,token);
    [HttpPut("administration/tenants/{tenantId:guid}/settings/direct-upi"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PaymentSettingsDto> DirectUpi(Guid tenantId,SaveDirectUpiConfiguration input,CancellationToken token)=>payments.SaveDirectUpiForTenantAsync(tenantId,input,Actor,token);
    [HttpPut("administration/tenants/{tenantId:guid}/settings/cod"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PaymentSettingsDto> Cod(Guid tenantId,SaveCodConfiguration input,CancellationToken token)=>payments.SaveCodForTenantAsync(tenantId,input,Actor,token);
    [HttpPut("administration/tenants/{tenantId:guid}/settings/options"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<PaymentSettingsDto> Options(Guid tenantId,SavePaymentOptions input,CancellationToken token)=>payments.SaveOptionsForTenantAsync(tenantId,input,Actor,token);
    private string Actor=>currentUser.Username??currentUser.Email??"authenticated-user";
}

[ApiController,Route("api/payments/webhooks")]
public sealed class PaymentWebhooksController(ICommercePaymentService payments):ControllerBase
{
    [AllowAnonymous,HttpPost("razorpay"),RequestSizeLimit(262144)]
    public async Task<IActionResult> Razorpay(CancellationToken token)
    {
        await using var buffer=new MemoryStream();await Request.Body.CopyToAsync(buffer,token);
        var signature=Request.Headers["X-Razorpay-Signature"].ToString();var eventId=Request.Headers["x-razorpay-event-id"].ToString();
        if(string.IsNullOrWhiteSpace(signature))return Unauthorized();
        try{await payments.ProcessRazorpayWebhookAsync(buffer.ToArray(),signature,string.IsNullOrWhiteSpace(eventId)?null:eventId,token);return Ok();}catch(UnauthorizedAccessException){return Unauthorized();}
    }
}
