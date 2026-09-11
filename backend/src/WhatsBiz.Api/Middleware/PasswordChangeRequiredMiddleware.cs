using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Api.Middleware;

public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var passwordChangeRequired = context.User.Identity?.IsAuthenticated == true &&
            string.Equals(context.User.FindFirst(CustomClaimTypes.MustChangePassword)?.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
        if (!passwordChangeRequired || context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Password change required",
            Detail = "Change the temporary password before using the application."
        });
    }
}
