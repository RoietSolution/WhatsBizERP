using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Suppliers;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.Warehouses;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Domain.POS;
using WhatsBiz.Domain.Purchases;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserService? currentUser = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    private static readonly Type[] TenantOwnedTypes = [typeof(Supplier), typeof(Warehouse), typeof(SalesInvoice), typeof(PurchaseInvoice), typeof(ProductExternalImage), typeof(SupplierCatalogImportProfile), typeof(SupplierCatalogProduct), typeof(SupplierCatalogImportHistory)];
    public static bool IsProductPubliclyApproved(Guid tenantId, Guid productId) => throw new NotSupportedException("SQL translation only.");
    public static byte[] ProductPublicContentHash(Guid tenantId, Guid productId) => throw new NotSupportedException("SQL translation only.");

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var tenant = currentUser?.TenantId;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added && TenantOwnedTypes.Contains(e.Entity.GetType())))
        {
            if (!tenant.HasValue || tenant.Value == Guid.Empty)
                throw new UnauthorizedAccessException("Tenant context is required to create tenant-owned records.");
            entry.Property("TenantId").CurrentValue = tenant.Value;
        }
        // Supplier tenant-guard triggers validate SESSION_CONTEXT on the same
        // connection used by EF. Set it before EF opens its save transaction.
        if (tenant is Guid tenantId && tenantId != Guid.Empty)
        {
            var connection = Database.GetDbConnection();
            var wasClosed = connection.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // Raw ADO.NET commands must explicitly enlist in an active EF transaction.
            command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF; EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tenant";
            parameter.Value = tenantId;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return await base.SaveChangesAsync(cancellationToken);
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductExternalImage> ProductExternalImages => Set<ProductExternalImage>();
    public DbSet<SupplierCatalogImportProfile> SupplierCatalogImportProfiles => Set<SupplierCatalogImportProfile>();
    public DbSet<SupplierCatalogProduct> SupplierCatalogProducts => Set<SupplierCatalogProduct>();
    public DbSet<SupplierCatalogImportHistory> SupplierCatalogImportHistory => Set<SupplierCatalogImportHistory>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.PublicRoute> PublicRoutes => Set<WhatsBiz.Infrastructure.Storefront.PublicRoute>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.MarketplaceCity> MarketplaceCities => Set<WhatsBiz.Infrastructure.Storefront.MarketplaceCity>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.CityMarketplaceMedia> CityMarketplaceMedia => Set<WhatsBiz.Infrastructure.Storefront.CityMarketplaceMedia>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.PartnerSeller> PartnerSellers => Set<WhatsBiz.Infrastructure.Storefront.PartnerSeller>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.CityRetailer> MarketplaceRetailers => Set<WhatsBiz.Infrastructure.Storefront.CityRetailer>();
    public DbSet<WhatsBiz.Infrastructure.Storefront.PartnerProductListing> PartnerProductListings => Set<WhatsBiz.Infrastructure.Storefront.PartnerProductListing>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<ProductTaxMapping> ProductTaxMappings => Set<ProductTaxMapping>();
    public DbSet<CommerceCollection> CommerceCollections => Set<CommerceCollection>();
    public DbSet<CommerceCollectionProduct> CommerceCollectionProducts => Set<CommerceCollectionProduct>();
    public DbSet<StorefrontConfiguration> StorefrontConfigurations => Set<StorefrontConfiguration>();
    public DbSet<StorefrontServiceablePincode> StorefrontServiceablePincodes => Set<StorefrontServiceablePincode>();
    public DbSet<StorefrontPromotion> StorefrontPromotions => Set<StorefrontPromotion>();
    public DbSet<StorefrontMedia> StorefrontMedia => Set<StorefrontMedia>();
    public DbSet<StorefrontBanner> StorefrontBanners => Set<StorefrontBanner>();
    public DbSet<StorefrontProductReview> StorefrontProductReviews => Set<StorefrontProductReview>();
    public DbSet<StorefrontCategoryImage> StorefrontCategoryImages => Set<StorefrontCategoryImage>();
    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();
    public DbSet<Supplier> Suppliers => Set<Supplier>(); public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>(); public DbSet<SupplierAddress> SupplierAddresses => Set<SupplierAddress>(); public DbSet<SupplierBankAccount> SupplierBankAccounts => Set<SupplierBankAccount>(); public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>(); public DbSet<SupplierPaymentTerm> SupplierPaymentTerms => Set<SupplierPaymentTerm>(); public DbSet<Customer> Customers => Set<Customer>(); public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>(); public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>(); public DbSet<CustomerBankAccount> CustomerBankAccounts => Set<CustomerBankAccount>(); public DbSet<CustomerDocument> CustomerDocuments => Set<CustomerDocument>(); public DbSet<CustomerPaymentTerm> CustomerPaymentTerms => Set<CustomerPaymentTerm>(); public DbSet<Warehouse> Warehouses => Set<Warehouse>(); public DbSet<WarehouseType> WarehouseTypes => Set<WarehouseType>(); public DbSet<WarehouseAddress> WarehouseAddresses => Set<WarehouseAddress>(); public DbSet<WarehouseContact> WarehouseContacts => Set<WarehouseContact>(); public DbSet<WarehouseZone> WarehouseZones => Set<WarehouseZone>(); public DbSet<WarehouseBin> WarehouseBins => Set<WarehouseBin>(); public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>(); public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>(); public DbSet<InventoryTransactionDetail> InventoryTransactionDetails => Set<InventoryTransactionDetail>(); public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>(); public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>(); public DbSet<StockReservation> StockReservations => Set<StockReservation>(); public DbSet<InventorySettings> InventorySettings => Set<InventorySettings>(); public DbSet<InventoryValuation> InventoryValuations => Set<InventoryValuation>(); public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>(); public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>(); public DbSet<SalesPayment> SalesPayments => Set<SalesPayment>(); public DbSet<SalesTax> SalesTaxes => Set<SalesTax>(); public DbSet<SalesDiscount> SalesDiscounts => Set<SalesDiscount>(); public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>(); public DbSet<SalesInvoiceReturn> SalesInvoiceReturns => Set<SalesInvoiceReturn>();

    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>(); public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>(); public DbSet<PurchasePayment> PurchasePayments => Set<PurchasePayment>(); public DbSet<PurchaseExpense> PurchaseExpenses => Set<PurchaseExpense>(); public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>(); public DbSet<PurchaseAttachment> PurchaseAttachments => Set<PurchaseAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<BusinessType>(e=>{e.ToTable("BusinessTypes","core");e.HasKey(x=>x.Code);e.Property(x=>x.Code).HasMaxLength(40).IsUnicode(false);e.Property(x=>x.Name).HasMaxLength(80);e.Property(x=>x.NormalizedName).HasMaxLength(80).HasComputedColumnSql("UPPER(LTRIM(RTRIM([Name])))",stored:true);e.Property(x=>x.Version).IsRowVersion();e.Property(x=>x.CreatedBy).HasMaxLength(256);e.Property(x=>x.ModifiedBy).HasMaxLength(256);});
        builder.Entity<WhatsBiz.Infrastructure.Storefront.StoreDirectoryEntry>().Property(x=>x.BusinessTypeCode).HasMaxLength(40).IsUnicode(false);
        builder.HasDbFunction(typeof(ApplicationDbContext).GetMethod(nameof(IsProductPubliclyApproved))!).HasName("IsProductPubliclyApproved").HasSchema("commerce");
        builder.HasDbFunction(typeof(ApplicationDbContext).GetMethod(nameof(ProductPublicContentHash))!).HasName("ProductPublicContentHash").HasSchema("commerce");
        builder.Entity<WhatsBiz.Infrastructure.Storefront.ProductPublication>(e => { e.ToTable("ProductPublications", "commerce"); e.HasKey(x => new { x.TenantId, x.ProductId }); e.Property(x => x.Version).IsRowVersion(); e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false); e.Property(x => x.ContentHash).HasMaxLength(32); e.Property(x => x.Reason).HasMaxLength(1000); e.Property(x => x.Reviewer).HasMaxLength(256); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.PublicCatalogAudit>(e => { e.ToTable("PublicCatalogAudit", "commerce"); e.HasKey(x => x.AuditId); e.Property(x => x.Action).HasMaxLength(32).IsUnicode(false); e.Property(x => x.Actor).HasMaxLength(256); e.Property(x => x.Reason).HasMaxLength(1000); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.StoreDirectoryEntry>(e => { e.ToTable("StoreDirectoryEntries", "commerce"); e.HasKey(x => x.TenantId); e.Property(x => x.Version).IsRowVersion(); e.Property(x => x.BusinessType).HasMaxLength(80); e.Property(x => x.ShortDescription).HasMaxLength(180); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.PublicRoute>(e => { e.ToTable("PublicRouteRegistry", "commerce"); e.HasKey(x => x.RouteId); e.Property(x => x.OriginalSlug).HasMaxLength(120); e.Property(x => x.NormalizedSlug).HasMaxLength(120).HasComputedColumnSql("LOWER(LTRIM(RTRIM([OriginalSlug])))", stored: true); e.Property(x => x.EntityType).HasMaxLength(16).IsUnicode(false); e.Property(x => x.RouteStatus).HasMaxLength(16).IsUnicode(false); e.Property(x => x.Version).IsRowVersion(); e.HasOne(x => x.City).WithOne(x => x.Route).HasForeignKey<WhatsBiz.Infrastructure.Storefront.PublicRoute>(x => x.CityId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.MarketplaceCity>(e => { e.ToTable("Cities", "commerce"); e.HasKey(x => x.CityId); e.Property(x => x.CityName).HasMaxLength(120); e.Property(x => x.StateName).HasMaxLength(120); e.Property(x => x.MarketplaceName).HasMaxLength(160); e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.BannerHeadline).HasMaxLength(120); e.Property(x => x.BannerSubtitle).HasMaxLength(240); e.Property(x => x.SeoTitle).HasMaxLength(180); e.Property(x => x.SeoDescription).HasMaxLength(320); e.Property(x => x.ExternalMarketingDomain).HasMaxLength(253); e.Property(x => x.CreatedBy).HasMaxLength(256); e.Property(x => x.UpdatedBy).HasMaxLength(256); e.Property(x => x.Version).IsRowVersion(); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.CityMarketplaceMedia>(e => { e.ToTable("CityMarketplaceMedia", "commerce", tb=>tb.UseSqlOutputClause(false));e.HasKey(x=>x.MediaId);e.HasIndex(x=>new{x.CityId,x.ResourceType,x.ResourceKey}).IsUnique().HasDatabaseName("UX_CityMarketplaceMedia_CityResourceKey");e.HasIndex(x=>new{x.CityId,x.ResourceType}).IsUnique().HasFilter("[ResourceType] IN ('LOGO','BANNER')").HasDatabaseName("UQ_CityMarketplaceMedia_CityResource");e.Property(x=>x.ResourceType).HasMaxLength(12).IsUnicode(false);e.Property(x=>x.ResourceKey).HasMaxLength(160);e.Property(x=>x.FileName).HasMaxLength(255);e.Property(x=>x.ContentType).HasMaxLength(100);e.Property(x=>x.ThumbnailContentType).HasMaxLength(100);e.Property(x=>x.StorageProvider).HasMaxLength(20);e.Property(x=>x.ObjectKey).HasMaxLength(1024);e.Property(x=>x.ThumbnailObjectKey).HasMaxLength(1024);e.Property(x=>x.ContentHash).HasMaxLength(64).IsUnicode(false);e.Property(x=>x.CreatedBy).HasMaxLength(256);e.HasOne<WhatsBiz.Infrastructure.Storefront.MarketplaceCity>().WithMany().HasForeignKey(x=>x.CityId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.PartnerSeller>(e => { e.ToTable("PartnerSellers", "commerce"); e.HasKey(x => x.PartnerSellerId); e.HasAlternateKey(x => new { x.OperatingTenantId, x.PartnerSellerId }); e.Property(x => x.SellerName).HasMaxLength(160); e.Property(x => x.SellerType).HasMaxLength(24).IsUnicode(false); e.Property(x => x.ContactName).HasMaxLength(120); e.Property(x => x.ContactEmail).HasMaxLength(256); e.Property(x => x.ContactPhone).HasMaxLength(32); e.Property(x => x.Locality).HasMaxLength(120); e.Property(x => x.BusinessRegistrationReference).HasMaxLength(100); e.Property(x => x.TaxRegistrationReference).HasMaxLength(100); e.Property(x => x.FoodRegistrationReference).HasMaxLength(100); e.Property(x => x.VerificationStatus).HasMaxLength(20).IsUnicode(false); e.Property(x => x.SellerStatus).HasMaxLength(16).IsUnicode(false); e.Property(x => x.CreatedBy).HasMaxLength(256); e.Property(x => x.Version).IsRowVersion(); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.CityRetailer>(e => { e.ToTable("CityRetailers", "commerce"); e.HasKey(x => new { x.CityId, x.TenantId }); e.Property(x => x.Locality).HasMaxLength(120); e.Property(x => x.ServiceArea).HasMaxLength(300); e.Property(x => x.CreatedBy).HasMaxLength(256); });
        builder.Entity<WhatsBiz.Infrastructure.Storefront.PartnerProductListing>(e => { e.ToTable("PartnerProductListings", "commerce"); e.HasKey(x => new { x.OperatingTenantId, x.ProductId }); e.Property(x => x.ListingStatus).HasMaxLength(20).IsUnicode(false); e.Property(x => x.CreatedBy).HasMaxLength(256); e.Property(x => x.Version).IsRowVersion(); });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users", "core", table => table.UseSqlOutputClause(false));
            entity.Property(x => x.TenantId);
            entity.Property(x => x.AccountType).HasMaxLength(30);
            entity.Property(x => x.CreatedBy).HasMaxLength(256);
            entity.Property(x => x.ModifiedBy).HasMaxLength(256);
            entity.Property(x => x.MustChangePassword);
            entity.Property(x => x.RowVersion).IsRowVersion();
        });
        builder.Entity<ApplicationRole>().ToTable("Roles", "core");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            .ToTable("UserRoles", "core", table => table.UseSqlOutputClause(false));
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
        builder.Entity<Tenant>(entity => { entity.ToTable("Tenants", "core"); entity.HasKey(x => x.TenantId); entity.HasIndex(x => x.TenantKey).IsUnique(); entity.Property(x => x.TenantKey).HasMaxLength(100).IsRequired(); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.DisplayName).HasMaxLength(200); });
        ConfigureCommerceCollections(builder);
        ConfigureStorefrontPresentation(builder);
        ConfigureSuppliers(builder);
        ConfigureCustomers(builder);
        ConfigureCustomerNormalization(builder);
        ConfigureCustomerGroups(builder);
        ConfigureCustomerTenant(builder);
        ConfigureWarehouses(builder);
        ConfigureWarehouseIndexes(builder);
        ConfigureInventory(builder);
        ConfigurePOS(builder);
        ConfigurePurchases(builder);
        ConfigureTenantOwnership(builder);
        ConfigureProductMasterVisibility(builder);
        ConfigureTenantCodeIndexes(builder);
        ConfigureTriggeredTables(builder);
    }

    private static void ConfigureTriggeredTables(ModelBuilder b)
    {
        b.Entity<Supplier>().ToTable("Suppliers", "purchase", tb => tb.UseSqlOutputClause(false));
        b.Entity<Warehouse>().ToTable("Warehouses", "inventory", tb => tb.UseSqlOutputClause(false));
        b.Entity<SalesInvoice>().ToTable("SalesInvoices", "sales", tb => tb.UseSqlOutputClause(false));
        b.Entity<PurchaseInvoice>().ToTable("PurchaseInvoices", "purchase", tb => tb.UseSqlOutputClause(false));
        b.Entity<InventoryBalance>().ToTable("InventoryBalances", "inventory", tb => tb.UseSqlOutputClause(false));
        b.Entity<InventoryTransaction>().ToTable("InventoryTransactions", "inventory", tb => tb.UseSqlOutputClause(false));
        b.Entity<SalesPayment>().ToTable("SalesPayments", "sales", tb => tb.UseSqlOutputClause(false));
        b.Entity<SalesInvoiceItem>().ToTable("SalesInvoiceItems", "sales", tb => tb.UseSqlOutputClause(false));
        b.Entity<PurchaseInvoiceItem>().ToTable("PurchaseInvoiceItems", "purchase", tb => tb.UseSqlOutputClause(false));
        b.Entity<PurchasePayment>().ToTable("PurchasePayments", "purchase", tb => tb.UseSqlOutputClause(false));
        b.Entity<PurchaseReturn>().ToTable("PurchaseReturns", "purchase", tb => tb.UseSqlOutputClause(false));
        b.Entity<SalesInvoiceReturn>().ToTable("SalesInvoiceReturns", "sales", tb => tb.UseSqlOutputClause(false));
    }

    // Phase 1 ownership columns are nullable while historical ownership is audited.
    // Shadow properties keep the domain model backwards-compatible during rollout.
    private static void ConfigureTenantOwnership(ModelBuilder b)
    {
        ConfigureTenantProperty(b.Entity<Supplier>(), "SupplierId");
        ConfigureTenantProperty(b.Entity<Warehouse>(), "WarehouseId");
        ConfigureTenantProperty(b.Entity<SalesInvoice>(), "InvoiceId");
        ConfigureTenantProperty(b.Entity<PurchaseInvoice>(), "PurchaseInvoiceId");
        ConfigureTenantProperty(b.Entity<InventoryBalance>(), "InventoryBalanceId");
        ConfigureTenantProperty(b.Entity<InventoryTransaction>(), "TransactionId");
    }

    private static void ConfigureProductMasterVisibility(ModelBuilder b)
    {
        b.Entity<TenantProductCategory>().ToTable("TenantProductCategories", "master").HasKey(x => new { x.TenantId, x.ProductCategoryId });
        b.Entity<TenantProductCategory>()
            .HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(x => x.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<TenantBrand>().ToTable("TenantBrands", "master").HasKey(x => new { x.TenantId, x.BrandId });
        b.Entity<TenantUnitOfMeasure>().ToTable("TenantUnitsOfMeasure", "master").HasKey(x => new { x.TenantId, x.UnitId });
        // Keep the master-unit dependency visible to EF.  Without this relationship
        // EF may insert the tenant mapping before a newly-added UnitsOfMeasure row,
        // causing FK_TenantUnitsOfMeasure_Unit during UOM imports.
        b.Entity<TenantUnitOfMeasure>()
            .HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTenantCodeIndexes(ModelBuilder b)
    {
        var products = b.Entity<Product>();
        var globalProductCode = products.Metadata.FindIndex([products.Property(x => x.ProductCode).Metadata]);
        if (globalProductCode is not null) products.Metadata.RemoveIndex(globalProductCode);
        products.HasIndex(x => new { x.TenantId, x.ProductCode }).IsUnique().HasDatabaseName("UX_Products_ProductCode").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0");
        b.Entity<Customer>().HasIndex(x => new { x.TenantId, x.CustomerCode }).IsUnique().HasDatabaseName("UX_Customers_Code").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0");
        b.Entity<Supplier>().HasIndex("TenantId", nameof(Supplier.SupplierName)).IsUnique().HasDatabaseName("UX_Suppliers_Name").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0");
    }

    private static void ConfigureTenantProperty<TEntity>(EntityTypeBuilder<TEntity> entity, string key)
        where TEntity : class
    {
        entity.Property<Guid?>("TenantId");
        entity.HasIndex("TenantId", key);
    }

    private static void ConfigurePurchases(ModelBuilder b) { b.Entity<PurchaseInvoice>(e => { e.ToTable("PurchaseInvoices", "purchase"); e.HasKey(x => x.PurchaseInvoiceId); foreach (var p in new[] { nameof(PurchaseInvoice.Subtotal), nameof(PurchaseInvoice.DiscountAmount), nameof(PurchaseInvoice.TaxAmount), nameof(PurchaseInvoice.ExpenseAmount), nameof(PurchaseInvoice.RoundOff), nameof(PurchaseInvoice.GrandTotal), nameof(PurchaseInvoice.PaidAmount) }) e.Property(p).HasPrecision(18, 2); e.Property(x => x.BalanceAmount).HasPrecision(18, 2).HasComputedColumnSql("[GrandTotal]-[PaidAmount]", true); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<PurchaseInvoiceItem>(e => { e.ToTable("PurchaseInvoiceItems", "purchase"); e.HasKey(x => x.PurchaseItemId); foreach (var p in new[] { nameof(PurchaseInvoiceItem.Quantity), nameof(PurchaseInvoiceItem.FreeQuantity), nameof(PurchaseInvoiceItem.ReturnedQuantity), nameof(PurchaseInvoiceItem.PurchasePrice), nameof(PurchaseInvoiceItem.MRP), nameof(PurchaseInvoiceItem.SellingPrice) }) e.Property(p).HasPrecision(18, 4); foreach (var p in new[] { nameof(PurchaseInvoiceItem.DiscountAmount), nameof(PurchaseInvoiceItem.GSTAmount), nameof(PurchaseInvoiceItem.AllocatedExpense), nameof(PurchaseInvoiceItem.LineTotal) }) e.Property(p).HasPrecision(18, 2); foreach (var p in new[] { nameof(PurchaseInvoiceItem.DiscountPercentage), nameof(PurchaseInvoiceItem.GSTPercentage) }) e.Property(p).HasPrecision(5, 2); e.HasOne<PurchaseInvoice>().WithMany(x => x.Items).HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<PurchasePayment>(e => { e.ToTable("PurchasePayments", "purchase"); e.HasKey(x => x.PurchasePaymentId); e.Property(x => x.Amount).HasPrecision(18, 2); e.HasOne<PurchaseInvoice>().WithMany(x => x.Payments).HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.PaymentMethod).WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<PurchaseExpense>(e => { e.ToTable("PurchaseExpenses", "purchase"); e.HasKey(x => x.PurchaseExpenseId); e.Property(x => x.Amount).HasPrecision(18, 2); e.HasOne<PurchaseInvoice>().WithMany(x => x.Expenses).HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<PurchaseReturn>(e => { e.ToTable("PurchaseReturns", "purchase"); e.HasKey(x => x.PurchaseReturnId); e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.AdjustmentAmount).HasPrecision(18, 2); }); b.Entity<PurchaseAttachment>(e => { e.ToTable("PurchaseAttachments", "purchase"); e.HasKey(x => x.PurchaseAttachmentId); e.HasOne<PurchaseInvoice>().WithMany(x => x.Attachments).HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade); }); }

    private static void ConfigurePOS(ModelBuilder b) { b.Entity<SalesInvoice>(e => { e.ToTable("SalesInvoices", "sales"); e.HasKey(x => x.InvoiceId); foreach (var p in new[] { nameof(SalesInvoice.Subtotal), nameof(SalesInvoice.DiscountAmount), nameof(SalesInvoice.TaxAmount), nameof(SalesInvoice.RoundOff), nameof(SalesInvoice.DeliveryCharge), nameof(SalesInvoice.DeliveryTaxAmount), nameof(SalesInvoice.PromotionDiscountAmount), nameof(SalesInvoice.FreeDeliveryThresholdSnapshot), nameof(SalesInvoice.GrandTotal), nameof(SalesInvoice.PaidAmount) }) e.Property(p).HasPrecision(18, 2); e.Property(x => x.BalanceAmount).HasPrecision(18, 2).HasComputedColumnSql("[GrandTotal]-[PaidAmount]", true); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<SalesInvoiceItem>(e => { e.ToTable("SalesInvoiceItems", "sales"); e.HasKey(x => x.InvoiceItemId); foreach (var p in new[] { nameof(SalesInvoiceItem.Quantity), nameof(SalesInvoiceItem.UnitPrice), nameof(SalesInvoiceItem.ReturnedQuantity) }) e.Property(p).HasPrecision(18, 4); foreach (var p in new[] { nameof(SalesInvoiceItem.DiscountAmount), nameof(SalesInvoiceItem.TaxAmount), nameof(SalesInvoiceItem.LineTotal) }) e.Property(p).HasPrecision(18, 2); foreach (var p in new[] { nameof(SalesInvoiceItem.DiscountPercentage), nameof(SalesInvoiceItem.TaxPercentage) }) e.Property(p).HasPrecision(5, 2); e.HasOne<SalesInvoice>().WithMany(x => x.Items).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<SalesPayment>(e => { e.ToTable("SalesPayments", "sales"); e.HasKey(x => x.PaymentId); e.Property(x => x.Amount).HasPrecision(18, 2); e.HasOne<SalesInvoice>().WithMany(x => x.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.PaymentMethod).WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<SalesTax>(e => { e.ToTable("SalesTaxes", "sales"); e.HasKey(x => x.SalesTaxId); e.Property(x => x.TaxPercentage).HasPrecision(5, 2); e.Property(x => x.TaxableAmount).HasPrecision(18, 2); e.Property(x => x.TaxAmount).HasPrecision(18, 2); e.HasOne<SalesInvoice>().WithMany(x => x.Taxes).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<SalesDiscount>(e => { e.ToTable("SalesDiscounts", "sales"); e.HasKey(x => x.SalesDiscountId); e.Property(x => x.DiscountValue).HasPrecision(18, 2); e.Property(x => x.DiscountAmount).HasPrecision(18, 2); e.HasOne<SalesInvoice>().WithMany(x => x.Discounts).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<PaymentMethod>(e => { e.ToTable("PaymentMethods", "sales"); e.HasKey(x => x.PaymentMethodId); e.Property(x => x.RowVersion).IsRowVersion(); }); b.Entity<SalesInvoiceReturn>(e => { e.ToTable("SalesInvoiceReturns", "sales"); e.HasKey(x => x.ReturnId); e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.RefundAmount).HasPrecision(18, 2); }); }

    private static void ConfigureInventory(ModelBuilder b) { b.Entity<InventoryBalance>(e => { e.ToTable("InventoryBalances", "inventory"); e.HasKey(x => x.InventoryBalanceId); e.Property(x => x.QuantityOnHand).HasPrecision(18, 4); e.Property(x => x.QuantityReserved).HasPrecision(18, 4); e.Property(x => x.QuantityAvailable).HasPrecision(18, 4).HasComputedColumnSql("[QuantityOnHand]-[QuantityReserved]", true); e.Property(x => x.AverageCost).HasPrecision(18, 4); e.Property(x => x.LastPurchaseCost).HasPrecision(18, 4); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Bin).WithMany().HasForeignKey(x => x.BinId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<InventoryTransaction>(e => { e.ToTable("InventoryTransactions", "inventory"); e.HasKey(x => x.TransactionId); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<InventoryTransactionDetail>(e => { e.ToTable("InventoryTransactionDetails", "inventory"); e.HasKey(x => x.TransactionDetailId); e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.UnitCost).HasPrecision(18, 4); e.Property(x => x.TotalCost).HasPrecision(18, 4).HasComputedColumnSql("[Quantity]*[UnitCost]", true); e.HasOne<InventoryTransaction>().WithMany(x => x.Details).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<StockAdjustment>(e => { e.ToTable("StockAdjustments", "inventory"); e.HasKey(x => x.StockAdjustmentId); }); b.Entity<StockTransfer>(e => { e.ToTable("StockTransfers", "inventory"); e.HasKey(x => x.StockTransferId); }); b.Entity<StockReservation>(e => { e.ToTable("StockReservations", "inventory"); e.HasKey(x => x.StockReservationId); e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.ReleasedQuantity).HasPrecision(18, 4); e.Property(x => x.RowVersion).IsRowVersion(); }); b.Entity<InventorySettings>(e => { e.ToTable("InventorySettings", "inventory"); e.HasKey(x => x.InventorySettingsId); e.Property(x => x.RowVersion).IsRowVersion(); }); b.Entity<InventoryValuation>(e => { e.ToTable("InventoryValuation", "inventory"); e.HasKey(x => x.InventoryValuationId); e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.UnitCost).HasPrecision(18, 4); e.Property(x => x.TotalValue).HasPrecision(18, 4).HasComputedColumnSql("[Quantity]*[UnitCost]", true); }); }

    private static void ConfigureWarehouses(ModelBuilder b) { b.Entity<WarehouseType>(e => { e.ToTable("WarehouseTypes", "inventory"); e.HasKey(x => x.WarehouseTypeId); e.Property(x => x.TypeCode).HasMaxLength(30); e.Property(x => x.TypeName).HasMaxLength(100); e.Property(x => x.RowVersion).IsRowVersion(); }); b.Entity<Warehouse>(e => { e.ToTable("Warehouses", "inventory"); e.HasKey(x => x.WarehouseId); e.Property(x => x.WarehouseCode).HasMaxLength(50); e.Property(x => x.WarehouseName).HasMaxLength(200); e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Capacity).HasPrecision(18, 4); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne(x => x.WarehouseType).WithMany().HasForeignKey(x => x.WarehouseTypeId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Address).WithOne().HasForeignKey<WarehouseAddress>(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<WarehouseAddress>(e => { e.ToTable("WarehouseAddresses", "inventory"); e.HasKey(x => x.AddressId); }); b.Entity<WarehouseContact>(e => { e.ToTable("WarehouseContacts", "inventory"); e.HasKey(x => x.ContactId); e.HasOne<Warehouse>().WithMany(x => x.Contacts).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<WarehouseZone>(e => { e.ToTable("WarehouseZones", "inventory"); e.HasKey(x => x.ZoneId); e.HasOne<Warehouse>().WithMany(x => x.Zones).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<WarehouseBin>(e => { e.ToTable("WarehouseBins", "inventory"); e.HasKey(x => x.BinId); e.Property(x => x.MaximumCapacity).HasPrecision(18, 4); e.HasOne<WarehouseZone>().WithMany(x => x.Bins).HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade); }); }
    private static void ConfigureWarehouseIndexes(ModelBuilder b) { var e = b.Entity<Warehouse>(); e.Property<Guid?>("TenantId"); e.HasIndex("TenantId", nameof(Warehouse.WarehouseCode)).IsUnique().HasDatabaseName("UX_Warehouses_Code").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0"); e.HasIndex("TenantId", nameof(Warehouse.WarehouseName)).IsUnique().HasDatabaseName("UX_Warehouses_Name").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0"); e.HasIndex("TenantId", nameof(Warehouse.IsDefault)).IsUnique().HasDatabaseName("UX_Warehouses_Default").HasFilter("[TenantId] IS NOT NULL AND [IsDefault] = 1 AND [IsDeleted] = 0"); }

    private static void ConfigureCustomers(ModelBuilder b) { b.Entity<CustomerPaymentTerm>(e => { e.ToTable("CustomerPaymentTerms", "sales"); e.HasKey(x => x.PaymentTermId); e.Property(x => x.RowVersion).IsRowVersion(); }); b.Entity<Customer>(e => { e.ToTable("Customers", "sales"); e.HasKey(x => x.CustomerId); e.Property(x => x.CustomerCode).HasMaxLength(50); e.Property(x => x.CustomerName).HasMaxLength(250); e.Property(x => x.GSTIN).HasMaxLength(15); e.Property(x => x.PAN).HasMaxLength(10); e.Property(x => x.ProfileMediaId); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.CreditLimit).HasPrecision(18, 2); e.Property(x => x.OpeningBalance).HasPrecision(18, 2); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne(x => x.PaymentTerm).WithMany().HasForeignKey(x => x.PaymentTermId).OnDelete(DeleteBehavior.Restrict); }); b.Entity<CustomerContact>(e => { e.ToTable("CustomerContacts", "sales"); e.HasKey(x => x.ContactId); e.HasOne<Customer>().WithMany(x => x.Contacts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<CustomerAddress>(e => { e.ToTable("CustomerAddresses", "sales"); e.HasKey(x => x.AddressId); e.Property(x => x.RecipientName).HasMaxLength(250); e.Property(x => x.Mobile).HasMaxLength(18); e.Property(x => x.Landmark).HasMaxLength(250); e.Property(x => x.AddressType).HasMaxLength(30); e.HasOne<Customer>().WithMany(x => x.Addresses).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<CustomerBankAccount>(e => { e.ToTable("CustomerBankAccounts", "sales"); e.HasKey(x => x.BankAccountId); e.HasOne<Customer>().WithMany(x => x.BankAccounts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<CustomerDocument>(e => { e.ToTable("CustomerDocuments", "sales"); e.HasKey(x => x.DocumentId); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne<Customer>().WithMany(x => x.Documents).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade); }); }

    private static void ConfigureCustomerNormalization(ModelBuilder b)
    {
        b.Entity<Customer>().Property<string?>("MobileNormalized").HasColumnName("MobileNormalized").HasMaxLength(10).IsUnicode(false);
    }

    private static void ConfigureSuppliers(ModelBuilder b)
    {
        b.Entity<SupplierPaymentTerm>(e => { e.ToTable("SupplierPaymentTerms", "purchase"); e.HasKey(x => x.PaymentTermId); e.Property(x => x.PaymentTermCode).HasMaxLength(30); e.Property(x => x.PaymentTermName).HasMaxLength(100); e.Property(x => x.RowVersion).IsRowVersion(); });
        b.Entity<Supplier>(e => { e.ToTable("Suppliers", "purchase"); e.HasKey(x => x.SupplierId); e.Property<Guid?>("TenantId"); e.Property(x => x.SupplierCode).HasMaxLength(50); e.Property(x => x.SupplierName).HasMaxLength(250); e.Property(x => x.SupplierType).HasMaxLength(50); e.Property(x => x.GSTIN).HasMaxLength(15); e.Property(x => x.PAN).HasMaxLength(10); e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Mobile).HasMaxLength(15); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.CreditLimit).HasPrecision(18, 2); e.Property(x => x.OpeningBalance).HasPrecision(18, 2); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex("TenantId", nameof(Supplier.SupplierCode)).IsUnique().HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0"); e.HasIndex("TenantId", nameof(Supplier.GSTIN)).IsUnique().HasFilter("[TenantId] IS NOT NULL AND [GSTIN] IS NOT NULL AND [IsDeleted] = 0"); e.HasOne(x => x.PaymentTerm).WithMany().HasForeignKey(x => x.PaymentTermId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<SupplierContact>(e => { e.ToTable("SupplierContacts", "purchase"); e.HasKey(x => x.ContactId); e.HasOne<Supplier>().WithMany(x => x.Contacts).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<SupplierAddress>(e => { e.ToTable("SupplierAddresses", "purchase"); e.HasKey(x => x.AddressId); e.HasOne<Supplier>().WithMany(x => x.Addresses).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<SupplierBankAccount>(e => { e.ToTable("SupplierBankAccounts", "purchase"); e.HasKey(x => x.BankAccountId); e.HasOne<Supplier>().WithMany(x => x.BankAccounts).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<SupplierDocument>(e => { e.ToTable("SupplierDocuments", "purchase"); e.HasKey(x => x.DocumentId); e.Property(x => x.RowVersion).IsRowVersion(); e.HasOne<Supplier>().WithMany(x => x.Documents).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SupplierCatalogImportProfile>(e => { e.ToTable("SupplierCatalogImportProfiles", "purchase"); e.HasKey(x => new { x.TenantId, x.SupplierId }); e.Property(x => x.Format).HasMaxLength(20); e.Property(x => x.FieldMappingsJson).HasColumnType("nvarchar(max)"); e.Property(x => x.SupplierOwnedFieldsJson).HasColumnType("nvarchar(max)"); e.Property(x => x.ModifiedBy).HasMaxLength(256); e.Property(x => x.Version).IsRowVersion(); e.HasOne<Supplier>().WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Brand>().WithMany().HasForeignKey(x=>x.DefaultBrandId).OnDelete(DeleteBehavior.Restrict); e.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x=>x.DefaultUnitId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<SupplierCatalogProduct>(e => { e.ToTable("SupplierCatalogProducts", "purchase"); e.HasKey(x => x.SupplierCatalogProductId); e.Property(x => x.SupplierSku).HasMaxLength(150); e.Property(x => x.SourceHandle).HasMaxLength(250); e.Property(x => x.SourceRowIdentity).HasMaxLength(250); e.Property(x => x.OriginalTagsJson).HasColumnType("nvarchar(max)"); e.Property(x => x.ImageUrlsJson).HasColumnType("nvarchar(max)"); e.Property(x => x.DescriptionHtml).HasColumnType("nvarchar(max)"); e.Property(x => x.DescriptionText).HasColumnType("nvarchar(max)"); foreach(var p in new[]{nameof(SupplierCatalogProduct.ProductCost),nameof(SupplierCatalogProduct.PackagingCost),nameof(SupplierCatalogProduct.ShippingCost),nameof(SupplierCatalogProduct.CombinedShippingPackagingCost),nameof(SupplierCatalogProduct.SupplierAvailableQuantity)}) e.Property(p).HasPrecision(18,4); e.HasIndex(x=>new{x.TenantId,x.SupplierId,x.SupplierSku}).IsUnique().HasFilter("[SupplierSku] IS NOT NULL").HasDatabaseName("UX_SupplierCatalogProducts_TenantSupplierSku"); e.HasIndex(x=>new{x.TenantId,x.SupplierId,x.SourceRowIdentity}).IsUnique().HasDatabaseName("UX_SupplierCatalogProducts_SourceRow"); e.HasIndex(x=>new{x.TenantId,x.ProductId}).HasDatabaseName("IX_SupplierCatalogProducts_TenantProduct"); e.HasOne<Supplier>().WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Product>().WithMany().HasForeignKey(x=>x.ProductId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<SupplierCatalogImportHistory>(e => { e.ToTable("SupplierCatalogImportHistory", "purchase"); e.HasKey(x => x.ImportId); e.Property(x => x.Format).HasMaxLength(20); e.Property(x => x.SourceFileHash).HasMaxLength(64).IsFixedLength().IsUnicode(false); e.Property(x => x.ImportedBy).HasMaxLength(256); e.HasIndex(x=>new{x.TenantId,x.SupplierId,x.ImportedOn}).HasDatabaseName("IX_SupplierCatalogImportHistory_TenantSupplierDate"); e.HasOne<Supplier>().WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict); });
    }

    private static void ConfigureProductMaster(ModelBuilder builder)
    {
        builder.Entity<ProductCategory>(entity => { ConfigureAudit(entity); entity.ToTable("ProductCategories", "master"); entity.HasKey(x => x.ProductCategoryId); entity.Property(x => x.CategoryCode).HasMaxLength(50); entity.Property(x => x.CategoryName).HasMaxLength(200); entity.Property(x => x.Description).HasMaxLength(1000); entity.HasIndex(x => x.CategoryCode).IsUnique().HasFilter("[IsDeleted] = 0"); entity.HasOne(x => x.ParentCategory).WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<Brand>(entity => { ConfigureAudit(entity); entity.ToTable("Brands", "master"); entity.HasKey(x => x.BrandId); entity.Property(x => x.BrandCode).HasMaxLength(50); entity.Property(x => x.BrandName).HasMaxLength(200); entity.Property(x => x.Description).HasMaxLength(1000); entity.Property(x => x.Logo).HasMaxLength(500); entity.HasIndex(x => x.BrandCode).IsUnique().HasFilter("[IsDeleted] = 0"); });
        builder.Entity<UnitOfMeasure>(entity => { ConfigureAudit(entity); entity.ToTable("UnitsOfMeasure", "master"); entity.HasKey(x => x.UnitId); entity.Property(x => x.UnitCode).HasMaxLength(50); entity.Property(x => x.UnitName).HasMaxLength(200); entity.Property(x => x.ShortName).HasMaxLength(20); entity.HasIndex(x => x.UnitCode).IsUnique().HasFilter("[IsDeleted] = 0"); });
        builder.Entity<Product>(entity => { ConfigureAudit(entity); entity.ToTable("Products", "master"); entity.HasKey(x => x.ProductId); entity.Property(x => x.ProductCode).HasMaxLength(50); entity.Property(x => x.Barcode).HasMaxLength(100); entity.Property(x => x.ProductName).HasMaxLength(250); entity.Property(x => x.ShortDescription).HasColumnType("nvarchar(max)"); entity.Property(x => x.ReturnPolicyMode).HasMaxLength(20); entity.Property(x => x.ReturnPolicyDescription).HasMaxLength(1000); entity.Property(x => x.HSNCode).HasMaxLength(20); entity.Property(x => x.SACCode).HasMaxLength(20); entity.Property(x => x.ImageUrl).HasMaxLength(2048); entity.Property(x => x.PackSize).HasMaxLength(50); foreach (var property in new[] { nameof(Product.PurchasePrice), nameof(Product.PackagingCost), nameof(Product.ShippingCost), nameof(Product.CombinedShippingPackagingCost), nameof(Product.SellingPrice), nameof(Product.MRP), nameof(Product.MinimumStock), nameof(Product.MaximumStock), nameof(Product.ReorderLevel), nameof(Product.Weight), nameof(Product.Length), nameof(Product.Width), nameof(Product.Height) }) entity.Property(property).HasPrecision(18, 4); entity.Property(x => x.GSTPercentage).HasPrecision(5, 2); entity.HasIndex(x => new { x.TenantId, x.ProductCode }).IsUnique().HasDatabaseName("UX_Products_ProductCode").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0"); entity.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasDatabaseName("UX_Products_Tenant_Barcode").HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0"); entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ProductExternalImage>(e => { e.ToTable("ProductExternalImages", "master"); e.HasKey(x => x.ExternalImageId); e.Property(x => x.ImageUrl).HasMaxLength(2048); e.HasIndex(x => new { x.TenantId, x.ProductId, x.DisplayOrder }); e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ProductSpecification>(entity => { ConfigureAudit(entity); entity.ToTable("ProductSpecifications", "master"); entity.HasKey(x => x.ProductSpecificationId); entity.Property(x => x.Name).HasMaxLength(100); entity.Property(x => x.Value).HasMaxLength(1000); entity.HasIndex(x => new { x.TenantId, x.ProductId, x.Name }).IsUnique().HasDatabaseName("UX_ProductSpecifications_Tenant_Product_Name").HasFilter("[IsDeleted] = 0"); entity.HasOne<Product>().WithMany(x => x.Specifications).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductImage>(entity => { ConfigureAudit(entity); entity.ToTable("ProductImages", "master"); entity.HasKey(x => x.ProductImageId); entity.Property(x => x.FileName).HasMaxLength(255); entity.Property(x => x.ContentType).HasMaxLength(100); entity.Property(x => x.ThumbnailContentType).HasMaxLength(100); entity.Property(x => x.StorageProvider).HasMaxLength(20); entity.Property(x => x.ObjectKey).HasMaxLength(1024); entity.Property(x => x.ThumbnailObjectKey).HasMaxLength(1024); entity.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductBarcode>(entity => { ConfigureAudit(entity); entity.ToTable("ProductBarcodes", "master"); entity.HasKey(x => x.ProductBarcodeId); entity.Property(x => x.Barcode).HasMaxLength(450); entity.Property(x => x.BarcodeType).HasMaxLength(20); entity.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasDatabaseName("UX_ProductBarcodes_Tenant_Barcode").HasFilter("[IsActive] = 1 AND [IsDeleted] = 0"); entity.HasIndex(x => new { x.TenantId, x.ProductId }).HasDatabaseName("IX_ProductBarcodes_Tenant_Product"); entity.HasOne<Product>().WithMany(x => x.Barcodes).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductPrice>(entity => { ConfigureAudit(entity); entity.ToTable("ProductPrices", "master"); entity.HasKey(x => x.ProductPriceId); entity.Property(x => x.PriceType).HasMaxLength(50); entity.Property(x => x.Amount).HasPrecision(18, 4); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ProductTaxMapping>(entity => { ConfigureAudit(entity); entity.ToTable("ProductTaxMappings", "master"); entity.HasKey(x => x.ProductTaxMappingId); entity.Property(x => x.TaxCode).HasMaxLength(50); entity.Property(x => x.TaxPercentage).HasPrecision(5, 2); entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); });
    }

    private static void ConfigureCustomerGroups(ModelBuilder b) { b.Entity<CustomerGroup>(e => { e.ToTable("CustomerGroups", "sales"); e.HasKey(x => x.CustomerGroupId); e.Property(x => x.GroupCode).HasMaxLength(50); e.Property(x => x.GroupName).HasMaxLength(150); e.HasIndex(x => new { x.TenantId, x.GroupCode }).IsUnique(); e.HasIndex(x => new { x.TenantId, x.GroupName }).IsUnique(); }); b.Entity<Customer>(e => e.HasOne(x => x.CustomerGroup).WithMany(x => x.Customers).HasForeignKey(x => x.CustomerGroupId).OnDelete(DeleteBehavior.Restrict)); }
    private static void ConfigureCustomerTenant(ModelBuilder b) { b.Entity<Customer>(e => { e.Property(x => x.TenantId); e.HasIndex(x => new { x.TenantId, x.CustomerId }); e.HasIndex(x => new { x.TenantId, x.Mobile }); }); }

    private static void ConfigureCommerceCollections(ModelBuilder builder)
    {
        builder.Entity<CommerceCollection>(entity =>
        {
            entity.ToTable("Collections", "commerce"); entity.HasKey(x => x.CollectionId);
            entity.Property(x => x.Name).HasMaxLength(200); entity.Property(x => x.Slug).HasMaxLength(220);
            entity.Property(x => x.Description).HasMaxLength(1000); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.TenantId, x.IsActive, x.DisplayOrder, x.Name }).HasFilter("[IsDeleted] = 0");
            entity.HasMany(x => x.Products).WithOne(x => x.Collection).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CommerceCollectionProduct>(entity =>
        {
            entity.ToTable("CollectionProducts", "commerce"); entity.HasKey(x => x.CollectionProductId);
            entity.Property(x => x.CreatedBy).HasMaxLength(256); entity.Property(x => x.ModifiedBy).HasMaxLength(256);
            entity.HasIndex(x => new { x.TenantId, x.CollectionId, x.ProductId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CollectionId, x.DisplayOrder, x.ProductId });
            entity.HasIndex(x => new { x.TenantId, x.ProductId });
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Collection).WithMany(x => x.Products).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureStorefrontPresentation(ModelBuilder b)
    {
        b.Entity<StorefrontConfiguration>(e => { e.ToTable("StorefrontConfigurations", "commerce", tb => tb.UseSqlOutputClause(false)); e.HasKey(x => x.TenantId); e.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.FeaturedCategory1Id).OnDelete(DeleteBehavior.Restrict); e.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.FeaturedCategory2Id).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => x.FeaturedCategory1Id).HasDatabaseName("IX_StorefrontConfigurations_FeaturedCategory1Id"); e.HasIndex(x => x.FeaturedCategory2Id).HasDatabaseName("IX_StorefrontConfigurations_FeaturedCategory2Id"); e.Property(x => x.Tagline).HasMaxLength(250); e.Property(x => x.AccentColor).HasMaxLength(20); e.Property(x => x.DeliveryMessage).HasMaxLength(250); e.Property(x => x.DeliveryCoverageMode).HasMaxLength(20).IsUnicode(false).HasDefaultValue("ALLOWLIST"); e.Property(x => x.AutomatedEmailMode).HasMaxLength(24).IsUnicode(false).HasDefaultValue("KHATADHARI_DEFAULT"); e.Property(x => x.NotificationSenderDisplayName).HasMaxLength(150); e.Property(x => x.NotificationReplyToEmail).HasMaxLength(256); e.Property(x => x.CustomSmtpHost).HasMaxLength(255); e.Property(x => x.CustomSmtpSecurityMode).HasMaxLength(16).IsUnicode(false); e.Property(x => x.CustomSmtpUsername).HasMaxLength(256); e.Property(x => x.CustomSmtpPasswordProtected).HasMaxLength(4000); e.Property(x => x.CustomSmtpFromEmail).HasMaxLength(256); e.Property(x => x.FreeDeliveryThreshold).HasPrecision(18, 2); e.Property(x => x.StandardDeliveryCharge).HasPrecision(18, 2); e.Property(x => x.DefaultReturnWindowDays).HasDefaultValue(7); e.Property(x => x.ShowProductDetailsOnShippingLabel).HasDefaultValue(true); e.Property(x => x.NewOrderNotificationEmail).HasMaxLength(256); e.Property(x => x.RestockWhatsAppTemplateName).HasMaxLength(100); });
        b.Entity<StorefrontServiceablePincode>(e => { e.ToTable("StorefrontServiceablePincodes", "commerce"); e.HasKey(x => new { x.TenantId, x.Pincode }); e.Property(x => x.Pincode).HasMaxLength(6).IsUnicode(false); e.Property(x => x.IsExcluded).HasDefaultValue(false); e.HasIndex(x => new { x.TenantId, x.IsExcluded, x.IsActive, x.Pincode }); });
        b.Entity<StorefrontPromotion>(e => { e.ToTable("StorefrontPromotions", "commerce"); e.HasKey(x => x.PromotionId); e.Property(x => x.OfferName).HasMaxLength(150); e.Property(x => x.OfferType).HasMaxLength(20).IsUnicode(false); e.Property(x => x.DiscountType).HasMaxLength(12).IsUnicode(false); e.Property(x => x.MinimumPurchaseAmount).HasPrecision(18, 2); e.Property(x => x.DiscountValue).HasPrecision(18, 2); e.Property(x => x.MaximumDiscount).HasPrecision(18, 2); e.Property(x => x.ShortDescription).HasMaxLength(300); e.Property(x => x.PromoCode).HasMaxLength(50); e.Property(x => x.CtaLabel).HasMaxLength(40); e.Property(x => x.EligibleItemsDescription).HasMaxLength(500); e.HasAlternateKey(x => new { x.TenantId, x.PromotionId }); e.HasIndex(x => new { x.TenantId, x.IsActive, x.IsDeleted }); });
        b.Entity<StorefrontMedia>(e => { e.ToTable("StorefrontMedia", "commerce", tb => tb.UseSqlOutputClause(false)); e.HasKey(x => x.MediaId); e.Property(x => x.ResourceType).HasMaxLength(30); e.Property(x => x.FileName).HasMaxLength(255); e.Property(x => x.ContentType).HasMaxLength(100); e.Property(x => x.ThumbnailContentType).HasMaxLength(100); e.Property(x => x.StorageProvider).HasMaxLength(20); e.Property(x => x.ObjectKey).HasMaxLength(1024); e.Property(x => x.ThumbnailObjectKey).HasMaxLength(1024); e.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false); e.HasIndex(x => new { x.TenantId, x.MediaId }); e.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique().HasFilter("[CustomerId] IS NOT NULL"); });
        b.Entity<StorefrontProductReview>(e => { e.ToTable("StorefrontProductReviews", "commerce", tb => tb.UseSqlOutputClause(false)); e.HasKey(x => x.ReviewId); e.Property(x => x.ReviewText).HasMaxLength(1000); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false); e.HasIndex(x => new { x.TenantId, x.CustomerId, x.ProductId }).IsUnique(); e.HasIndex(x => new { x.TenantId, x.ProductId, x.Status }); });
        b.Entity<StorefrontBanner>(e => { e.ToTable("StorefrontBanners", "commerce", tb => tb.UseSqlOutputClause(false)); e.HasKey(x => x.BannerId); e.Property(x => x.Slot).HasMaxLength(20); e.Property(x => x.Title).HasMaxLength(150); e.Property(x => x.Subtitle).HasMaxLength(300); e.Property(x => x.TargetUrl).HasMaxLength(500); e.HasOne<StorefrontPromotion>().WithMany().HasForeignKey(x => new { x.TenantId, x.PromotionId }).HasPrincipalKey(x => new { x.TenantId, x.PromotionId }).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new { x.TenantId, x.Slot }).IsUnique(); });
        b.Entity<StorefrontCategoryImage>(e => { e.ToTable("StorefrontCategoryImages", "commerce", tb => tb.UseSqlOutputClause(false)); e.HasKey(x => new { x.TenantId, x.ProductCategoryId }); e.HasIndex(x => new { x.TenantId, x.MediaId }).IsUnique(); });
    }

    private static void ConfigureAudit<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity) where TEntity : ProductMasterEntity
    {
        entity.Property(x => x.CreatedBy).HasMaxLength(256);
        entity.Property(x => x.ModifiedBy).HasMaxLength(256);
        entity.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class TenantProductCategory { public Guid TenantId { get; set; } public Guid ProductCategoryId { get; set; } }
internal sealed class TenantBrand { public Guid TenantId { get; set; } public Guid BrandId { get; set; } }
internal sealed class TenantUnitOfMeasure { public Guid TenantId { get; set; } public Guid UnitId { get; set; } }
