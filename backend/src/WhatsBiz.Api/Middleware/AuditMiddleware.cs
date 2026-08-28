using Microsoft.Data.SqlClient;

namespace WhatsBiz.Api.Middleware;

public sealed partial class AuditMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<AuditMiddleware> logger)
{
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
            await using var command = new SqlCommand("INSERT admin.AuditLogs(UserName,Action,EntityType,RequestPath,HttpMethod,IpAddress,Succeeded) VALUES(@user,@action,@entity,@path,@method,@ip,@ok)", connection);
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

    [LoggerMessage(1101, LogLevel.Error, "Audit log persistence failed for {Method} {Path}.")]
    private static partial void AuditWriteFailed(ILogger logger, string method, string path, Exception exception);
}
