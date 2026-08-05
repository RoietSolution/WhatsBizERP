using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users", "core");
            entity.Property(x => x.CreatedBy).HasMaxLength(256);
            entity.Property(x => x.ModifiedBy).HasMaxLength(256);
            entity.Property(x => x.RowVersion).IsRowVersion();
        });
        builder.Entity<ApplicationRole>().ToTable("Roles", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens", "core");

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "core");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.Property(x => x.CreatedBy).HasMaxLength(256);
            entity.Property(x => x.ModifiedBy).HasMaxLength(256);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
