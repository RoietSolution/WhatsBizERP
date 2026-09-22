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
    [HttpGet("settings"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentSettingsDto> Settings(CancellationToken token) => payments.GetSettingsAsync(token);

    [HttpPut("settings/razorpay"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentSettingsDto> Razorpay(SaveRazorpayConfiguration input, CancellationToken token) => payments.SaveRazorpayAsync(input, Actor, token);

    [HttpPut("settings/direct-upi"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentSettingsDto> DirectUpi(SaveDirectUpiConfiguration input, CancellationToken token) => payments.SaveDirectUpiAsync(input, Actor, token);

    [HttpPut("settings/cod"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentSettingsDto> Cod(SaveCodConfiguration input, CancellationToken token) => payments.SaveCodAsync(input, Actor, token);

    [HttpPut("settings/options"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentSettingsDto> Options(SavePaymentOptions input, CancellationToken token) => payments.SaveOptionsAsync(input, Actor, token);

    [HttpGet("methods"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<IReadOnlyCollection<EnabledPaymentMethod>> Methods(CancellationToken token) => payments.GetEnabledMethodsAsync(token);

    [HttpPost("attempts"), HasPermission(Permissions.POS.Create), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<PaymentAttemptResult> Create(CreatePaymentAttemptInput input, CancellationToken token) => payments.CreateAttemptAsync(input, Actor, token);

    [HttpGet("{paymentId:guid}"), HasPermission(Permissions.Finance.PaymentView), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<CommercePaymentDto> Get(Guid paymentId, CancellationToken token) => payments.GetPaymentAsync(paymentId, token);

    [HttpGet, HasPermission(Permissions.Finance.PaymentView), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<IReadOnlyCollection<CommercePaymentDto>> List([FromQuery] PaymentQuery query, CancellationToken token) => payments.ListAsync(query, token);

    [HttpPost("{paymentId:guid}/verify-direct-upi"), HasPermission(Permissions.Finance.PaymentCreate), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<CommercePaymentDto> Verify(Guid paymentId, VerifyDirectUpiInput input, CancellationToken token) =>
        payments.VerifyDirectUpiAsync(paymentId, input, currentUser.UserId ?? throw new UnauthorizedAccessException(), Actor, token);

    private string Actor => currentUser.Username ?? currentUser.Email ?? "authenticated-user";
}

[ApiController, Route("api/payments/webhooks")]
public sealed class PaymentWebhooksController(ICommercePaymentService payments) : ControllerBase
{
    [AllowAnonymous, HttpPost("razorpay"), RequestSizeLimit(262144)]
    public async Task<IActionResult> Razorpay(CancellationToken token)
    {
        await using var buffer = new MemoryStream(); await Request.Body.CopyToAsync(buffer, token);
        var signature = Request.Headers["X-Razorpay-Signature"].ToString();
        var eventId = Request.Headers["x-razorpay-event-id"].ToString();
        if (string.IsNullOrWhiteSpace(signature)) return Unauthorized();
        try { await payments.ProcessRazorpayWebhookAsync(buffer.ToArray(), signature, string.IsNullOrWhiteSpace(eventId) ? null : eventId, token); return Ok(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}
