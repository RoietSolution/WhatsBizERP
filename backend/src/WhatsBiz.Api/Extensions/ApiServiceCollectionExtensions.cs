using Microsoft.AspNetCore.Authentication.JwtBearer; using Microsoft.AspNetCore.Authorization; using Microsoft.IdentityModel.Tokens; using Microsoft.OpenApi.Models; using System.Text; using WhatsBiz.Api.Authorization; using WhatsBiz.Infrastructure.Identity;
namespace WhatsBiz.Api.Extensions;
public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicyName = "DefaultCorsPolicy";
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetRequiredSection(JwtOptions.SectionName).Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is required.");
        services.AddControllers(); services.AddEndpointsApiExplorer(); services.AddSwaggerGen(options => { options.SwaggerDoc("v1", new OpenApiInfo { Title = "WhatsBiz ERP API", Version = "v1" }); options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header }); options.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, [] } }); });
        services.AddHealthChecks(); services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), ClockSkew = TimeSpan.FromMinutes(1) });
        services.AddAuthorization(); services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>(); services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>(); return services;
    }
}
