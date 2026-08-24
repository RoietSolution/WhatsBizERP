using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/whatsapp")]
public sealed class WhatsAppController(IWhatsAppService service, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("configuration"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WhatsAppConfiguration)]
    public Task<WhatsAppConfigurationDto> Get(CancellationToken token) => service.GetConfigurationAsync(TenantId(), token);

    [HttpPut("configuration"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppConfiguration)]
    public Task<WhatsAppConfigurationDto> Save(SaveWhatsAppConfigurationInput input, CancellationToken token) => service.SaveConfigurationAsync(TenantId(), input, currentUser.Username, token);

    [HttpPost("configuration/validate"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppConnectionResult> Validate(ValidateWhatsAppConnectionInput? input, CancellationToken token) => service.ValidateConnectionAsync(TenantId(), input?.AccessToken, token);

    [HttpPost("configuration/test-message"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.MetaWhatsAppIntegration)]
    public Task<WhatsAppTestMessageResult> SendTestMessage(SendWhatsAppTestMessageInput input, CancellationToken token) =>
        service.SendTestMessageAsync(TenantId(), input, token);

    [HttpGet("configuration/diagnostics"), HasPermission(Permissions.Admin.View), RequireFeature(FeatureKeys.WebhookDiagnostics)]
    public Task<WhatsAppMetaTestDiagnosticsDto> Diagnostics(CancellationToken token) => service.GetDiagnosticsAsync(TenantId(), token);

    [HttpGet("administration/platform"), HasPermission(Permissions.Features.Manage)]
    public Task<WhatsAppPlatformConfigurationDto> Platform(CancellationToken token) => service.GetPlatformConfigurationAsync(token);

    [HttpPut("administration/platform"), HasPermission(Permissions.Features.Manage)]
    public Task<WhatsAppPlatformConfigurationDto> Platform(SaveWhatsAppPlatformConfigurationInput input, CancellationToken token) => service.SavePlatformConfigurationAsync(input,currentUser.Username,token);

    [HttpGet("administration/retailer-connections"), HasPermission(Permissions.Features.Manage)]
    public Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> RetailerConnections(CancellationToken token) => service.GetRetailerConnectionsAsync(token);

    [AllowAnonymous, HttpGet("webhook")]
    public async Task<IActionResult> VerifyWebhook([FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string verifyToken,
        [FromQuery(Name = "hub.challenge")] string challenge, CancellationToken token)
    { var result = await service.VerifyWebhookAsync(mode, verifyToken, challenge, token); return result is null ? Forbid() : Content(result, "text/plain"); }

    [AllowAnonymous, HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken token)
    { if (Request.ContentLength > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge); using var stream = new MemoryStream(); await Request.Body.CopyToAsync(stream, token); if (stream.Length > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge); var accepted = await service.ReceiveWebhookAsync(Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), stream.ToArray(), token); return accepted ? Ok() : Unauthorized(); }

    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}

public sealed record ValidateWhatsAppConnectionInput(string? AccessToken);
