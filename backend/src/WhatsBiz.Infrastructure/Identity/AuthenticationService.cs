using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Identity;
#pragma warning disable CA1725

public sealed class AuthenticationService(UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, ApplicationDbContext db, IOptions<JwtOptions> options, JwtTokenGenerator tokens, IFeatureService features) : IAuthenticationService
{
    private readonly JwtOptions settings = options.Value;
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken t) { var user = await users.FindByNameAsync(request.Username); if (user is null || !user.IsActive || user.IsDeleted || !await users.CheckPasswordAsync(user, request.Password)) { await LogLogin(request.Username, null, false, "Invalid credentials", t); throw new UnauthorizedAccessException("Invalid username or password."); } await LogLogin(user.UserName ?? request.Username, user.Id, true, null, t); return await Issue(user, null, t); }
    public async Task<AuthResponse> RefreshAsync(string rawToken, CancellationToken t) { var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == Hash(rawToken), t) ?? throw new UnauthorizedAccessException("Invalid refresh token."); if (!stored.IsActive || stored.IsDeleted || stored.RevokedOn is not null || stored.ExpiresOn <= DateTimeOffset.UtcNow) throw new UnauthorizedAccessException("Invalid refresh token."); var user = await users.FindByIdAsync(stored.UserId.ToString()) ?? throw new UnauthorizedAccessException("Invalid refresh token."); if (!user.IsActive || user.IsDeleted) throw new UnauthorizedAccessException("Invalid refresh token."); stored.RevokedOn = DateTimeOffset.UtcNow; stored.ModifiedOn = stored.RevokedOn; stored.ModifiedBy = user.UserName; stored.IsActive = false; return await Issue(user, stored, t); }
    public async Task LogoutAsync(string rawToken, CancellationToken t) { var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == Hash(rawToken), t); if (stored is null || stored.RevokedOn is not null) return; stored.RevokedOn = DateTimeOffset.UtcNow; stored.IsActive = false; await db.SaveChangesAsync(t); var user = await users.FindByIdAsync(stored.UserId.ToString()); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TOP(1) admin.LoginHistory SET LogoutOn=SYSDATETIMEOFFSET() WHERE UserName={user!.UserName} AND LogoutOn IS NULL", t); }
    private async Task<AuthResponse> Issue(ApplicationUser user, RefreshToken? replaced, CancellationToken t) { if (user.TenantId is not Guid tenantId) throw new UnauthorizedAccessException("User is not assigned to a tenant."); var roleNames = (await users.GetRolesAsync(user)).ToArray(); var permissions = new HashSet<string>(StringComparer.Ordinal); foreach (var name in roleNames) { var role = await roles.FindByNameAsync(name); if (role is not null) foreach (var claim in await roles.GetClaimsAsync(role)) if (claim.Type == CustomClaimTypes.Permission) permissions.Add(claim.Value); } var (accessToken, expires) = tokens.Generate(user, roleNames, permissions); var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)); var refresh = new RefreshToken { UserId = user.Id, TokenHash = Hash(raw), ExpiresOn = DateTimeOffset.UtcNow.AddDays(settings.RefreshTokenExpiryDays), CreatedBy = user.UserName }; if (replaced is not null) replaced.ReplacedByTokenHash = refresh.TokenHash; db.RefreshTokens.Add(refresh); await db.SaveChangesAsync(t); return new(accessToken, raw, expires, new(user.Id, tenantId, user.UserName ?? string.Empty, user.Email ?? string.Empty, roleNames, permissions.ToArray(), await features.GetEffectiveFeaturesAsync(tenantId, t))); }
    private Task LogLogin(string name, Guid? id, bool ok, string? reason, CancellationToken t) => db.Database.ExecuteSqlInterpolatedAsync($"INSERT admin.LoginHistory(UserId,UserName,Succeeded,FailureReason) VALUES({id},{name},{ok},{reason})", t);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
