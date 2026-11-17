using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace WhatsBiz.Tests.Configuration;

public sealed class SwaggerDocumentGenerationTests
{
    [Fact]
    public void ApiSwaggerDocumentCanBeGenerated()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "WhatsBiz ERP API", Version = "v1" });
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);
        });
        using var app = builder.Build();
        var swagger = app.Services.GetRequiredService<ISwaggerProvider>();
        Assert.NotEmpty(swagger.GetSwagger("v1").Paths);
    }
}
