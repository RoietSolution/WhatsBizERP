using WhatsBiz.Api.Authorization;
using WhatsBiz.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace WhatsBiz.Api.Middleware;

public sealed class TenantContextAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/api/auth") ||
            context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var platformOperation = context.GetEndpoint()?.Metadata.GetMetadata<PlatformAuthorizeAttribute>() is not null;
        var applicationOwner = context.User.IsInRole("ApplicationOwner");
        var hasTenant = context.User.HasClaim(claim =>
            claim.Type == CustomClaimTypes.TenantId && Guid.TryParse(claim.Value, out _));

        if ((platformOperation && !applicationOwner) ||
            (!platformOperation && (applicationOwner || !hasTenant)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Account scope is not valid for this operation.",
                Detail = platformOperation
                    ? "This operation requires an application-owner account."
                    : "This operation requires a retailer tenant context."
            });
            return;
        }

        await next(context);
    }
}
