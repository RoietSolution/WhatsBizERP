using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using WhatsBiz.Application.Features.Authentication.CurrentUser;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.Application.Features.Authentication.Login;
using WhatsBiz.Application.Features.Authentication.Logout;
using WhatsBiz.Application.Features.Authentication.RefreshToken;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Identity;
namespace WhatsBiz.Api.Controllers;
[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender, UserManager<ApplicationUser> users, IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous][HttpPost("login")][ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)][ProducesResponseType(StatusCodes.Status400BadRequest)] public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) => sender.Send(new LoginCommand(request.Username, request.Password), cancellationToken);
    [AllowAnonymous][HttpPost("refresh")][ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)] public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) => sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
    [Authorize][HttpPost("logout")][ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) { await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken); return NoContent(); }
    [Authorize][HttpGet("me")][ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)] public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => sender.Send(new GetCurrentUserQuery(), cancellationToken);

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<ForgotPasswordResponse> ForgotPassword(ForgotPasswordRequest request)
    {
        var identifier = request.Identifier.Trim();
        if (string.IsNullOrWhiteSpace(identifier)) return new("If the account exists, reset instructions have been prepared.");
        var user = await users.FindByNameAsync(identifier) ?? await users.FindByEmailAsync(identifier);
        if (user is null || !user.IsActive || user.IsDeleted) return new("If the account exists, reset instructions have been prepared.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return environment.IsDevelopment()
            ? new("Reset instructions are ready for this development environment.", encoded, user.Id.ToString())
            : new("If the account exists, reset instructions have been sent.");
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || !user.IsActive || user.IsDeleted) return BadRequest(new ProblemDetails { Title = "Password reset failed", Detail = "The reset link is invalid or has expired." });
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (FormatException) { return BadRequest(new ProblemDetails { Title = "Password reset failed", Detail = "The reset link is invalid or has expired." }); }
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        return result.Succeeded ? NoContent() : BadRequest(new ProblemDetails { Title = "Password reset failed", Detail = string.Join("; ", result.Errors.Select(error => error.Description)) });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await GetAuthenticatedUser();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? NoContent() : BadRequest(new ProblemDetails { Title = "Password change failed", Detail = string.Join("; ", result.Errors.Select(error => error.Description)) });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<CurrentUserDto>> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await GetAuthenticatedUser();
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new ProblemDetails { Title = "Profile update failed", Detail = "Email is required." });
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null && existing.Id != user.Id) return Conflict(new ProblemDetails { Title = "Profile update failed", Detail = "That email address is already in use." });
        user.Email = email;
        user.NormalizedEmail = users.NormalizeEmail(email);
        user.ModifiedOn = DateTimeOffset.UtcNow;
        user.ModifiedBy = user.UserName;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(new ProblemDetails { Title = "Profile update failed", Detail = string.Join("; ", result.Errors.Select(error => error.Description)) });
        var roles = await users.GetRolesAsync(user);
        var permissions = User.FindAll(CustomClaimTypes.Permission).Select(claim => claim.Value).ToArray();
        var tenantId = user.TenantId ?? throw new UnauthorizedAccessException("User is not assigned to a tenant.");
        var features = await HttpContext.RequestServices.GetRequiredService<IFeatureService>().GetEffectiveFeaturesAsync(tenantId, HttpContext.RequestAborted);
        return new CurrentUserDto(user.Id, tenantId, user.UserName ?? string.Empty, user.Email ?? string.Empty, roles.ToArray(), permissions, features);
    }

    private async Task<ApplicationUser> GetAuthenticatedUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("The current session is invalid.");
        return await users.FindByIdAsync(id) ?? throw new UnauthorizedAccessException("The current session is invalid.");
    }
}
