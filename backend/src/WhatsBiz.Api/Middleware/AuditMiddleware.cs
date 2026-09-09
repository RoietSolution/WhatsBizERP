using Microsoft.Data.SqlClient;
using System.Security.Claims;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Api.Middleware;

public sealed partial class AuditMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<AuditMiddleware> logger)
{
    public const string TargetTenantItemKey = "PlatformAudit.TargetTenantId";

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE")
            && !path.Contains("/print", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/export", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new SqlCommand("INSERT admin.AuditLogs(UserId,UserName,TargetTenantId,Action,EntityType,RequestPath,HttpMethod,IpAddress,Succeeded) VALUES(@userId,@user,@targetTenantId,@action,@entity,@path,@method,@ip,@ok)", connection);
            var action = path.Contains("print", StringComparison.OrdinalIgnoreCase)
                ? "PRINT"
                : path.Contains("export", StringComparison.OrdinalIgnoreCase)
                    ? "EXPORT"
                    : method switch
                    {
                        "POST" => "CREATE",
                        "PUT" or "PATCH" => "UPDATE",
                        "DELETE" => "DELETE",
                        _ => method
                    };
            command.Parameters.AddWithValue("@user", (object?)context.User.Identity?.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("@userId", Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : DBNull.Value);
            command.Parameters.AddWithValue("@targetTenantId", (object?)ResolveTargetTenant(context) ?? DBNull.Value);
            command.Parameters.AddWithValue("@action", action);
            command.Parameters.AddWithValue("@entity", path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty);
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@method", method);
            command.Parameters.AddWithValue("@ip", (object?)context.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("@ok", context.Response.StatusCode < 400);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The request ended before its audit record could be persisted.
        }
        catch (Exception exception)
        {
            AuditWriteFailed(logger, method, path, exception);
        }
    }

    private static Guid? ResolveTargetTenant(HttpContext context)
    {
        if (context.Items.TryGetValue(TargetTenantItemKey, out var itemTenant) && itemTenant is Guid auditedTenantId)
            return auditedTenantId;
        if (context.Request.RouteValues.TryGetValue("tenantId", out var routeTenant) &&
            Guid.TryParse(routeTenant?.ToString(), out var explicitTenantId)) return explicitTenantId;
        if (Guid.TryParse(context.User.FindFirstValue(CustomClaimTypes.TenantId), out var authenticatedTenantId)) return authenticatedTenantId;
        return null;
    }

    [LoggerMessage(1101, LogLevel.Error, "Audit log persistence failed for {Method} {Path}.")]
    private static partial void AuditWriteFailed(ILogger logger, string method, string path, Exception exception);
}
