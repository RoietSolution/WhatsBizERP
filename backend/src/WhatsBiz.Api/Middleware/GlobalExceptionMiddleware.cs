using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using UnauthorizedAccessException = System.UnauthorizedAccessException;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace WhatsBiz.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly Action<ILogger, string, Exception?> ValidationFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1001, nameof(ValidationFailed)), "Validation failed for {Path}");
    private static readonly Action<ILogger, string, Exception?> UnhandledException = LoggerMessage.Define<string>(LogLevel.Error, new EventId(1002, nameof(UnhandledException)), "Unhandled exception for {Path}");

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100) correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Browsers may cancel queued image requests while the demo is loading or navigating.
            // The response can no longer be written, so this is not an application failure.
        }
        catch (ValidationException exception)
        {
            ValidationFailed(logger, context.Request.Path, exception);
            LogRequest(context, started, 400, exception);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed", exception.Errors.Select(error => error.ErrorMessage));
        }
        catch (UnauthorizedAccessException exception)
        {
            LogRequest(context, started, 401, exception); await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Authentication failed", [exception.Message]);
        }
        catch (EntityNotFoundException exception)
        {
            LogRequest(context, started, 404, exception); await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Resource not found", [exception.Message]);
        }
        catch (BusinessRuleException exception)
        {
            LogRequest(context, started, 409, exception); await WriteProblemAsync(context, StatusCodes.Status409Conflict, "Business rule violation", [exception.Message]);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogRequest(context, started, 409, exception); await WriteProblemAsync(context, StatusCodes.Status409Conflict, "The record was changed by another user", [exception.Message]);
        }
        catch (DbUpdateException exception)
        {
            LogRequest(context, started, 409, exception);
            var detail = IsUniqueConstraint(exception)
                ? "A record with the same unique value already exists. Check the supplier code or GSTIN and try again."
                : context.Request.Path.StartsWithSegments("/api/suppliers")
                    ? "The supplier could not be saved because its data conflicts with the current database. Verify the supplier details and try again."
                    : "The record could not be saved because its data conflicts with the current database.";
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "Unable to save record", [detail]);
        }
        catch (Exception exception)
        {
            UnhandledException(logger, context.Request.Path, exception);
            LogRequest(context, started, 500, exception);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Unable to complete request", ["The server could not complete this request. Please try again. If the problem continues, contact your administrator."]);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, IEnumerable<string>? errors)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = errors is null ? title : string.Join("; ", errors) });
    }

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }

    private void LogRequest(HttpContext context, long started, int status, Exception exception)
    {
#pragma warning disable CA1848
        logger.LogError(exception, "API failure {Method} {Path} {StatusCode} TraceId={TraceId} TenantId={TenantId} UserId={UserId} DurationMs={DurationMs} ExceptionType={ExceptionType}", context.Request.Method, context.Request.Path, status, context.TraceIdentifier, context.User.FindFirst("tenant_id")?.Value, context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().Name);
#pragma warning restore CA1848
    }
}
