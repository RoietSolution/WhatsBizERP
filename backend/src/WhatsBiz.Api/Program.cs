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
using WhatsBiz.Infrastructure.Notifications;
using WhatsBiz.Infrastructure.Administration;
using WhatsBiz.Infrastructure.Products;
using Microsoft.AspNetCore.HttpOverrides;
using WhatsBiz.Infrastructure.Features;
using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.Infrastructure.Analytics;
using WhatsBiz.Infrastructure.Loyalty;
using WhatsBiz.Application.Features.Loyalty;
using WhatsBiz.Application.Features.Referrals;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.Infrastructure.Delivery;
using WhatsBiz.Api.Configuration;
using Serilog.Events;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        var fileOptions = context.Configuration
            .GetSection(LocalFileLoggingOptions.SectionName)
            .Get<LocalFileLoggingOptions>() ?? new LocalFileLoggingOptions();
        if (!fileOptions.Enabled) return;

        try
        {
            var logPath = Path.IsPathRooted(fileOptions.Path)
                ? Path.GetFullPath(fileOptions.Path)
                : Path.GetFullPath(fileOptions.Path, context.HostingEnvironment.ContentRootPath);
            var logDirectory = Path.GetDirectoryName(logPath)
                ?? throw new InvalidOperationException("The local log path must include a directory.");
            Directory.CreateDirectory(logDirectory);

            configuration.WriteTo.File(
                logPath,
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] [Trace:{TraceId}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: Math.Max(fileOptions.FileSizeLimitBytes, 1024 * 1024),
                rollOnFileSizeLimit: true,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Clamp(fileOptions.RetainedFileCountLimit, 1, 365),
                shared: true);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Local file logging is unavailable for '{fileOptions.Path}': {exception.Message}"));
        }
    });
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddSingleton<IProductImageOptimizer, ProductImageOptimizer>();
    builder.Services.AddProductImageStorage(builder.Configuration);
    builder.Services.AddWhatsAppIntegration(builder.Configuration);
    builder.Services.AddSingleton<IProductMasterSpreadsheetService, ProductMasterSpreadsheetService>();
    builder.Services.AddCustomerNotifications(builder.Configuration);
    builder.Services.AddScoped<IInventoryOperationsRepository, InventoryOperationsRepository>();
    builder.Services.AddScoped<IReceivablesRepository, ReceivablesRepository>();
    builder.Services.AddMemoryCache(options => options.SizeLimit = 1024);
    builder.Services.AddOptions<GlobalFeatureOptions>().Bind(builder.Configuration.GetSection(GlobalFeatureOptions.SectionName));
    builder.Services.AddScoped<IFeatureService, FeatureService>();
    builder.Services.AddScoped<IAuthorizationHandler, FeatureAuthorizationHandler>();
    builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
    builder.Services.AddScoped<IGstRepository, GstRepository>();
    builder.Services.AddSingleton<IGstExportService, GstExportService>();
    builder.Services.AddScoped<IPrintRepository, PrintRepository>();
    builder.Services.AddSingleton<IPrintingService, PrintingService>();
    builder.Services.AddScoped<IAdminRepository, AdminRepository>();
    builder.Services.AddScoped<ICommerceCollectionRepository, CommerceCollectionRepository>();
    builder.Services.AddScoped<ICustomerGroupRepository, CustomerGroupRepository>();
    builder.Services.AddScoped<ICommerceAnalyticsService, CommerceAnalyticsService>();
    builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
    builder.Services.AddScoped<ICustomerReferralService, CustomerReferralService>();
    builder.Services.AddScoped<IDeliveryService, DeliveryService>();
    builder.Services.AddHostedService<RewardCoinExpirationWorker>();
    builder.Services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
    builder.Services.AddApiServices(builder.Configuration);
    var app = builder.Build();
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedHeaders.KnownProxies.Clear();
    forwardedHeaders.KnownNetworks.Clear();
    forwardedHeaders.KnownProxies.Add(System.Net.IPAddress.Loopback);
    forwardedHeaders.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
    app.UseForwardedHeaders(forwardedHeaders);
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseMiddleware<AuditMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseResponseCompression();
    app.UseAuthentication();
    app.UseMiddleware<FeatureGateMiddleware>();
    app.UseRateLimiter();
    app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
    if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();
    await app.RunAsync();
}
catch (Exception exception) { Log.Fatal(exception, "WhatsBiz API terminated unexpectedly"); }
finally { await Log.CloseAndFlushAsync(); }

public partial class Program;
