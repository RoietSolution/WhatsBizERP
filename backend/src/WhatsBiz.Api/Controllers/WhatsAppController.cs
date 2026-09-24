using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/whatsapp")]
public sealed partial class WhatsAppController(IWhatsAppService service, ICurrentUserService currentUser,
    IFeatureService? features = null, IWhatsAppUsageBillingService? usageBilling = null,
    ILogger<WhatsAppController>? logger = null) : ControllerBase
{
    [HttpGet("usage/summary"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<WhatsAppUsageSummary> UsageSummary([FromQuery] int year, [FromQuery] int month, CancellationToken token) =>
        (usageBilling ?? throw new InvalidOperationException("WhatsApp usage billing service is unavailable."))
            .GetSummaryAsync(TenantId(), year, month, token);

    [HttpGet("configuration"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WhatsAppConfiguration)]
    public Task<WhatsAppConfigurationDto> GetForRetailer(CancellationToken token) => service.GetConfigurationAsync(TenantId(), token);

    [HttpGet("onboarding/config"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppOnboardingConfigurationDto> OnboardingConfig(CancellationToken token) => service.GetOnboardingConfigurationAsync(token);

    [HttpPut("configuration"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppConfiguration)]
    public Task<WhatsAppConfigurationDto> SaveForRetailer(SaveWhatsAppConfigurationInput input, CancellationToken token) => service.SaveConfigurationAsync(TenantId(), input, currentUser.Username, token);

    [HttpPost("onboarding/complete"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppConnectionResult> CompleteOnboarding(WhatsAppOnboardingCompletionInput input, CancellationToken token) =>
        service.CompleteOnboardingAsync(TenantId(), input, currentUser.Username, token);

    [HttpPost("configuration/validate"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppConnectionResult> ValidateForRetailer(ValidateWhatsAppConnectionInput? input, CancellationToken token) => service.ValidateConnectionAsync(TenantId(), input?.AccessToken, token);

    [HttpPost("configuration/test-message"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppTestMessageResult> SendTestMessageForRetailer(SendWhatsAppTestMessageInput input, CancellationToken token) => service.SendTestMessageAsync(TenantId(), input, token);

    [HttpGet("configuration/diagnostics"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WebhookDiagnostics)]
    public Task<WhatsAppMetaTestDiagnosticsDto> DiagnosticsForRetailer(CancellationToken token) => service.GetDiagnosticsAsync(TenantId(), token);

    [HttpGet("administration/tenants/{tenantId:guid}/configuration"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppConfigurationDto> Get(Guid tenantId, CancellationToken token) => await service.GetConfigurationAsync(await TargetTenant(tenantId, token), token);

    [HttpPut("administration/tenants/{tenantId:guid}/configuration"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppConfigurationDto> Save(Guid tenantId, SaveWhatsAppConfigurationInput input, CancellationToken token) => await service.SaveConfigurationAsync(await TargetTenant(tenantId, token), input, currentUser.Username, token);

    [HttpPost("administration/tenants/{tenantId:guid}/configuration/validate"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppConnectionResult> Validate(Guid tenantId, ValidateWhatsAppConnectionInput? input, CancellationToken token) => await service.ValidateConnectionAsync(await TargetTenant(tenantId, token), input?.AccessToken, token);

    [HttpPost("administration/tenants/{tenantId:guid}/configuration/test-message"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppTestMessageResult> SendTestMessage(Guid tenantId, SendWhatsAppTestMessageInput input, CancellationToken token) =>
        await service.SendTestMessageAsync(await TargetTenant(tenantId, token), input, token);

    [HttpGet("administration/tenants/{tenantId:guid}/configuration/diagnostics"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppMetaTestDiagnosticsDto> Diagnostics(Guid tenantId, CancellationToken token) => await service.GetDiagnosticsAsync(await TargetTenant(tenantId, token), token);

    [HttpGet("administration/tenants/{tenantId:guid}/configuration/subscription-diagnostics"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppSubscriptionDiagnosticDto> SubscriptionDiagnostics(Guid tenantId, CancellationToken token) =>
        await service.GetSubscriptionDiagnosticAsync(await TargetTenant(tenantId, token), token);

    [HttpGet("administration/platform"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<WhatsAppPlatformConfigurationDto> Platform(CancellationToken token) => service.GetPlatformConfigurationAsync(token);

    [HttpPut("administration/platform"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<WhatsAppPlatformConfigurationDto> Platform(SaveWhatsAppPlatformConfigurationInput input, CancellationToken token) => service.SavePlatformConfigurationAsync(input,currentUser.Username,token);

    [HttpGet("administration/retailer-connections"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> RetailerConnections(CancellationToken token) => service.GetRetailerConnectionsAsync(token);

    [AllowAnonymous, HttpGet("webhook")]
    public async Task<IActionResult> VerifyWebhook([FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge, CancellationToken token)
    { var result = await service.VerifyWebhookAsync(mode, verifyToken, challenge, token); return result is null ? Forbid() : Content(result, "text/plain"); }

    [AllowAnonymous, HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        if (Request.ContentLength > 1_048_576)
        {
            if (logger is not null) WebhookOversized(logger, "BEFORE_BODY_READ",
                Request.Headers.ContainsKey("X-Hub-Signature-256"), StatusCodes.Status413PayloadTooLarge,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        Request.EnableBuffering();
        if (Request.Body.CanSeek) Request.Body.Position = 0;
        using var stream = new MemoryStream();
        await Request.Body.CopyToAsync(stream, token);
        if (Request.Body.CanSeek) Request.Body.Position = 0;
        if (stream.Length > 1_048_576)
        {
            if (logger is not null) WebhookOversized(logger, "AFTER_BODY_READ",
                Request.Headers.ContainsKey("X-Hub-Signature-256"), StatusCodes.Status413PayloadTooLarge,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var result = await service.ReceiveWebhookAsync(
            Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), stream.ToArray(), token);
        return result switch
        {
            WhatsAppWebhookReceiveResult.Acknowledged => Ok(),
            WhatsAppWebhookReceiveResult.InvalidPayload => BadRequest(),
            WhatsAppWebhookReceiveResult.PersistenceFailure => StatusCode(StatusCodes.Status500InternalServerError),
            _ => Unauthorized()
        };
    }

    [LoggerMessage(2201, LogLevel.Warning, "WhatsApp webhook POST rejected as oversized at {Phase}; signature present: {SignaturePresent}; HTTP {StatusCode}; duration {DurationMilliseconds} ms.")]
    private static partial void WebhookOversized(ILogger logger, string phase, bool signaturePresent, int statusCode, double durationMilliseconds);

    private async Task<Guid> TargetTenant(Guid tenantId, CancellationToken token)
    {
        await (features ?? throw new InvalidOperationException("Feature service is unavailable."))
            .GetTenantConfigurationAsync(tenantId, token);
        HttpContext.Items[AuditMiddleware.TargetTenantItemKey] = tenantId;
        return tenantId;
    }

    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");

}

public sealed record ValidateWhatsAppConnectionInput(string? AccessToken);

[ApiController, Route("api/whatsapp-contacts"), RequireFeature(FeatureKeys.WhatsAppCommerce)]
public sealed class WhatsAppContactsController(IWhatsAppService service, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.View)]
    public Task<PagedWhatsAppContacts> Get([FromQuery] string? search,[FromQuery] string? status,
        [FromQuery] int pageNumber=1,[FromQuery] int pageSize=20,CancellationToken token=default) =>
        service.GetContactsAsync(TenantId(),search,status,pageNumber,pageSize,token);

    [HttpPost("{id:guid}/link"), HasPermission(Permissions.Customer.Edit)]
    public Task<WhatsAppContactDto> Link(Guid id,LinkWhatsAppContactInput input,CancellationToken token) =>
        service.LinkContactAsync(TenantId(),id,input.CustomerId,currentUser.Username,token);

    private Guid TenantId()=>currentUser.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");
}
