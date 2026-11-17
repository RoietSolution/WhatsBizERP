using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Products;

internal sealed class ProductImageCleanupWorker(IServiceScopeFactory scopes, ILogger<ProductImageCleanupWorker> logger, IOptions<ProductImageCleanupOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Configuration is opt-in. Missing configuration binds the options default (false).
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                worked = await scope.ServiceProvider.GetRequiredService<ProductImageCleanupProcessor>().ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { ProductImageCleanupLogs.PassFailed(logger, exception.GetType().Name); }
            if (!worked) await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

internal sealed class ProductImageCleanupProcessor(ApplicationDbContext context, IProductImageStorage storage, ILogger<ProductImageCleanupProcessor> logger, IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    public async Task<bool> ProcessOneAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var job = await context.ProductImageCleanupJobs
            .Where(x => x.DeleteAfterUtc <= now && ((x.Status == "PENDING" && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)) || (x.Status == "PROCESSING" && x.LeaseUntilUtc < now)))
            .OrderBy(x => x.ProductImageId == null).ThenBy(x => x.DeleteAfterUtc).ThenBy(x => x.RequestedAtUtc).FirstOrDefaultAsync(token);
        if (job is null) return false;
        job.Status = "PROCESSING"; job.AttemptCount++; job.LeaseUntilUtc = now.Add(LeaseDuration); job.LeaseToken = Guid.NewGuid(); job.NextAttemptAtUtc = null;
        try { await SetTenantSessionAsync(job.TenantId, token); await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { context.Entry(job).State = EntityState.Detached; return true; }
        await using var lease = new ProductImageCleanupLease(scopeFactory, job.TenantId, job.JobId, job.LeaseToken!.Value, LeaseDuration, logger);
        using var ownedToken = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LostOwnershipToken);
        var workToken = ownedToken.Token;
        try
        {
            var product = await context.Products.SingleOrDefaultAsync(x => x.TenantId == job.TenantId && x.ProductId == job.ProductId, workToken);
            if (job.ProductImageId is null)
            {
                if (job.Source != "PRODUCT_DELETE" || product is not { IsDeleted: true } || product.DeletedOnUtc != job.RequestedAtUtc)
                { job.Status = "CANCELLED"; job.LastError = "Product reference cleanup cancelled because the product was restored or changed."; job.LeaseUntilUtc = null; job.LeaseToken = null; await lease.SaveOwnedChangesAsync(context, job, workToken); return true; }
                var incompleteImages = await context.ProductImageCleanupJobs.AnyAsync(x => x.TenantId == job.TenantId && x.ProductId == job.ProductId && x.ProductImageId != null && x.Status != "COMPLETED" && x.Status != "CANCELLED", workToken);
                if (incompleteImages) { job.Status = "PENDING"; job.AttemptCount = Math.Max(0, job.AttemptCount - 1); job.NextAttemptAtUtc = now.AddMinutes(5); job.LeaseUntilUtc = null; job.LeaseToken = null; await lease.SaveOwnedChangesAsync(context, job, workToken); return true; }
                product.ImageUrl = null;
                var externalReferences = await context.ProductExternalImages.Where(x => x.TenantId == job.TenantId && x.ProductId == job.ProductId).ToArrayAsync(workToken);
                context.ProductExternalImages.RemoveRange(externalReferences); // Remove database references only; never contact a CDN.
                Complete(job, now, null); await SetTenantSessionAsync(job.TenantId, workToken); await lease.SaveOwnedChangesAsync(context, job, workToken); return true;
            }
            var image = await context.ProductImages.SingleOrDefaultAsync(x => x.TenantId == job.TenantId && x.ProductId == job.ProductId && x.ProductImageId == job.ProductImageId.Value, workToken);
            if (image is null) { Complete(job, now, "Image metadata no longer exists; no storage key was trusted for deletion."); await lease.SaveOwnedChangesAsync(context, job, workToken); return true; }
            var validOwner = job.Source == "PRODUCT_DELETE"
                ? product is { IsDeleted: true } && product.DeletedOnUtc == job.RequestedAtUtc
                : job.Source == "IMAGE_DELETE" && image.IsDeleted;
            var unchanged = image.StorageProvider == job.StorageProvider && image.ObjectKey == job.ObjectKey && image.ThumbnailObjectKey == job.ThumbnailObjectKey && image.ContentHash == job.ContentHash;
            if (!validOwner || !unchanged)
            {
                job.Status = "CANCELLED"; job.LastError = "Cleanup cancelled because the product or image was restored or its managed media reference changed."; job.LeaseUntilUtc = null; job.LeaseToken = null;
                await lease.SaveOwnedChangesAsync(context, job, workToken); return true;
            }

            var catalogShared = await IsReferencedByOtherImage(job.ProductImageId.Value, job.ObjectKey, workToken);
            var thumbnailShared = await IsReferencedByOtherImage(job.ProductImageId.Value, job.ThumbnailObjectKey, workToken);
            string? catalog = catalogShared ? null : job.ObjectKey;
            string? thumbnail = thumbnailShared ? null : job.ThumbnailObjectKey;
            if (!job.StorageProvider.Equals(ProductImageStorageProviders.Database, StringComparison.OrdinalIgnoreCase))
            {
                if (!job.StorageProvider.Equals(ProductImageStorageProviders.Local, StringComparison.OrdinalIgnoreCase) && !job.StorageProvider.Equals(ProductImageStorageProviders.S3, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cleanup refused an unsupported or external storage provider.");
                if (catalog is not null) ProductImageStorage.ValidateManagedProductKey(job.TenantId, job.ProductId, job.ProductImageId.Value, catalog, "catalog.webp");
                if (thumbnail is not null) ProductImageStorage.ValidateManagedProductKey(job.TenantId, job.ProductId, job.ProductImageId.Value, thumbnail, "thumbnail.webp");
                if (catalog is not null || thumbnail is not null)
                    await storage.DeleteAsync(new(job.TenantId, job.ProductId, job.ProductImageId.Value, job.StorageProvider, catalog, thumbnail), workToken);
            }

            image.ImageData = []; image.ThumbnailData = []; image.IsDeleted = true; image.IsActive = false; image.ModifiedOn = now; image.ModifiedBy = "product-image-cleanup";
            job.LeaseToken = null; job.LeaseUntilUtc = null;
            Complete(job, now, catalogShared || thumbnailShared ? "Shared storage references were retained; exclusive managed objects were removed." : null);
            await SetTenantSessionAsync(job.TenantId, workToken);
            await lease.SaveOwnedChangesAsync(context, job, workToken);
            return true;
        }
        catch (OperationCanceledException) when (lease.LostOwnershipToken.IsCancellationRequested && !token.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            return true; // A newer lease owner is responsible for retry/finalization.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            var failedJob = await context.ProductImageCleanupJobs.SingleOrDefaultAsync(x => x.TenantId == job.TenantId && x.JobId == job.JobId, CancellationToken.None);
            if (failedJob is null || failedJob.LeaseToken != lease.LeaseToken) return true;
            failedJob.Status = failedJob.AttemptCount >= 10 ? "FAILED" : "PENDING";
            var delayMinutes = Math.Min(1440, Math.Pow(2, Math.Min(failedJob.AttemptCount, 10)));
            failedJob.NextAttemptAtUtc = failedJob.Status == "PENDING" ? DateTimeOffset.UtcNow.AddMinutes(delayMinutes) : null;
            failedJob.LeaseUntilUtc = null; failedJob.LeaseToken = null; failedJob.LastError = $"{exception.GetType().Name}; retry status: {failedJob.Status}. See application logs for diagnostic details.";
            await SetTenantSessionAsync(failedJob.TenantId, CancellationToken.None);
            try { await lease.SaveOwnedChangesAsync(context, failedJob, CancellationToken.None); }
            catch (ProductImageCleanupLeaseLostException) { return true; }
            ProductImageCleanupLogs.JobFailed(logger, exception.GetType().Name, failedJob.JobId, failedJob.AttemptCount, failedJob.Status);
            return true;
        }
    }

    private Task<bool> IsReferencedByOtherImage(Guid imageId, string? key, CancellationToken token) =>
        string.IsNullOrEmpty(key) ? Task.FromResult(false) : context.ProductImages.AsNoTracking().AnyAsync(x => x.ProductImageId != imageId && (x.ObjectKey == key || x.ThumbnailObjectKey == key), token);

    private static void Complete(WhatsBiz.Domain.Products.ProductImageCleanupJob job, DateTimeOffset now, string? note)
    { job.Status = "COMPLETED"; job.CompletedAtUtc = now; job.LeaseUntilUtc = null; job.LeaseToken = null; job.NextAttemptAtUtc = null; job.LastError = note; }

    private async Task SetTenantSessionAsync(Guid tenantId, CancellationToken token)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@tenant"; parameter.Value = tenantId; command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(token);
    }
}

