using FluentAssertions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Authentication.CurrentUser;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.Application.Features.Authentication.Login;
using WhatsBiz.Application.Features.Authentication.Logout;
using WhatsBiz.Application.Features.Authentication.RefreshToken;

namespace WhatsBiz.Tests.Authentication;

public sealed class AuthenticationHandlerTests
{
    [Fact]
    public async Task LoginHandlerPassesCredentialsToAuthenticationService()
    {
        var service = new RecordingAuthenticationService();
        var response = await new LoginCommandHandler(service).Handle(new LoginCommand("admin", "password"), default);

        response.Should().Be(service.Response);
        service.LoginRequest.Should().Be(new LoginRequest("admin", "password"));
    }

    [Fact]
    public async Task RefreshHandlerPassesTokenToAuthenticationService()
    {
        var service = new RecordingAuthenticationService();
        var response = await new RefreshTokenCommandHandler(service).Handle(new RefreshTokenCommand("refresh-token"), default);

        response.Should().Be(service.Response);
        service.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task LogoutHandlerPassesTokenToAuthenticationService()
    {
        var service = new RecordingAuthenticationService();
        await new LogoutCommandHandler(service).Handle(new LogoutCommand("refresh-token"), default);

        service.LogoutToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task CurrentUserHandlerMapsAuthenticatedPrincipal()
    {
        var currentUser = new StubCurrentUserService();
        var response = await new GetCurrentUserQueryHandler(currentUser).Handle(new GetCurrentUserQuery(), default);

        response.UserId.Should().Be(currentUser.UserId!.Value);
        response.Roles.Should().Contain("Administrator");
        response.Permissions.Should().Contain("product.view");
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public AuthResponse Response { get; } = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(5), new CurrentUserDto(Guid.NewGuid(), "admin", "admin@whatsbiz.local", [], []));
        public LoginRequest? LoginRequest { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? LogoutToken { get; private set; }
        public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken) { LoginRequest = request; return Task.FromResult(Response); }
        public Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken) { RefreshToken = refreshToken; return Task.FromResult(Response); }
        public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) { LogoutToken = refreshToken; return Task.CompletedTask; }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "admin";
        public string? Email => "admin@whatsbiz.local";
        public IReadOnlyCollection<string> Roles => ["Administrator"];
        public IReadOnlyCollection<string> Permissions => ["product.view"];
    }
}
