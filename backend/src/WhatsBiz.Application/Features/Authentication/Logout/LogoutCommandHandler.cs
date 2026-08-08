using MediatR;
using WhatsBiz.Application.Common.Interfaces;
namespace WhatsBiz.Application.Features.Authentication.Logout;
public sealed class LogoutCommandHandler(IAuthenticationService authenticationService) : IRequestHandler<LogoutCommand> { public async Task Handle(LogoutCommand request, CancellationToken cancellationToken) => await authenticationService.LogoutAsync(request.Token, cancellationToken); }
