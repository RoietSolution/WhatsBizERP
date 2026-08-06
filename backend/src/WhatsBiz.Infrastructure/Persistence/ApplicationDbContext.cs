using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Suppliers;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.Warehouses;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<ProductTaxMapping> ProductTaxMappings => Set<ProductTaxMapping>();
    public DbSet<Supplier> Suppliers => Set<Supplier>(); public DbSet<SupplierContact> SupplierContacts=>Set<SupplierContact>(); public DbSet<SupplierAddress> SupplierAddresses=>Set<SupplierAddress>(); public DbSet<SupplierBankAccount> SupplierBankAccounts=>Set<SupplierBankAccount>(); public DbSet<SupplierDocument> SupplierDocuments=>Set<SupplierDocument>(); public DbSet<SupplierPaymentTerm> SupplierPaymentTerms=>Set<SupplierPaymentTerm>(); public DbSet<Customer> Customers=>Set<Customer>();public DbSet<CustomerContact> CustomerContacts=>Set<CustomerContact>();public DbSet<CustomerAddress> CustomerAddresses=>Set<CustomerAddress>();public DbSet<CustomerBankAccount> CustomerBankAccounts=>Set<CustomerBankAccount>();public DbSet<CustomerDocument> CustomerDocuments=>Set<CustomerDocument>();public DbSet<CustomerPaymentTerm> CustomerPaymentTerms=>Set<CustomerPaymentTerm>(); public DbSet<Warehouse> Warehouses=>Set<Warehouse>();public DbSet<WarehouseType> WarehouseTypes=>Set<WarehouseType>();public DbSet<WarehouseAddress> WarehouseAddresses=>Set<WarehouseAddress>();public DbSet<WarehouseContact> WarehouseContacts=>Set<WarehouseContact>();public DbSet<WarehouseZone> WarehouseZones=>Set<WarehouseZone>();public DbSet<WarehouseBin> WarehouseBins=>Set<WarehouseBin>();

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

        ConfigureProductMaster(builder);
        ConfigureSuppliers(builder);
        ConfigureCustomers(builder);
        ConfigureWarehouses(builder);
    }

    private static void ConfigureWarehouses(ModelBuilder b){b.Entity<WarehouseType>(e=>{e.ToTable("WarehouseTypes","inventory");e.HasKey(x=>x.WarehouseTypeId);e.Property(x=>x.TypeCode).HasMaxLength(30);e.Property(x=>x.TypeName).HasMaxLength(100);e.Property(x=>x.RowVersion).IsRowVersion();});b.Entity<Warehouse>(e=>{e.ToTable("Warehouses","inventory");e.HasKey(x=>x.WarehouseId);e.Property(x=>x.WarehouseCode).HasMaxLength(50);e.Property(x=>x.WarehouseName).HasMaxLength(200);e.Property(x=>x.Email).HasMaxLength(256);e.Property(x=>x.Capacity).HasPrecision(18,4);e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne(x=>x.WarehouseType).WithMany().HasForeignKey(x=>x.WarehouseTypeId).OnDelete(DeleteBehavior.Restrict);e.HasOne(x=>x.Address).WithOne().HasForeignKey<WarehouseAddress>(x=>x.WarehouseId).OnDelete(DeleteBehavior.Cascade);});b.Entity<WarehouseAddress>(e=>{e.ToTable("WarehouseAddresses","inventory");e.HasKey(x=>x.AddressId);});b.Entity<WarehouseContact>(e=>{e.ToTable("WarehouseContacts","inventory");e.HasKey(x=>x.ContactId);e.HasOne<Warehouse>().WithMany(x=>x.Contacts).HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Cascade);});b.Entity<WarehouseZone>(e=>{e.ToTable("WarehouseZones","inventory");e.HasKey(x=>x.ZoneId);e.HasOne<Warehouse>().WithMany(x=>x.Zones).HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Cascade);});b.Entity<WarehouseBin>(e=>{e.ToTable("WarehouseBins","inventory");e.HasKey(x=>x.BinId);e.Property(x=>x.MaximumCapacity).HasPrecision(18,4);e.HasOne<WarehouseZone>().WithMany(x=>x.Bins).HasForeignKey(x=>x.ZoneId).OnDelete(DeleteBehavior.Cascade);});}

    private static void ConfigureCustomers(ModelBuilder b){b.Entity<CustomerPaymentTerm>(e=>{e.ToTable("CustomerPaymentTerms","sales");e.HasKey(x=>x.PaymentTermId);e.Property(x=>x.RowVersion).IsRowVersion();});b.Entity<Customer>(e=>{e.ToTable("Customers","sales");e.HasKey(x=>x.CustomerId);e.Property(x=>x.CustomerCode).HasMaxLength(50);e.Property(x=>x.CustomerName).HasMaxLength(250);e.Property(x=>x.GSTIN).HasMaxLength(15);e.Property(x=>x.PAN).HasMaxLength(10);e.Property(x=>x.Currency).HasMaxLength(3).IsFixedLength();e.Property(x=>x.CreditLimit).HasPrecision(18,2);e.Property(x=>x.OpeningBalance).HasPrecision(18,2);e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne(x=>x.PaymentTerm).WithMany().HasForeignKey(x=>x.PaymentTermId).OnDelete(DeleteBehavior.Restrict);});b.Entity<CustomerContact>(e=>{e.ToTable("CustomerContacts","sales");e.HasKey(x=>x.ContactId);e.HasOne<Customer>().WithMany(x=>x.Contacts).HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Cascade);});b.Entity<CustomerAddress>(e=>{e.ToTable("CustomerAddresses","sales");e.HasKey(x=>x.AddressId);e.HasOne<Customer>().WithMany(x=>x.Addresses).HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Cascade);});b.Entity<CustomerBankAccount>(e=>{e.ToTable("CustomerBankAccounts","sales");e.HasKey(x=>x.BankAccountId);e.HasOne<Customer>().WithMany(x=>x.BankAccounts).HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Cascade);});b.Entity<CustomerDocument>(e=>{e.ToTable("CustomerDocuments","sales");e.HasKey(x=>x.DocumentId);e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne<Customer>().WithMany(x=>x.Documents).HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Cascade);});}

    private static void ConfigureSuppliers(ModelBuilder b)
    {
        b.Entity<SupplierPaymentTerm>(e=>{e.ToTable("SupplierPaymentTerms","purchase");e.HasKey(x=>x.PaymentTermId);e.Property(x=>x.PaymentTermCode).HasMaxLength(30);e.Property(x=>x.PaymentTermName).HasMaxLength(100);e.Property(x=>x.RowVersion).IsRowVersion();});
        b.Entity<Supplier>(e=>{e.ToTable("Suppliers","purchase");e.HasKey(x=>x.SupplierId);e.Property(x=>x.SupplierCode).HasMaxLength(50);e.Property(x=>x.SupplierName).HasMaxLength(250);e.Property(x=>x.SupplierType).HasMaxLength(50);e.Property(x=>x.GSTIN).HasMaxLength(15);e.Property(x=>x.PAN).HasMaxLength(10);e.Property(x=>x.Email).HasMaxLength(256);e.Property(x=>x.Mobile).HasMaxLength(15);e.Property(x=>x.Currency).HasMaxLength(3).IsFixedLength();e.Property(x=>x.CreditLimit).HasPrecision(18,2);e.Property(x=>x.OpeningBalance).HasPrecision(18,2);e.Property(x=>x.RowVersion).IsRowVersion();e.HasIndex(x=>x.SupplierCode).IsUnique().HasFilter("[IsDeleted] = 0");e.HasIndex(x=>x.GSTIN).IsUnique().HasFilter("[GSTIN] IS NOT NULL AND [IsDeleted] = 0");e.HasOne(x=>x.PaymentTerm).WithMany().HasForeignKey(x=>x.PaymentTermId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<SupplierContact>(e=>{e.ToTable("SupplierContacts","purchase");e.HasKey(x=>x.ContactId);e.HasOne<Supplier>().WithMany(x=>x.Contacts).HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Cascade);}); b.Entity<SupplierAddress>(e=>{e.ToTable("SupplierAddresses","purchase");e.HasKey(x=>x.AddressId);e.HasOne<Supplier>().WithMany(x=>x.Addresses).HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Cascade);}); b.Entity<SupplierBankAccount>(e=>{e.ToTable("SupplierBankAccounts","purchase");e.HasKey(x=>x.BankAccountId);e.HasOne<Supplier>().WithMany(x=>x.BankAccounts).HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Cascade);}); b.Entity<SupplierDocument>(e=>{e.ToTable("SupplierDocuments","purchase");e.HasKey(x=>x.DocumentId);e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne<Supplier>().WithMany(x=>x.Documents).HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Cascade);});
    }

    private static void ConfigureProductMaster(ModelBuilder builder)
    {
        builder.Entity<ProductCategory>(entity => { ConfigureAudit(entity); entity.ToTable("ProductCategories", "master"); entity.HasKey(x => x.ProductCategoryId); entity.Property(x => x.CategoryCode).HasMaxLength(50); entity.Property(x => x.CategoryName).HasMaxLength(200); entity.Property(x => x.Description).HasMaxLength(1000); entity.HasIndex(x => x.CategoryCode).IsUnique().HasFilter("[IsDeleted] = 0"); entity.HasOne(x => x.ParentCategory).WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<Brand>(entity => { ConfigureAudit(entity); entity.ToTable("Brands", "master"); entity.HasKey(x => x.BrandId); entity.Property(x => x.BrandCode).HasMaxLength(50); entity.Property(x => x.BrandName).HasMaxLength(200); entity.Property(x => x.Description).HasMaxLength(1000); entity.Property(x => x.Logo).HasMaxLength(500); entity.HasIndex(x => x.BrandCode).IsUnique().HasFilter("[IsDeleted] = 0"); });
        builder.Entity<UnitOfMeasure>(entity => { ConfigureAudit(entity); entity.ToTable("UnitsOfMeasure", "master"); entity.HasKey(x => x.UnitId); entity.Property(x => x.UnitCode).HasMaxLength(50); entity.Property(x => x.UnitName).HasMaxLength(200); entity.Property(x => x.ShortName).HasMaxLength(20); entity.HasIndex(x => x.UnitCode).IsUnique().HasFilter("[IsDeleted] = 0"); });
        builder.Entity<Product>(entity => { ConfigureAudit(entity); entity.ToTable("Products", "master"); entity.HasKey(x => x.ProductId); entity.Property(x => x.ProductCode).HasMaxLength(50); entity.Property(x => x.Barcode).HasMaxLength(100); entity.Property(x => x.ProductName).HasMaxLength(250); entity.Property(x => x.ShortDescription).HasMaxLength(500); entity.Property(x => x.HSNCode).HasMaxLength(20); entity.Property(x => x.SACCode).HasMaxLength(20); entity.Property(x => x.ImageUrl).HasMaxLength(500); foreach (var property in new[] { nameof(Product.PurchasePrice), nameof(Product.SellingPrice), nameof(Product.MRP), nameof(Product.MinimumStock), nameof(Product.MaximumStock), nameof(Product.ReorderLevel), nameof(Product.Weight), nameof(Product.Length), nameof(Product.Width), nameof(Product.Height) }) entity.Property(property).HasPrecision(18, 4); entity.Property(x => x.GSTPercentage).HasPrecision(5, 2); entity.HasIndex(x => x.ProductCode).IsUnique().HasFilter("[IsDeleted] = 0"); entity.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0"); entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ProductImage>(entity => { ConfigureAudit(entity); entity.ToTable("ProductImages", "master"); entity.HasKey(x => x.ProductImageId); entity.Property(x => x.FileName).HasMaxLength(255); entity.Property(x => x.ContentType).HasMaxLength(100); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductBarcode>(entity => { ConfigureAudit(entity); entity.ToTable("ProductBarcodes", "master"); entity.HasKey(x => x.ProductBarcodeId); entity.Property(x => x.Barcode).HasMaxLength(100); entity.HasIndex(x => x.Barcode).IsUnique().HasFilter("[IsDeleted] = 0"); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductPrice>(entity => { ConfigureAudit(entity); entity.ToTable("ProductPrices", "master"); entity.HasKey(x => x.ProductPriceId); entity.Property(x => x.PriceType).HasMaxLength(50); entity.Property(x => x.Amount).HasPrecision(18, 4); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductTaxMapping>(entity => { ConfigureAudit(entity); entity.ToTable("ProductTaxMappings", "master"); entity.HasKey(x => x.ProductTaxMappingId); entity.Property(x => x.TaxCode).HasMaxLength(50); entity.Property(x => x.TaxPercentage).HasPrecision(5, 2); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
    }

    private static void ConfigureAudit<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity) where TEntity : ProductMasterEntity
    {
        entity.Property(x => x.CreatedBy).HasMaxLength(256);
        entity.Property(x => x.ModifiedBy).HasMaxLength(256);
        entity.Property(x => x.RowVersion).IsRowVersion();
    }
}
