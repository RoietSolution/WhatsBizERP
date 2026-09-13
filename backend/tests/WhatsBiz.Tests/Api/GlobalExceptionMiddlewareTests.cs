using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Api.Middleware;

namespace WhatsBiz.Tests.Api;

public sealed class GlobalExceptionMiddlewareTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task UnexpectedErrorsReturnHelpfulMessageWithoutExposingReferenceOrException()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/suppliers";
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("sensitive database detail"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Headers.Should().ContainKey("X-Correlation-ID");
        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body, JsonOptions);
        problem!.Detail.Should().Be("The server could not complete this request. Please try again. If the problem continues, contact your administrator.")
            .And.NotContain("Reference ID")
            .And.NotContain("sensitive database detail");
    }
}
