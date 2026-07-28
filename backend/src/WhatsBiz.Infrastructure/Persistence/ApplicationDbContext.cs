using Microsoft.EntityFrameworkCore;
using WhatsBiz.Domain.Common;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified) { entry.Entity.SetAuditValues(timestamp, null, entry.State == EntityState.Added); }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}
