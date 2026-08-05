using MediatR; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using WhatsBiz.Application.Features.Authentication.CurrentUser; using WhatsBiz.Application.Features.Authentication.DTOs; using WhatsBiz.Application.Features.Authentication.Login; using WhatsBiz.Application.Features.Authentication.Logout; using WhatsBiz.Application.Features.Authentication.RefreshToken;
namespace WhatsBiz.Api.Controllers;
[ApiController] [Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [AllowAnonymous] [HttpPost("login")] [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)] [ProducesResponseType(StatusCodes.Status400BadRequest)] public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) => sender.Send(new LoginCommand(request.Username, request.Password), cancellationToken);
    [AllowAnonymous] [HttpPost("refresh")] [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)] public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) => sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
    [Authorize] [HttpPost("logout")] [ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) { await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken); return NoContent(); }
    [Authorize] [HttpGet("me")] [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)] public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => sender.Send(new GetCurrentUserQuery(), cancellationToken);
}
