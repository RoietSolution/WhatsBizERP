using Serilog;
using System.Globalization;
using WhatsBiz.Api.Extensions;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application;
using WhatsBiz.Infrastructure;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext());
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    var app = builder.Build();
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();
    await app.RunAsync();
}
catch (Exception exception) { Log.Fatal(exception, "WhatsBiz API terminated unexpectedly"); }
finally { await Log.CloseAndFlushAsync(); }

public partial class Program;
