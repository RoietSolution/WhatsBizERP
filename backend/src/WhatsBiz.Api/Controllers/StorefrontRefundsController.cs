using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-refunds"), RequireFeature(FeatureKeys.WhatsAppCommerce)]
public sealed class StorefrontRefundsController(ICommerceRefundService refunds,
    ICurrentUserService current) : ControllerBase
{
    private Guid Tenant => current.TenantId ?? throw new System.UnauthorizedAccessException();
    private Guid ActorId => current.UserId ?? throw new System.UnauthorizedAccessException();
    private string Actor => current.Username ?? current.Email ?? "authenticated-user";

    [HttpPost("orders/{invoiceId:guid}/prepare"), HasPermission(Permissions.POS.Void),
     HasPermission(Permissions.Finance.PaymentCreate)]
    public async Task<ActionResult<StorefrontRefundDto>> Prepare(Guid invoiceId,CancellationToken token)
    {
        try { return Ok(await refunds.PrepareFullCancellationAsync(Tenant,invoiceId,ActorId,token)); }
        catch(BusinessRuleException exception) { return BadRequest(new { message=exception.Message }); }
    }

    [HttpGet("{refundId:guid}"), HasPermission(Permissions.Finance.PaymentView)]
    public async Task<ActionResult<StorefrontRefundDto>> Get(Guid refundId,CancellationToken token)
    {
        try { return Ok(await refunds.GetAsync(Tenant,refundId,token)); }
        catch(BusinessRuleException exception) { return NotFound(new { message=exception.Message }); }
    }

    [HttpPost("{refundId:guid}/razorpay/start"), HasPermission(Permissions.POS.Void),
     HasPermission(Permissions.Finance.PaymentCreate)]
    public async Task<ActionResult<StorefrontRefundDto>> StartRazorpay(Guid refundId,CancellationToken token)
    {
        try { return Ok(await refunds.StartRazorpayAsync(Tenant,refundId,token)); }
        catch(BusinessRuleException exception) { return BadRequest(new { message=exception.Message }); }
    }

    [HttpPost("{refundId:guid}/razorpay/reconcile"), HasPermission(Permissions.POS.Void),
     HasPermission(Permissions.Finance.PaymentCreate)]
    public async Task<ActionResult<StorefrontRefundDto>> Reconcile(Guid refundId,CancellationToken token)
    {
        try { return Ok(await refunds.ReconcileRazorpayAsync(Tenant,refundId,token)); }
        catch(BusinessRuleException exception) { return BadRequest(new { message=exception.Message }); }
    }

    [HttpPost("{refundId:guid}/manual/confirm"), HasPermission(Permissions.POS.Void),
     HasPermission(Permissions.Finance.PaymentCreate)]
    public async Task<ActionResult<StorefrontRefundDto>> ConfirmManual(Guid refundId,
        ConfirmManualRefundInput input,CancellationToken token)
    {
        try { return Ok(await refunds.ConfirmManualAsync(Tenant,refundId,input,Actor,token)); }
        catch(BusinessRuleException exception) { return BadRequest(new { message=exception.Message }); }
    }
}
