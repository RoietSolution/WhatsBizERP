using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Authentication;

public sealed class JwtTokenGeneratorTests
{
    [Fact]
    public void GenerateCreatesSignedTokenWithRoleAndPermissionClaims()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "WhatsBiz.Tests",
            Audience = "WhatsBiz.Web.Tests",
            SigningKey = "a-test-signing-key-that-is-at-least-32-characters-long",
            ExpiryMinutes = 15
        });
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin", Email = "admin@whatsbiz.local" };

        var result = new JwtTokenGenerator(options).Generate(user, ["Administrator"], [Permissions.Product.View]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Issuer.Should().Be(options.Value.Issuer);
        token.Audiences.Should().Contain(options.Value.Audience);
        token.Claims.Should().Contain(claim => claim.Type == ClaimTypes.Role && claim.Value == "Administrator");
        token.Claims.Should().Contain(claim => claim.Type == CustomClaimTypes.Permission && claim.Value == Permissions.Product.View);
        result.ExpiresOnUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
