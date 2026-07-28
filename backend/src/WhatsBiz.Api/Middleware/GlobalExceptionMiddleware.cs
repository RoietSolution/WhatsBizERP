using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace WhatsBiz.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly Action<ILogger, string, Exception?> ValidationFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1001, nameof(ValidationFailed)), "Validation failed for {Path}");
    private static readonly Action<ILogger, string, Exception?> UnhandledException = LoggerMessage.Define<string>(LogLevel.Error, new EventId(1002, nameof(UnhandledException)), "Unhandled exception for {Path}");

    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (ValidationException exception)
        {
            ValidationFailed(logger, context.Request.Path, exception);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed", exception.Errors.Select(error => error.ErrorMessage));
        }
        catch (Exception exception)
        {
            UnhandledException(logger, context.Request.Path, exception);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred", null);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, IEnumerable<string>? errors)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = errors is null ? null : string.Join("; ", errors) });
    }
}
