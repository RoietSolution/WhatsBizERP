using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace WhatsBiz.Infrastructure.Identity;

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresOnUtc) Generate(
        ApplicationUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(CustomClaimTypes.Permission, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
