using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Features.Authentication.CurrentUser;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.Application.Features.Authentication.Login;
using WhatsBiz.Application.Features.Authentication.Logout;
using WhatsBiz.Application.Features.Authentication.RefreshToken;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.DemoRequests;
namespace WhatsBiz.Api.Controllers;
[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender, UserManager<ApplicationUser> users, IDemoRequestEmailSender emailSender, IOptions<DemoRequestOptions> smtpOptions, IOptions<PasswordResetOptions> resetOptions, ILogger<AuthController> logger) : ControllerBase
{
    private const string GenericResetMessage = "If the account exists, reset instructions have been sent.";
    private static readonly Action<ILogger, Guid, string, Exception?> ResetEmailFailed =
        LoggerMessage.Define<Guid, string>(LogLevel.Error, new EventId(3201, nameof(ResetEmailFailed)), "Password reset email delivery failed for user {UserId}; failure type: {FailureType}");
    [AllowAnonymous][HttpPost("login")][ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)][ProducesResponseType(StatusCodes.Status400BadRequest)] public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) => sender.Send(new LoginCommand(request.Username, request.Password, "Retailer"), cancellationToken);
    [AllowAnonymous][HttpPost("application-owner/login")][ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)][ProducesResponseType(StatusCodes.Status400BadRequest)] public Task<AuthResponse> ApplicationOwnerLogin(LoginRequest request, CancellationToken cancellationToken) => sender.Send(new LoginCommand(request.Username, request.Password, "ApplicationOwner"), cancellationToken);
    [AllowAnonymous][HttpPost("refresh")][ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)] public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) => sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
    [Authorize][HttpPost("logout")][ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) { await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken); return NoContent(); }
    [Authorize][HttpGet("me")][ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)] public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => sender.Send(new GetCurrentUserQuery(), cancellationToken);

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<ForgotPasswordResponse> ForgotPassword(ForgotPasswordRequest request)
    {
        var identifier = request.Identifier.Trim();
        if (string.IsNullOrWhiteSpace(identifier)) return new(GenericResetMessage);
        var user = await users.FindByNameAsync(identifier) ?? await users.FindByEmailAsync(identifier);
        if (user is null || !user.IsActive || user.IsDeleted) return new(GenericResetMessage);
        try
        {
            if (!smtpOptions.Value.Email.Enabled) throw new InvalidOperationException("Password reset email is disabled.");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var baseUrl = resetOptions.Value.FrontendBaseUrl.TrimEnd('/');
            var link = $"{baseUrl}/reset-password?userId={Uri.EscapeDataString(user.Id.ToString())}&token={Uri.EscapeDataString(encoded)}";
            var body = $"<p>A password reset was requested for your KhataDhari account.</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Reset password</a></p><p>This link expires in {Math.Clamp(resetOptions.Value.TokenLifespanMinutes, 5, 1440)} minutes. If you did not request this, ignore this email.</p>";
            await emailSender.SendAsync(new DemoRequestEmail(user.Email!, "Reset your KhataDhari password", body, true), HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ResetEmailFailed(logger, user.Id, exception.GetType().Name, null);
        }
        return new(GenericResetMessage);
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
        if (!result.Succeeded) return BadRequest(new ProblemDetails { Title = "Password reset failed", Detail = string.Join("; ", result.Errors.Select(error => error.Description)) });
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var update = await users.UpdateAsync(user);
            if (!update.Succeeded) return BadRequest(new ProblemDetails { Title = "Password reset failed", Detail = string.Join("; ", update.Errors.Select(error => error.Description)) });
        }
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await GetAuthenticatedUser();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new ProblemDetails { Title = "Password change failed", Detail = string.Join("; ", result.Errors.Select(error => error.Description)) });
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            user.ModifiedOn = DateTimeOffset.UtcNow;
            user.ModifiedBy = user.UserName;
            var update = await users.UpdateAsync(user);
            if (!update.Succeeded) return BadRequest(new ProblemDetails { Title = "Password change failed", Detail = string.Join("; ", update.Errors.Select(error => error.Description)) });
        }
        return NoContent();
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
        var features = user.TenantId is Guid tenantId
            ? await HttpContext.RequestServices.GetRequiredService<IFeatureService>().GetEffectiveFeaturesAsync(tenantId, HttpContext.RequestAborted)
            : new Dictionary<string, bool>();
        return new CurrentUserDto(user.Id, user.TenantId, user.UserName ?? string.Empty, user.Email ?? string.Empty, roles.ToArray(), permissions, features) { MustChangePassword = user.MustChangePassword };
    }

    private async Task<ApplicationUser> GetAuthenticatedUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("The current session is invalid.");
        return await users.FindByIdAsync(id) ?? throw new UnauthorizedAccessException("The current session is invalid.");
    }
}
