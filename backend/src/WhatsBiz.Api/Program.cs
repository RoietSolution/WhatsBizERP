using Serilog;
using System.Globalization;
using WhatsBiz.Api.Extensions;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Gst;
using WhatsBiz.Infrastructure.Printing;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext());
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddScoped<IInventoryOperationsRepository, InventoryOperationsRepository>();
    builder.Services.AddScoped<IReceivablesRepository, ReceivablesRepository>();
    builder.Services.AddMemoryCache(options => options.SizeLimit = 1024);
    builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
    builder.Services.AddScoped<IGstRepository, GstRepository>();
    builder.Services.AddSingleton<IGstExportService, GstExportService>();
    builder.Services.AddScoped<IPrintRepository, PrintRepository>();
    builder.Services.AddSingleton<IPrintingService, PrintingService>();
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
