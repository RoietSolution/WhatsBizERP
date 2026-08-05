using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Authentication;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task HandlerSucceedsWhenPermissionClaimMatches()
    {
        var requirement = new PermissionRequirement(Permissions.Product.View);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomClaimTypes.Permission, Permissions.Product.View)], "test"));
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandlerDoesNotSucceedWithoutPermissionClaim()
    {
        var requirement = new PermissionRequirement(Permissions.Product.Delete);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity("test")), null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
