using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Domain.POS;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Purchases;
using WhatsBiz.Domain.Suppliers;
using WhatsBiz.Domain.Warehouses;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Security;

public sealed class NewTenantDataIsolationTests
{
    [Fact]
    public async Task SalesPurchasesAndInventoryAreFilteredByAuthenticatedTenant()
    {
        var tenantA=Guid.NewGuid(); var tenantB=Guid.NewGuid();
        await using var db=Db();
        var saleA=new SalesInvoice{InvoiceNumber="A-SALE",InvoiceDate=DateTimeOffset.UtcNow,Status="COMPLETED"};
        var saleB=new SalesInvoice{InvoiceNumber="B-SALE",InvoiceDate=DateTimeOffset.UtcNow,Status="COMPLETED"};
        var supplierA=new Supplier{SupplierCode="A-SUP",SupplierName="A Supplier"}; var supplierB=new Supplier{SupplierCode="B-SUP",SupplierName="B Supplier"};
        var warehouseA=new Warehouse{WarehouseCode="A-WH",WarehouseName="A Warehouse"}; var warehouseB=new Warehouse{WarehouseCode="B-WH",WarehouseName="B Warehouse"};
        var productA=new Product{TenantId=tenantA,ProductCode="A-PROD",ProductName="A Product",CategoryId=Guid.NewGuid(),BrandId=Guid.NewGuid(),UnitId=Guid.NewGuid()};
        var productB=new Product{TenantId=tenantB,ProductCode="B-PROD",ProductName="B Product",CategoryId=Guid.NewGuid(),BrandId=Guid.NewGuid(),UnitId=Guid.NewGuid()};
        var purchaseA=new PurchaseInvoice{InvoiceNumber="A-PUR",InvoiceDate=DateTimeOffset.UtcNow,Status="POSTED",SupplierId=supplierA.SupplierId,WarehouseId=warehouseA.WarehouseId};
        var purchaseB=new PurchaseInvoice{InvoiceNumber="B-PUR",InvoiceDate=DateTimeOffset.UtcNow,Status="POSTED",SupplierId=supplierB.SupplierId,WarehouseId=warehouseB.WarehouseId};
        var balanceA=new InventoryBalance{ProductId=productA.ProductId,WarehouseId=warehouseA.WarehouseId,QuantityOnHand=5,AverageCost=10};
        var balanceB=new InventoryBalance{ProductId=productB.ProductId,WarehouseId=warehouseB.WarehouseId,QuantityOnHand=9,AverageCost=20};
        var txA=new InventoryTransaction{TransactionNo="A-TX",TransactionType="PURCHASE",WarehouseId=warehouseA.WarehouseId};
        var txB=new InventoryTransaction{TransactionNo="B-TX",TransactionType="PURCHASE",WarehouseId=warehouseB.WarehouseId};
        db.AddRange(saleA,saleB,supplierA,supplierB,warehouseA,warehouseB,productA,productB,purchaseA,purchaseB,balanceA,balanceB,txA,txB);
        Own(db,saleA,tenantA); Own(db,saleB,tenantB); Own(db,purchaseA,tenantA); Own(db,purchaseB,tenantB);
        Own(db,balanceA,tenantA); Own(db,balanceB,tenantB); Own(db,txA,tenantA); Own(db,txB,tenantB);
        Own(db,supplierA,tenantA); Own(db,supplierB,tenantB); Own(db,warehouseA,tenantA); Own(db,warehouseB,tenantB);
        db.SaveChanges();

        var user=new User(tenantB);
        (await new POSRepository(db,user).Invoices(null,null,null,null,1,20,default)).Item1.Select(x=>x.InvoiceNumber).Should().Equal("B-SALE");
        (await new PurchaseRepository(db,user).List(null,null,null,null,null,1,20,default)).Item1.Select(x=>x.InvoiceNumber).Should().Equal("B-PUR");
        (await new InventoryRepository(db,user).Balances(null,null,null,1,20,default)).Item1.Should().ContainSingle(x=>x.InventoryBalanceId==balanceB.InventoryBalanceId);
        (await new InventoryRepository(db,user).Transactions(null,null,null,null,null,1,20,default)).Item1.Should().ContainSingle(x=>x.TransactionId==txB.TransactionId);
    }

