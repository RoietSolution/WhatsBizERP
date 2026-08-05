using MediatR; using WhatsBiz.Application.Common.Interfaces; using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.Login;
public sealed class LoginCommandHandler(IAuthenticationService authenticationService) : IRequestHandler<LoginCommand, AuthResponse> { public Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken) => authenticationService.LoginAsync(new LoginRequest(request.Username, request.Password), cancellationToken); }
