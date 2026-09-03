using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Infrastructure.WhatsApp;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class WhatsAppWebhookVerificationTests
{
    private const string ConfiguredToken = "configured-test-token";

    [Fact]
    public void ControllerBindsTheExactMetaQueryParameterNames()
    {
        var parameters = typeof(WhatsAppController)
            .GetMethod(nameof(WhatsAppController.VerifyWebhook))!
            .GetParameters()[..3];

        parameters.Select(parameter => parameter.GetCustomAttribute<FromQueryAttribute>()?.Name)
            .Should().Equal("hub.mode", "hub.verify_token", "hub.challenge");
    }

    [Fact]
    public async Task CorrectTokenOnPublicRequestReturnsExactPlainTextChallenge()
    {
        var controller = Controller();

        var response = await controller.VerifyWebhook("subscribe", ConfiguredToken, "123456", default);

        var content = response.Should().BeOfType<ContentResult>().Which;
        content.StatusCode.Should().BeNull("HTTP 200 is the ContentResult default");
        content.ContentType.Should().Be("text/plain");
        content.Content.Should().Be("123456");
    }

    [Theory]
    [InlineData("subscribe", "wrong-token")]
    [InlineData("subscribe", null)]
    [InlineData("invalid", ConfiguredToken)]
    public async Task InvalidVerificationRequestReturnsForbidden(string mode, string? token)
    {
        var controller = Controller();

        var response = await controller.VerifyWebhook(mode, token, "123456", default);

        response.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void TenantTokenResolutionDoesNotLeakAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var candidates = new[]
        {
            (tenantA, (string?)"tenant-a-token"),
            (tenantB, (string?)"tenant-b-token")
        };

        WhatsAppService.ResolveUniqueTenantToken(candidates, "tenant-b-token").Should().Be(tenantB);
        WhatsAppService.ResolveUniqueTenantToken(candidates, "unknown-token").Should().BeNull();
    }

    [Fact]
    public void AmbiguousTokenSharedByTwoTenantsIsRejected()
    {
        var candidates = new[]
        {
            (Guid.NewGuid(), (string?)"duplicate-token"),
            (Guid.NewGuid(), (string?)"duplicate-token")
        };

        WhatsAppService.ResolveUniqueTenantToken(candidates, "duplicate-token").Should().BeNull();
    }

    [Fact]
    public void VerificationLoggingHasNoSecretValueParameters()
    {
        typeof(WhatsAppLogs).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("WebhookVerification", StringComparison.Ordinal)
                || method.Name.StartsWith("WebhookPost", StringComparison.Ordinal))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name)
            .Should().NotContain(["verifyToken", "accessToken", "appSecret", "signature", "challenge", "body"]);
    }

    [Fact]
    public async Task ValidSignedPostUsesExactRawBytesReturnsOkAndLeavesBodyReadable()
    {
        const string secret = "tenant-b-app-secret";
        var body = Encoding.UTF8.GetBytes("{\n  \"object\": \"whatsapp_business_account\", \"entry\": []\n}");
        var controller = PostController(secret, body, Signature(secret, body));

        var result = await controller.ReceiveWebhook(default);

        result.Should().BeOfType<OkResult>();
        controller.Request.Body.Position.Should().Be(0);
        using var copy = new MemoryStream();
        await controller.Request.Body.CopyToAsync(copy);
        copy.ToArray().Should().Equal(body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task MissingOrInvalidPostSignatureReturnsUnauthorized(string? signature)
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\",\"entry\":[]}");
        var controller = PostController("correct-app-secret", body, signature);

        var result = await controller.ReceiveWebhook(default);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void SignatureForExactBodyFailsAfterWhitespaceByteModification()
    {
        const string secret = "correct-app-secret";
        var exactBody = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\"}");
        var changedBody = Encoding.UTF8.GetBytes("{ \"object\":\"whatsapp_business_account\"}");
        var signature = Signature(secret, exactBody);

        WhatsAppService.ValidSignature(signature, exactBody, secret).Should().BeTrue();
        WhatsAppService.ValidSignature(signature, changedBody, secret).Should().BeFalse();
        WhatsAppService.ValidSignature(signature, exactBody, "another-tenant-secret").Should().BeFalse();
    }

    private static WhatsAppController Controller() =>
        new(new VerificationOnlyWhatsAppService(ConfiguredToken), new AnonymousCurrentUser());

    private static WhatsAppController PostController(string secret, byte[] body, string? signature)
    {
        var controller = new WhatsAppController(new RawBodySignatureService(secret), new AnonymousCurrentUser());
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        if (signature is not null) context.Request.Headers["X-Hub-Signature-256"] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static string Signature(string secret, byte[] body) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    private sealed class AnonymousCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public string? Username => null;
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class VerificationOnlyWhatsAppService(string configuredToken) : IWhatsAppService
    {
        public Task<string?> VerifyWebhookAsync(string? mode, string? verifyToken, string? challenge,
            CancellationToken token) => Task.FromResult(
            mode == "subscribe" && verifyToken == configuredToken && challenge is not null
                ? challenge
                : null);

        public Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppTestMessageResult> SendTestMessageAsync(Guid tenantId, SendWhatsAppTestMessageInput input, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppMetaTestDiagnosticsDto> GetDiagnosticsAsync(Guid tenantId, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppPlatformConfigurationDto> GetPlatformConfigurationAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppPlatformConfigurationDto> SavePlatformConfigurationAsync(SaveWhatsAppPlatformConfigurationInput input, string? actor, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> GetRetailerConnectionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<PagedWhatsAppContacts> GetContactsAsync(Guid tenantId, string? search, string? status, int pageNumber, int pageSize, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppContactDto> LinkContactAsync(Guid tenantId, Guid contactId, Guid customerId, string? actor, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class RawBodySignatureService(string secret) : IWhatsAppService
    {
        public Task<bool> ReceiveWebhookAsync(string? signature, ReadOnlyMemory<byte> body,
            CancellationToken token) => Task.FromResult(
            WhatsAppService.ValidSignature(signature, body.Span, secret));

        public Task<string?> VerifyWebhookAsync(string? mode, string? verifyToken, string? challenge, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppConfigurationDto> GetConfigurationAsync(Guid tenantId, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppConfigurationDto> SaveConfigurationAsync(Guid tenantId, SaveWhatsAppConfigurationInput input, string? actor, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppConnectionResult> ValidateConnectionAsync(Guid tenantId, string? replacementAccessToken, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppTestMessageResult> SendTestMessageAsync(Guid tenantId, SendWhatsAppTestMessageInput input, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppMetaTestDiagnosticsDto> GetDiagnosticsAsync(Guid tenantId, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppPlatformConfigurationDto> GetPlatformConfigurationAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppPlatformConfigurationDto> SavePlatformConfigurationAsync(SaveWhatsAppPlatformConfigurationInput input, string? actor, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RetailerWhatsAppConnectionDto>> GetRetailerConnectionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<PagedWhatsAppContacts> GetContactsAsync(Guid tenantId, string? search, string? status, int pageNumber, int pageSize, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsAppContactDto> LinkContactAsync(Guid tenantId, Guid contactId, Guid customerId, string? actor, CancellationToken token) => throw new NotSupportedException();
    }
}