    [Fact]
    public async Task CustomerGroupsSuppliersAndProductMastersAreTenantVisibleOnly()
    {
        var tenantA=Guid.NewGuid(); var tenantB=Guid.NewGuid();
        await using var db=Db();
        var customerA=new Customer{TenantId=tenantA,CustomerCode="CA",CustomerName="Customer A"};
        var customerB=new Customer{TenantId=tenantB,CustomerCode="CB",CustomerName="Customer B"};
        var groupA=new CustomerGroup{TenantId=tenantA,GroupCode="GA",GroupName="Group A"};
        var groupB=new CustomerGroup{TenantId=tenantB,GroupCode="GB",GroupName="Group B"};
        var supplierA=new Supplier{SupplierCode="SA",SupplierName="Supplier A"};
        var supplierB=new Supplier{SupplierCode="SB",SupplierName="Supplier B"};
        var categoryA=new ProductCategory{CategoryCode="GROCERY",CategoryName="Grocery"};
        var categoryB=new ProductCategory{CategoryCode="GARMENTS",CategoryName="Garments"};
        var brandA=new Brand{BrandCode="FOOD",BrandName="Food Brand"}; var brandB=new Brand{BrandCode="FASHION",BrandName="Fashion Brand"};
        var unitA=new UnitOfMeasure{UnitCode="KG",UnitName="Kilogram",ShortName="kg"}; var unitB=new UnitOfMeasure{UnitCode="PCS",UnitName="Pieces",ShortName="pcs"};
        db.AddRange(customerA,customerB,groupA,groupB,supplierA,supplierB,categoryA,categoryB,brandA,brandB,unitA,unitB);
        Own(db,supplierA,tenantA); Own(db,supplierB,tenantB);
        db.Set<TenantProductCategory>().AddRange(new(){TenantId=tenantA,ProductCategoryId=categoryA.ProductCategoryId},new(){TenantId=tenantB,ProductCategoryId=categoryB.ProductCategoryId});
        db.Set<TenantBrand>().AddRange(new(){TenantId=tenantA,BrandId=brandA.BrandId},new(){TenantId=tenantB,BrandId=brandB.BrandId});
        db.Set<TenantUnitOfMeasure>().AddRange(new(){TenantId=tenantA,UnitId=unitA.UnitId},new(){TenantId=tenantB,UnitId=unitB.UnitId});
        db.SaveChanges();

        var user=new User(tenantB);
        (await new CustomerRepository(db,user).Search(null,null,"name",false,1,20,default)).Item1.Select(x=>x.CustomerCode).Should().Equal("CB");
        (await new CustomerGroupRepository(db,user).List(default)).Select(x=>x.GroupCode).Should().Equal("GB");
        (await new SupplierRepository(db,user).SearchAsync(null,null,"name",false,1,20,default)).Item1.Select(x=>x.SupplierCode).Should().Equal("SB");
        var products=new ProductRepository(db,user);
        (await products.GetCategoriesAsync(default)).Select(x=>x.CategoryCode).Should().Equal("GARMENTS");
        (await products.GetBrandsAsync(default)).Select(x=>x.BrandCode).Should().Equal("FASHION");
        (await products.GetUnitsAsync(default)).Select(x=>x.UnitCode).Should().Equal("PCS");
    }

    [Fact]
    public void MigrationScopesConfigurationAndAggregatesWithoutDefaultTenantFallback()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"database","WhatsBiz.Database","Scripts","V29-NewTenantIsolationAndOnboarding.sql"));
        sql.Should().Contain("EXEC(N'ALTER TABLE admin.Companies ADD TenantId")
            .And.Contain("EXEC(N'ALTER TABLE gst.GSTSettings ADD TenantId")
            .And.Contain("EXEC(N'ALTER TABLE printing.PrinterConfigurations ADD TenantId")
            .And.Contain("master.TenantProductCategories")
            .And.Contain("dashboard.Summary_Get @TenantId")
            .And.Contain("dashboard.Inventory_Get @TenantId")
            .And.Contain("gst.TaxSummary_Get @TenantId")
            .And.Contain("gst.SyncTransactions @TenantId")
            .And.Contain("inventory.StockAdjustment_List @TenantId")
            .And.Contain("inventory.StockTransfer_List @TenantId")
            .And.Contain("inventory.PhysicalVerification_List @TenantId")
            .And.NotContain("QA_DEFAULT");
    }

    [Fact]
    public void QaPlanGuardRejectsStaticTenantColumnReferencesInTheAddColumnBatch()
    {
        var script=File.ReadAllText(Path.Combine(Root(),"deployment","deploy-qa-database.ps1"));
        script.Should().Contain("$unsafeTenantAddBatches")
            .And.Contain("ADD TenantId followed by a same-batch static TenantId reference");
    }

    private static void Own(ApplicationDbContext db,object entity,Guid tenant)=>db.Entry(entity).Property("TenantId").CurrentValue=tenant;
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static string Root(){var x=new DirectoryInfo(AppContext.BaseDirectory);while(x is not null&&!Directory.Exists(Path.Combine(x.FullName,"database")))x=x.Parent;return x!.FullName;}
    private sealed class User(Guid tenant):ICurrentUserService{public Guid? UserId=>Guid.NewGuid();public Guid? TenantId=>tenant;public string? Username=>"tenant-test";public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[];}
}
