using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Products;

internal sealed class ProductImageCleanupQueue(ApplicationDbContext context, IOptions<ProductImageStorageOptions> options) : IProductImageCleanupScheduler
{
    public int RetentionDays => Math.Clamp(options.Value.DeletionRetentionDays, 1, 3650);

    public async Task ScheduleProductAsync(Guid tenantId, Guid productId, string requestedBy, DateTimeOffset requestedAtUtc, CancellationToken cancellationToken)
    {
        var externalJob = await context.ProductImageCleanupJobs.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProductId == productId && x.Source == "PRODUCT_DELETE" && x.ProductImageId == null, cancellationToken);
        if (externalJob is null) context.ProductImageCleanupJobs.Add(new ProductImageCleanupJob { TenantId = tenantId, ProductId = productId, ProductImageId = null, Source = "PRODUCT_DELETE", RequestedBy = requestedBy, RequestedAtUtc = requestedAtUtc, DeleteAfterUtc = requestedAtUtc.AddDays(RetentionDays), Status = "PENDING", NextAttemptAtUtc = requestedAtUtc });
        else { externalJob.RequestedBy = requestedBy; externalJob.RequestedAtUtc = requestedAtUtc; externalJob.DeleteAfterUtc = requestedAtUtc.AddDays(RetentionDays); externalJob.Status = "PENDING"; externalJob.AttemptCount = 0; externalJob.NextAttemptAtUtc = requestedAtUtc; externalJob.CompletedAtUtc = null; externalJob.LeaseUntilUtc = null; externalJob.LastError = null; }
        var images = await context.ProductImages.Where(x => x.TenantId == tenantId && x.ProductId == productId && !x.IsDeleted && x.IsActive).ToArrayAsync(cancellationToken);
        foreach (var image in images)
        {
            var existing = await context.ProductImageCleanupJobs.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProductImageId == image.ProductImageId, cancellationToken);
            if (existing is null) context.ProductImageCleanupJobs.Add(NewJob(image, "PRODUCT_DELETE", requestedBy, requestedAtUtc));
            else if (existing.Status is "CANCELLED" or "FAILED") Reset(existing, image, "PRODUCT_DELETE", requestedBy, requestedAtUtc);
            // A separately requested image deletion already owns its own cleanup schedule.
        }
    }

    public async Task ScheduleImageAsync(ProductImage image, string requestedBy, DateTimeOffset requestedAtUtc, CancellationToken cancellationToken)
    {
        var existing = await context.ProductImageCleanupJobs.SingleOrDefaultAsync(x => x.TenantId == image.TenantId && x.ProductImageId == image.ProductImageId, cancellationToken);
        if (existing is null) context.ProductImageCleanupJobs.Add(NewJob(image, "IMAGE_DELETE", requestedBy, requestedAtUtc));
        else if (existing.Status is "CANCELLED" or "FAILED") Reset(existing, image, "IMAGE_DELETE", requestedBy, requestedAtUtc);
        else if (existing.Status == "PENDING") Reset(existing, image, "IMAGE_DELETE", requestedBy, requestedAtUtc);
    }

    public async Task<bool> CancelProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken)
    {
        var jobs = await context.ProductImageCleanupJobs.Where(x => x.TenantId == tenantId && x.ProductId == productId && x.Source == "PRODUCT_DELETE").ToArrayAsync(cancellationToken);
        var productJob = jobs.SingleOrDefault(x => x.ProductImageId == null);
        if (productJob is null || productJob.DeleteAfterUtc <= DateTimeOffset.UtcNow) return false;
        if (jobs.Any(x => x.Status is "PROCESSING" or "COMPLETED")) return false;
        foreach (var job in jobs.Where(x => x.Status == "PENDING")) { job.Status = "CANCELLED"; job.NextAttemptAtUtc = null; job.LeaseUntilUtc = null; job.LeaseToken = null; job.LastError = "Cancelled because the product was restored during its retention period."; }
        return true;
    }

    private ProductImageCleanupJob NewJob(ProductImage image, string source, string user, DateTimeOffset requestedAt) => new()
    {
        TenantId = image.TenantId, ProductId = image.ProductId, ProductImageId = image.ProductImageId,
        Source = source, RequestedBy = user, RequestedAtUtc = requestedAt,
        DeleteAfterUtc = requestedAt.AddDays(RetentionDays), Status = "PENDING", NextAttemptAtUtc = requestedAt,
        StorageProvider = image.StorageProvider, ObjectKey = image.ObjectKey, ThumbnailObjectKey = image.ThumbnailObjectKey, ContentHash = image.ContentHash
    };

    private void Reset(ProductImageCleanupJob job, ProductImage image, string source, string user, DateTimeOffset requestedAt)
    {
        job.Source = source; job.RequestedBy = user; job.RequestedAtUtc = requestedAt; job.DeleteAfterUtc = requestedAt.AddDays(RetentionDays);
        job.Status = "PENDING"; job.AttemptCount = 0; job.NextAttemptAtUtc = requestedAt; job.CompletedAtUtc = null; job.LeaseUntilUtc = null; job.LeaseToken = null; job.LastError = null;
        job.StorageProvider = image.StorageProvider; job.ObjectKey = image.ObjectKey; job.ThumbnailObjectKey = image.ThumbnailObjectKey; job.ContentHash = image.ContentHash;
    }
}

