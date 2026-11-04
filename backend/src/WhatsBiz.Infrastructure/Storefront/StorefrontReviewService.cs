using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontReviewService(ApplicationDbContext db, IStorefrontCustomerService customers) : IStorefrontReviewService
{
    public async Task<StorefrontReviewSummaryDto?> GetAsync(string storeKey, Guid productId, string? sessionToken, CancellationToken token)
    {
        var tenant = await Tenant(storeKey, token);
        if (tenant is null || !await ProductAvailable(tenant.Value, productId, token)) return null;
        var settings = await Settings(tenant.Value, token);
        if (!settings.ShowReviews) return new(settings.ShowRatings ? await Average(tenant.Value, productId, token) : null, settings.ShowRatings ? await Count(tenant.Value, productId, token) : 0, []);
        var customer = string.IsNullOrWhiteSpace(sessionToken) ? null : await customers.GetSessionAsync(storeKey, sessionToken, token);
        var rows = await (from review in db.StorefrontProductReviews.AsNoTracking()
                          join c in db.Customers.AsNoTracking() on review.CustomerId equals c.CustomerId
                          where review.TenantId == tenant && review.ProductId == productId && review.Status == "PUBLISHED" && c.TenantId == tenant && c.IsActive && !c.IsDeleted
                          orderby review.UpdatedAt descending
                          select new StorefrontReviewDto(review.ReviewId, c.CustomerName, review.Rating, review.ReviewText, review.CreatedAt, review.UpdatedAt, customer != null && review.CustomerId == customer.Id))
                          .Take(100).ToArrayAsync(token);
        return new(settings.ShowRatings ? await Average(tenant.Value, productId, token) : null, settings.ShowRatings ? await Count(tenant.Value, productId, token) : 0, rows);
    }

    public async Task<StorefrontReviewDto?> UpsertAsync(string storeKey, Guid productId, string sessionToken, StorefrontReviewInput input, CancellationToken token)
    {
        if (input.Rating is < 1 or > 5) throw new BusinessRuleException("Rating must be between 1 and 5.");
        var text = input.ReviewText?.Trim() ?? string.Empty;
        if (text.Length is < 2 or > 1000) throw new BusinessRuleException("Review text must be between 2 and 1000 characters.");
        var tenant = await Tenant(storeKey, token);
        if (tenant is null || !await ProductAvailable(tenant.Value, productId, token)) throw new EntityNotFoundException("Product was not found.");
        var settings = await Settings(tenant.Value, token);
        if (!settings.ShowReviews) throw new BusinessRuleException("Product reviews are disabled for this store.");
        var customer = await customers.GetSessionAsync(storeKey, sessionToken, token);
        if (customer is null) return null;
        var purchased = await PurchasedAsync(tenant.Value, customer.Id, productId, token);
        if (!purchased) throw new BusinessRuleException("Only customers who purchased this product can review it.");
        var now = DateTimeOffset.UtcNow;
        var review = await db.StorefrontProductReviews.SingleOrDefaultAsync(x => x.TenantId == tenant && x.CustomerId == customer.Id && x.ProductId == productId, token);
        if (review is null)
        {
            review = new StorefrontProductReview { TenantId = tenant.Value, CustomerId = customer.Id, ProductId = productId, CreatedAt = now };
            db.StorefrontProductReviews.Add(review);
        }
        review.Rating = input.Rating; review.ReviewText = text; review.Status = "PUBLISHED"; review.UpdatedAt = now;
        await db.SaveChangesAsync(token);
        return new(review.ReviewId, customer.Name, review.Rating, review.ReviewText, review.CreatedAt, review.UpdatedAt, true);
    }

    private Task<Guid?> Tenant(string key, CancellationToken token) { var normalized = key?.Trim().ToUpperInvariant(); return db.Tenants.AsNoTracking().Where(x => x.TenantKey == normalized && x.IsActive).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(token); }
    private Task<bool> ProductAvailable(Guid tenant, Guid product, CancellationToken token) => db.Products.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.ProductId == product && x.IsActive && !x.IsDeleted && x.IsWhatsAppVisible, token);
    private async Task<bool> PurchasedAsync(Guid tenant, Guid customer, Guid product, CancellationToken token)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) 1
            FROM sales.SalesInvoices i
            JOIN sales.SalesInvoiceItems li ON li.InvoiceId=i.InvoiceId
            WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND li.ProductId=@product
              AND i.Status NOT IN(N'CANCELLED',N'VOID')
              AND (
                (i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') AND i.PaidAmount>=i.GrandTotal AND NOT EXISTS(
                    SELECT 1 FROM commerce.CommercePayments p
                    WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId AND p.Provider=N'COD')
                ) OR EXISTS(
                    SELECT 1
                    FROM commerce.CommercePayments p
                    WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId
                      AND p.Status IN(N'PAID',N'CAPTURED')
                      AND (p.Provider<>N'COD' OR EXISTS(
                          SELECT 1 FROM commerce.OrderDeliveries d
                          WHERE d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId AND d.DeliveryStatus=N'DELIVERED'))
                )
              );
            """;
        Add(command, "@tenant", tenant); Add(command, "@customer", customer); Add(command, "@product", product);
        return await command.ExecuteScalarAsync(token) is not null;
    }
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
    private async Task<(bool ShowRatings,bool ShowReviews)> Settings(Guid tenant, CancellationToken token) { var x = await db.StorefrontConfigurations.AsNoTracking().Where(c => c.TenantId == tenant).Select(c => new { c.ShowProductRatings, c.ShowProductReviews }).SingleOrDefaultAsync(token); return (x?.ShowProductRatings ?? true, x?.ShowProductReviews ?? true); }
    private async Task<decimal?> Average(Guid tenant, Guid product, CancellationToken token) { var value = await db.StorefrontProductReviews.AsNoTracking().Where(x => x.TenantId == tenant && x.ProductId == product && x.Status == "PUBLISHED").AverageAsync(x => (decimal?)x.Rating, token); return value.HasValue ? decimal.Round(value.Value, 1) : null; }
    private Task<int> Count(Guid tenant, Guid product, CancellationToken token) => db.StorefrontProductReviews.AsNoTracking().CountAsync(x => x.TenantId == tenant && x.ProductId == product && x.Status == "PUBLISHED", token);
}
