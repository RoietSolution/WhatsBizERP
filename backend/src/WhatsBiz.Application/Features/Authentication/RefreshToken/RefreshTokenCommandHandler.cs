using MediatR; using WhatsBiz.Application.Common.Interfaces; using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.RefreshToken;
public sealed class RefreshTokenCommandHandler(IAuthenticationService authenticationService) : IRequestHandler<RefreshTokenCommand, AuthResponse> { public Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken) => authenticationService.RefreshAsync(request.Token, cancellationToken); }
