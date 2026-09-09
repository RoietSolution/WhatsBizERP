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
        var tenantId = Guid.NewGuid();
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = tenantId, UserName = "admin", Email = "admin@whatsbiz.local" };

        var result = new JwtTokenGenerator(options).Generate(user, ["Administrator"], [Permissions.Product.View]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Issuer.Should().Be(options.Value.Issuer);
        token.Audiences.Should().Contain(options.Value.Audience);
        token.Claims.Should().Contain(claim => claim.Type == ClaimTypes.Role && claim.Value == "Administrator");
        token.Claims.Should().Contain(claim => claim.Type == CustomClaimTypes.Permission && claim.Value == Permissions.Product.View);
        token.Claims.Should().Contain(claim => claim.Type == CustomClaimTypes.TenantId && claim.Value == tenantId.ToString());
        result.ExpiresOnUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ApplicationOwnerTokenHasPlatformClaimsAndNoTenantClaim()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "WhatsBiz.Tests", Audience = "WhatsBiz.Web.Tests",
            SigningKey = "a-test-signing-key-that-is-at-least-32-characters-long", ExpiryMinutes = 15
        });
        var owner = new ApplicationUser { Id = Guid.NewGuid(), TenantId = null, AccountType = AccountTypes.ApplicationOwner, UserName = "owner", Email = "owner@example.test" };

        var result = new JwtTokenGenerator(options).Generate(owner, ["ApplicationOwner"], [Permissions.Features.Manage]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Claims.Should().Contain(claim => claim.Type == ClaimTypes.Role && claim.Value == "ApplicationOwner");
        token.Claims.Should().Contain(claim => claim.Type == CustomClaimTypes.Permission && claim.Value == Permissions.Features.Manage);
        token.Claims.Should().NotContain(claim => claim.Type == CustomClaimTypes.TenantId);
    }
}