internal sealed class ProductImageCleanupLease : IAsyncDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly Guid tenantId;
    private readonly Guid jobId;
    private readonly Guid leaseToken;
    private readonly TimeSpan leaseDuration;
    private readonly ILogger<ProductImageCleanupProcessor> logger;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource lost = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Task renewal;
    public Guid LeaseToken => leaseToken;
    public CancellationToken LostOwnershipToken => lost.Token;
    public ProductImageCleanupLease(IServiceScopeFactory scopes, Guid tenantId, Guid jobId, Guid leaseToken, TimeSpan leaseDuration, ILogger<ProductImageCleanupProcessor> logger)
    { this.scopes=scopes;this.tenantId=tenantId;this.jobId=jobId;this.leaseToken=leaseToken;this.leaseDuration=leaseDuration;this.logger=logger;renewal=RenewLoopAsync(); }

    public async Task SaveOwnedChangesAsync(ApplicationDbContext context, WhatsBiz.Domain.Products.ProductImageCleanupJob job, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var snapshot = await RenewCoreAsync(token);
            if (snapshot is null) throw new ProductImageCleanupLeaseLostException();
            context.Entry(job).Property(x => x.RowVersion).OriginalValue = snapshot.RowVersion;
            await context.SaveChangesAsync(token);
        }
        finally { gate.Release(); }
    }

    private async Task<LeaseSnapshot?> RenewCoreAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
        await using (var setTenant = connection.CreateCommand())
        {
            setTenant.CommandText = "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant";
            var tenant = setTenant.CreateParameter(); tenant.ParameterName = "@tenant"; tenant.Value = tenantId; setTenant.Parameters.Add(tenant);
            await setTenant.ExecuteNonQueryAsync(token);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE master.ProductImageCleanupJobs SET LeaseUntilUtc=DATEADD(SECOND,@leaseSeconds,SYSUTCDATETIME() AT TIME ZONE 'UTC') OUTPUT inserted.RowVersion,inserted.LeaseUntilUtc WHERE JobId=@job AND TenantId=@tenant AND Status=N'PROCESSING' AND LeaseToken=@lease AND LeaseUntilUtc>SYSUTCDATETIME() AT TIME ZONE 'UTC';";
        Add(command, "@leaseSeconds", (int)leaseDuration.TotalSeconds); Add(command, "@job", jobId); Add(command, "@tenant", tenantId); Add(command, "@lease", leaseToken);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    private async Task RenewLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                await gate.WaitAsync(stop.Token);
                try { if (await RenewCoreAsync(stop.Token) is null) { lost.Cancel(); return; } }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception) { ProductImageCleanupLogs.LeaseRenewalFailed(logger, exception.GetType().Name, jobId); lost.Cancel(); }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await renewal; } catch (OperationCanceledException) { }
        stop.Dispose(); lost.Dispose(); gate.Dispose();
    }

    private sealed record LeaseSnapshot(byte[] RowVersion, DateTimeOffset LeaseUntilUtc);
}

internal sealed class ProductImageCleanupLeaseLostException : Exception { }

internal static partial class ProductImageCleanupLogs
{
    [LoggerMessage(9310, LogLevel.Error, "Product image cleanup pass failed with error type {ErrorType}; durable jobs remain queued for retry.")]
    public static partial void PassFailed(ILogger logger, string errorType);
    [LoggerMessage(9311, LogLevel.Error, "Product image cleanup job {JobId} failed on attempt {AttemptCount}; status is {Status}; error type is {ErrorType}.")]
    public static partial void JobFailed(ILogger logger, string errorType, Guid jobId, int attemptCount, string status);
    [LoggerMessage(9312, LogLevel.Error, "Product image cleanup lease renewal failed for job {JobId}; error type is {ErrorType}. The worker has stopped acting on this lease.")]
    public static partial void LeaseRenewalFailed(ILogger logger, string errorType, Guid jobId);
}
