/* V29: retailer configuration/master visibility and first-login read isolation. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;

IF COL_LENGTH(N'admin.Companies',N'TenantId') IS NULL
 EXEC(N'ALTER TABLE admin.Companies ADD TenantId uniqueidentifier NULL;');
GO
IF OBJECT_ID(N'admin.FK_Companies_Tenants',N'F') IS NULL
 EXEC(N'ALTER TABLE admin.Companies ADD CONSTRAINT FK_Companies_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'admin.Companies') AND name=N'UX_Companies_Tenant')
 EXEC(N'CREATE UNIQUE INDEX UX_Companies_Tenant ON admin.Companies(TenantId) WHERE TenantId IS NOT NULL;');
GO

/* Resolve only companies whose branch warehouse provides one unambiguous owner. */
;WITH Ownership AS
(
 SELECT b.CompanyId,CONVERT(uniqueidentifier,MIN(CONVERT(char(36),w.TenantId))) TenantId
 FROM admin.Branches b JOIN inventory.Warehouses w ON w.WarehouseId=b.DefaultWarehouseId
 WHERE w.TenantId IS NOT NULL GROUP BY b.CompanyId HAVING COUNT(DISTINCT w.TenantId)=1
)
UPDATE c SET TenantId=o.TenantId FROM admin.Companies c JOIN Ownership o ON o.CompanyId=c.CompanyId WHERE c.TenantId IS NULL;

/* Give existing enrolled retailers that have no company a clean configuration root. */
INSERT admin.Companies(CompanyId,CompanyCode,CompanyName,LegalName,Country,IsActive,CreatedOn,TenantId)
SELECT NEWID(),N'RT-'+LEFT(REPLACE(CONVERT(nvarchar(36),t.TenantId),N'-',N''),27),t.Name,t.Name,N'India',1,SYSUTCDATETIME(),t.TenantId
FROM core.Tenants t
WHERE t.IsActive=1 AND NOT EXISTS(SELECT 1 FROM admin.Companies c WHERE c.TenantId=t.TenantId);

IF COL_LENGTH(N'gst.GSTSettings',N'TenantId') IS NULL
 EXEC(N'ALTER TABLE gst.GSTSettings ADD TenantId uniqueidentifier NULL;');
GO
IF OBJECT_ID(N'gst.FK_GSTSettings_Tenants',N'F') IS NULL
 EXEC(N'ALTER TABLE gst.GSTSettings ADD CONSTRAINT FK_GSTSettings_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'gst.GSTSettings') AND name=N'IX_GSTSettings_Tenant')
 EXEC(N'CREATE INDEX IX_GSTSettings_Tenant ON gst.GSTSettings(TenantId,IsActive,EffectiveDate DESC);');
GO
UPDATE g SET TenantId=c.TenantId FROM gst.GSTSettings g JOIN admin.Companies c ON c.GSTIN=g.CompanyGSTIN WHERE g.TenantId IS NULL AND c.TenantId IS NOT NULL;

IF COL_LENGTH(N'printing.PrinterConfigurations',N'TenantId') IS NULL
 EXEC(N'ALTER TABLE printing.PrinterConfigurations ADD TenantId uniqueidentifier NULL;');
GO
IF OBJECT_ID(N'printing.FK_PrinterConfigurations_Tenants',N'F') IS NULL
 EXEC(N'ALTER TABLE printing.PrinterConfigurations ADD CONSTRAINT FK_PrinterConfigurations_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'IX_PrinterConfigurations_Tenant')
 EXEC(N'CREATE INDEX IX_PrinterConfigurations_Tenant ON printing.PrinterConfigurations(TenantId,IsDefault,IsActive);');
GO

/* Resolve legacy printers only through an existing company printer selection. */
;WITH Ownership AS
(
 SELECT p.PrinterConfigurationId,CONVERT(uniqueidentifier,MIN(CONVERT(char(36),c.TenantId))) TenantId
 FROM printing.PrinterConfigurations p
 JOIN admin.PrinterSettings s ON p.PrinterConfigurationId IN(s.ThermalPrinterId,s.A4PrinterId,s.BarcodePrinterId)
 JOIN admin.Companies c ON c.CompanyId=s.CompanyId
 WHERE c.TenantId IS NOT NULL GROUP BY p.PrinterConfigurationId HAVING COUNT(DISTINCT c.TenantId)=1
)
UPDATE p SET TenantId=o.TenantId FROM printing.PrinterConfigurations p JOIN Ownership o ON o.PrinterConfigurationId=p.PrinterConfigurationId WHERE p.TenantId IS NULL;

IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'UX_PrinterConfigurations_Default')
 DROP INDEX UX_PrinterConfigurations_Default ON printing.PrinterConfigurations;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'UX_PrinterConfigurations_TenantDefault')
 CREATE UNIQUE INDEX UX_PrinterConfigurations_TenantDefault ON printing.PrinterConfigurations(TenantId) WHERE IsDefault=1 AND TenantId IS NOT NULL;
IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'UQ_PrinterConfigurations_Name')
 ALTER TABLE printing.PrinterConfigurations DROP CONSTRAINT UQ_PrinterConfigurations_Name;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'UX_PrinterConfigurations_TenantName')
 CREATE UNIQUE INDEX UX_PrinterConfigurations_TenantName ON printing.PrinterConfigurations(TenantId,PrinterName) WHERE TenantId IS NOT NULL;

MERGE master.TenantProductCategories t USING(SELECT DISTINCT TenantId,CategoryId ProductCategoryId FROM master.Products WHERE TenantId IS NOT NULL)s ON s.TenantId=t.TenantId AND s.ProductCategoryId=t.ProductCategoryId WHEN NOT MATCHED THEN INSERT VALUES(s.TenantId,s.ProductCategoryId);
MERGE master.TenantBrands t USING(SELECT DISTINCT TenantId,BrandId FROM master.Products WHERE TenantId IS NOT NULL)s ON s.TenantId=t.TenantId AND s.BrandId=t.BrandId WHEN NOT MATCHED THEN INSERT VALUES(s.TenantId,s.BrandId);
MERGE master.TenantUnitsOfMeasure t USING(SELECT DISTINCT TenantId,UnitId FROM master.Products WHERE TenantId IS NOT NULL)s ON s.TenantId=t.TenantId AND s.UnitId=t.UnitId WHEN NOT MATCHED THEN INSERT VALUES(s.TenantId,s.UnitId);

MERGE printing.DocumentTemplates t USING(VALUES
 (N'GST-INVOICE-A4',N'GST Invoice - A4',N'SALES_INVOICE',N'A4',N'<main>{{BodyHtml}}</main>'),
 (N'GST-INVOICE-80MM',N'GST Invoice - 80mm',N'SALES_INVOICE',N'80MM',N'<main>{{BodyHtml}}</main>'),
 (N'GST-INVOICE-58MM',N'GST Invoice - 58mm',N'SALES_INVOICE',N'58MM',N'<main>{{BodyHtml}}</main>'))
 s(TemplateCode,TemplateName,DocumentType,PaperType,HtmlTemplate) ON t.TemplateCode=s.TemplateCode
WHEN MATCHED THEN UPDATE SET TemplateName=s.TemplateName,DocumentType=s.DocumentType,PaperType=s.PaperType,HtmlTemplate=s.HtmlTemplate,IsActive=1
WHEN NOT MATCHED THEN INSERT(TemplateCode,TemplateName,DocumentType,PaperType,HtmlTemplate,IsDefault,IsActive) VALUES(s.TemplateCode,s.TemplateName,s.DocumentType,s.PaperType,s.HtmlTemplate,CASE WHEN s.PaperType=N'80MM' THEN 1 ELSE 0 END,1);
GO

CREATE OR ALTER PROCEDURE dashboard.Summary_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset AS
BEGIN
 SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT ISNULL((SELECT SUM(GrandTotal) FROM sales.SalesInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED')),0),
 ISNULL((SELECT SUM(GrandTotal) FROM purchase.PurchaseInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND IsDeleted=0 AND Status NOT IN(N'DRAFT',N'CANCELLED')),0),
 ISNULL((SELECT SUM(p.Amount) FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=p.PaymentMethodId WHERE p.PaymentDate>=@From AND p.PaymentDate<@To AND p.Status=N'COMPLETED' AND m.MethodCode=N'CASH'),0),
 ISNULL((SELECT SUM(p.Amount) FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=p.PaymentMethodId WHERE p.PaymentDate>=@From AND p.PaymentDate<@To AND p.Status=N'COMPLETED' AND m.MethodCode=N'UPI'),0),
 ISNULL((SELECT SUM(p.Amount) FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=p.PaymentMethodId WHERE p.PaymentDate>=@From AND p.PaymentDate<@To AND p.Status=N'COMPLETED' AND m.MethodCode=N'CARD'),0),
 ISNULL((SELECT SUM((d.Quantity-d.ReturnedQuantity)*(d.UnitPrice-p.PurchasePrice)-d.DiscountAmount) FROM sales.SalesInvoiceItems d JOIN sales.SalesInvoices i ON i.InvoiceId=d.InvoiceId AND i.TenantId=@TenantId JOIN master.Products p ON p.ProductId=d.ProductId WHERE i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED')),0),
 ISNULL((SELECT SUM(e.Amount) FROM purchase.PurchaseExpenses e JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=e.PurchaseInvoiceId AND i.TenantId=@TenantId WHERE i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.IsDeleted=0),0),
 ISNULL((SELECT SUM(p.Amount) FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=@TenantId WHERE p.PaymentDate>=@From AND p.PaymentDate<@To AND p.Status=N'COMPLETED'),0)-ISNULL((SELECT SUM(p.Amount) FROM purchase.PurchasePayments p JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=p.PurchaseInvoiceId AND i.TenantId=@TenantId WHERE p.PaymentDate>=@From AND p.PaymentDate<@To AND p.Status=N'COMPLETED'),0);
 SELECT TOP(10)InvoiceId,InvoiceNumber,InvoiceDate,GrandTotal,Status FROM sales.SalesInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To ORDER BY InvoiceDate DESC;
 SELECT TOP(10)PurchaseInvoiceId,InvoiceNumber,InvoiceDate,GrandTotal,Status FROM purchase.PurchaseInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND IsDeleted=0 ORDER BY InvoiceDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dashboard.Purchase_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT CONVERT(varchar(10),CONVERT(date,InvoiceDate),23),SUM(GrandTotal) FROM purchase.PurchaseInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND IsDeleted=0 AND Status NOT IN(N'DRAFT',N'CANCELLED') GROUP BY CONVERT(date,InvoiceDate) ORDER BY CONVERT(date,InvoiceDate);
 SELECT CONCAT(YEAR(InvoiceDate),N'-',RIGHT(N'0'+CONVERT(varchar(2),MONTH(InvoiceDate)),2)),SUM(GrandTotal) FROM purchase.PurchaseInvoices WHERE TenantId=@TenantId AND InvoiceDate>=DATEADD(month,-12,@To) AND InvoiceDate<@To AND IsDeleted=0 AND Status NOT IN(N'DRAFT',N'CANCELLED') GROUP BY YEAR(InvoiceDate),MONTH(InvoiceDate) ORDER BY YEAR(InvoiceDate),MONTH(InvoiceDate);
 SELECT s.SupplierName,SUM(i.GrandTotal) FROM purchase.PurchaseInvoices i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId AND s.TenantId=@TenantId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.IsDeleted=0 AND i.Status NOT IN(N'DRAFT',N'CANCELLED') GROUP BY s.SupplierName ORDER BY SUM(i.GrandTotal) DESC;
END;
GO

CREATE OR ALTER PROCEDURE dashboard.Sales_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT CONVERT(varchar(2),DATEPART(hour,InvoiceDate)),SUM(GrandTotal) FROM sales.SalesInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY DATEPART(hour,InvoiceDate) ORDER BY DATEPART(hour,InvoiceDate);
 SELECT CONVERT(varchar(10),CONVERT(date,InvoiceDate),23),SUM(GrandTotal) FROM sales.SalesInvoices WHERE TenantId=@TenantId AND InvoiceDate>=@From AND InvoiceDate<@To AND Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY CONVERT(date,InvoiceDate) ORDER BY CONVERT(date,InvoiceDate);
 SELECT CONCAT(YEAR(InvoiceDate),N'-',RIGHT(N'0'+CONVERT(varchar(2),MONTH(InvoiceDate)),2)),SUM(GrandTotal) FROM sales.SalesInvoices WHERE TenantId=@TenantId AND InvoiceDate>=DATEADD(month,-12,@To) AND InvoiceDate<@To AND Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY YEAR(InvoiceDate),MONTH(InvoiceDate) ORDER BY YEAR(InvoiceDate),MONTH(InvoiceDate);
 SELECT CONVERT(varchar(4),YEAR(InvoiceDate)),SUM(GrandTotal) FROM sales.SalesInvoices WHERE TenantId=@TenantId AND Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY YEAR(InvoiceDate) ORDER BY YEAR(InvoiceDate);
 SELECT c.CategoryName,SUM(d.LineTotal) FROM sales.SalesInvoices i JOIN sales.SalesInvoiceItems d ON d.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId JOIN master.ProductCategories c ON c.ProductCategoryId=p.CategoryId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY c.CategoryName ORDER BY SUM(d.LineTotal) DESC;
 SELECT b.BrandName,SUM(d.LineTotal) FROM sales.SalesInvoices i JOIN sales.SalesInvoiceItems d ON d.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId JOIN master.Brands b ON b.BrandId=p.BrandId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY b.BrandName ORDER BY SUM(d.LineTotal) DESC;
 SELECT m.MethodName,SUM(x.Amount) FROM sales.SalesPayments x JOIN sales.SalesInvoices i ON i.InvoiceId=x.InvoiceId AND i.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=x.PaymentMethodId WHERE x.PaymentDate>=@From AND x.PaymentDate<@To AND x.Status=N'COMPLETED' GROUP BY m.MethodName ORDER BY SUM(x.Amount) DESC;
 SELECT TOP(10)p.ProductCode,SUM(d.Quantity-d.ReturnedQuantity) FROM sales.SalesInvoices i JOIN sales.SalesInvoiceItems d ON d.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY p.ProductCode ORDER BY SUM(d.Quantity-d.ReturnedQuantity) DESC;
 SELECT TOP(10)p.ProductCode,SUM(d.Quantity-d.ReturnedQuantity) FROM sales.SalesInvoices i JOIN sales.SalesInvoiceItems d ON d.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY p.ProductCode ORDER BY SUM(d.Quantity-d.ReturnedQuantity);
 SELECT TOP(10)p.ProductCode,SUM((d.Quantity-d.ReturnedQuantity)*(d.UnitPrice-p.PurchasePrice)-d.DiscountAmount) FROM sales.SalesInvoices i JOIN sales.SalesInvoiceItems d ON d.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId WHERE i.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY p.ProductCode ORDER BY SUM((d.Quantity-d.ReturnedQuantity)*(d.UnitPrice-p.PurchasePrice)-d.DiscountAmount) DESC;
END;
GO

CREATE OR ALTER PROCEDURE dashboard.Inventory_Get @TenantId uniqueidentifier AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT ISNULL(SUM(b.QuantityOnHand*b.AverageCost),0),COUNT(DISTINCT CASE WHEN b.QuantityAvailable>0 AND b.QuantityAvailable<=p.ReorderLevel THEN b.ProductId END),COUNT(DISTINCT CASE WHEN b.QuantityAvailable=0 THEN b.ProductId END),COUNT(DISTINCT CASE WHEN b.QuantityAvailable<0 THEN b.ProductId END),
 ISNULL((SELECT COUNT(*) FROM inventory.InventoryAlerts a JOIN master.Products x ON x.ProductId=a.ProductId WHERE x.TenantId=@TenantId AND a.Status=N'ACTIVE' AND a.AlertType=N'EXPIRING_SOON'),0),ISNULL((SELECT COUNT(*) FROM inventory.InventoryAlerts a JOIN master.Products x ON x.ProductId=a.ProductId WHERE x.TenantId=@TenantId AND a.Status=N'ACTIVE' AND a.AlertType=N'EXPIRED_STOCK'),0),ISNULL((SELECT COUNT(*) FROM inventory.ReorderSuggestions r JOIN master.Products x ON x.ProductId=r.ProductId WHERE x.TenantId=@TenantId AND r.Status=N'OPEN'),0)
 FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId AND p.TenantId=@TenantId WHERE b.TenantId=@TenantId;
 SELECT TOP(20)p.ProductCode,b.QuantityAvailable FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId AND p.TenantId=@TenantId WHERE b.TenantId=@TenantId AND b.QuantityAvailable<=p.ReorderLevel ORDER BY b.QuantityAvailable;
END;
GO

CREATE OR ALTER PROCEDURE inventory.InventoryAlert_List @TenantId uniqueidentifier,@WarehouseId uniqueidentifier=NULL,@CategoryId uniqueidentifier=NULL,@ProductId uniqueidentifier=NULL,@Search nvarchar(100)=NULL,@Status nvarchar(30)=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT a.InventoryAlertId,a.ProductId,p.ProductCode,p.ProductName,a.WarehouseId,w.WarehouseName,a.AlertType,a.CurrentQuantity,a.SuggestedQuantity,a.GeneratedOn,a.Status,a.Detail FROM inventory.InventoryAlerts a JOIN master.Products p ON p.ProductId=a.ProductId AND p.TenantId=@TenantId JOIN inventory.Warehouses w ON w.WarehouseId=a.WarehouseId AND w.TenantId=@TenantId WHERE a.Status=N'ACTIVE' AND(@WarehouseId IS NULL OR a.WarehouseId=@WarehouseId)AND(@CategoryId IS NULL OR p.CategoryId=@CategoryId)AND(@ProductId IS NULL OR a.ProductId=@ProductId)AND(@Status IS NULL OR a.AlertType=@Status)AND(@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%' OR p.ProductName LIKE N'%'+@Search+N'%') ORDER BY a.GeneratedOn DESC;
END;
GO

CREATE OR ALTER PROCEDURE inventory.ReorderSuggestion_List @TenantId uniqueidentifier,@WarehouseId uniqueidentifier=NULL,@CategoryId uniqueidentifier=NULL,@ProductId uniqueidentifier=NULL,@Search nvarchar(100)=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT r.ReorderSuggestionId,r.ProductId,p.ProductCode,p.ProductName,r.WarehouseId,w.WarehouseName,N'REORDER',r.CurrentStock,r.SuggestedQuantity,r.GeneratedOn,r.Status,CONCAT(N'Pending: ',r.PendingPurchase,N', daily velocity: ',r.SalesVelocity) FROM inventory.ReorderSuggestions r JOIN master.Products p ON p.ProductId=r.ProductId AND p.TenantId=@TenantId JOIN inventory.Warehouses w ON w.WarehouseId=r.WarehouseId AND w.TenantId=@TenantId WHERE(@WarehouseId IS NULL OR r.WarehouseId=@WarehouseId)AND(@CategoryId IS NULL OR p.CategoryId=@CategoryId)AND(@ProductId IS NULL OR r.ProductId=@ProductId)AND(@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%' OR p.ProductName LIKE N'%'+@Search+N'%') ORDER BY r.SuggestedQuantity DESC;
END;
GO

CREATE OR ALTER PROCEDURE inventory.StockMovementHistory_List @TenantId uniqueidentifier,@WarehouseId uniqueidentifier=NULL,@ProductId uniqueidentifier=NULL,@From datetimeoffset=NULL,@To datetimeoffset=NULL,@Search nvarchar(100)=NULL,@PageNumber int=1,@PageSize int=20 AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 SELECT h.StockMovementHistoryId,h.MovementDate,h.MovementType,t.TransactionNo,h.ProductId,p.ProductCode,p.ProductName,h.WarehouseId,w.WarehouseName,h.ZoneId,h.BinId,h.Quantity,h.BalanceAfter,h.ReferenceType,h.Remarks,h.CreatedBy FROM inventory.StockMovementHistory h JOIN inventory.InventoryTransactions t ON t.TransactionId=h.TransactionId AND t.TenantId=@TenantId JOIN master.Products p ON p.ProductId=h.ProductId AND p.TenantId=@TenantId JOIN inventory.Warehouses w ON w.WarehouseId=h.WarehouseId AND w.TenantId=@TenantId WHERE(@WarehouseId IS NULL OR h.WarehouseId=@WarehouseId)AND(@ProductId IS NULL OR h.ProductId=@ProductId)AND(@From IS NULL OR h.MovementDate>=@From)AND(@To IS NULL OR h.MovementDate<=@To)AND(@Search IS NULL OR t.TransactionNo LIKE N'%'+@Search+N'%'OR p.ProductCode LIKE N'%'+@Search+N'%'OR p.ProductName LIKE N'%'+@Search+N'%') ORDER BY h.MovementDate DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM inventory.StockMovementHistory h JOIN inventory.InventoryTransactions t ON t.TransactionId=h.TransactionId AND t.TenantId=@TenantId JOIN master.Products p ON p.ProductId=h.ProductId AND p.TenantId=@TenantId WHERE(@WarehouseId IS NULL OR h.WarehouseId=@WarehouseId)AND(@ProductId IS NULL OR h.ProductId=@ProductId)AND(@From IS NULL OR h.MovementDate>=@From)AND(@To IS NULL OR h.MovementDate<=@To)AND(@Search IS NULL OR t.TransactionNo LIKE N'%'+@Search+N'%'OR p.ProductCode LIKE N'%'+@Search+N'%'OR p.ProductName LIKE N'%'+@Search+N'%');
END;
GO

CREATE OR ALTER PROCEDURE inventory.StockAdjustment_List @TenantId uniqueidentifier,@Search nvarchar(100)=NULL,@WarehouseId uniqueidentifier=NULL,@From datetimeoffset=NULL,@To datetimeoffset=NULL,@PageNumber int=1,@PageSize int=20 AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 WITH q AS(SELECT a.StockAdjustmentId Id,a.AdjustmentNo Number,t.TransactionDate Date,CONCAT(a.AdjustmentType,N' / ',a.ReasonCode) Type,a.ApprovalStatus Status,t.Remarks,SUM(ISNULL(i.Quantity,0)) TotalQuantity,COUNT(i.StockAdjustmentItemId) ItemCount FROM inventory.StockAdjustments a JOIN inventory.InventoryTransactions t ON t.TransactionId=a.TransactionId AND t.TenantId=@TenantId LEFT JOIN inventory.StockAdjustmentItems i ON i.StockAdjustmentId=a.StockAdjustmentId WHERE(@Search IS NULL OR a.AdjustmentNo LIKE N'%'+@Search+N'%' OR a.ReasonCode LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)AND(@From IS NULL OR t.TransactionDate>=@From)AND(@To IS NULL OR t.TransactionDate<=@To)GROUP BY a.StockAdjustmentId,a.AdjustmentNo,t.TransactionDate,a.AdjustmentType,a.ReasonCode,a.ApprovalStatus,t.Remarks) SELECT * FROM q ORDER BY Date DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM inventory.StockAdjustments a JOIN inventory.InventoryTransactions t ON t.TransactionId=a.TransactionId AND t.TenantId=@TenantId WHERE(@Search IS NULL OR a.AdjustmentNo LIKE N'%'+@Search+N'%' OR a.ReasonCode LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)AND(@From IS NULL OR t.TransactionDate>=@From)AND(@To IS NULL OR t.TransactionDate<=@To);
END;
GO

CREATE OR ALTER PROCEDURE inventory.StockTransfer_List @TenantId uniqueidentifier,@Search nvarchar(100)=NULL,@WarehouseId uniqueidentifier=NULL,@From datetimeoffset=NULL,@To datetimeoffset=NULL,@PageNumber int=1,@PageSize int=20 AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 WITH q AS(SELECT h.StockTransferId Id,h.TransferNo Number,h.TransferDate Date,N'TRANSFER' Type,h.ApprovalStatus Status,h.Remarks,SUM(ISNULL(i.Quantity,0)) TotalQuantity,COUNT(i.StockTransferItemId) ItemCount FROM inventory.StockTransfers h JOIN inventory.Warehouses sw ON sw.WarehouseId=h.SourceWarehouseId AND sw.TenantId=@TenantId JOIN inventory.Warehouses dw ON dw.WarehouseId=h.DestinationWarehouseId AND dw.TenantId=@TenantId LEFT JOIN inventory.StockTransferItems i ON i.StockTransferId=h.StockTransferId WHERE(@Search IS NULL OR h.TransferNo LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR h.SourceWarehouseId=@WarehouseId OR h.DestinationWarehouseId=@WarehouseId)AND(@From IS NULL OR h.TransferDate>=@From)AND(@To IS NULL OR h.TransferDate<=@To)GROUP BY h.StockTransferId,h.TransferNo,h.TransferDate,h.ApprovalStatus,h.Remarks) SELECT * FROM q ORDER BY Date DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM inventory.StockTransfers h JOIN inventory.Warehouses sw ON sw.WarehouseId=h.SourceWarehouseId AND sw.TenantId=@TenantId JOIN inventory.Warehouses dw ON dw.WarehouseId=h.DestinationWarehouseId AND dw.TenantId=@TenantId WHERE(@Search IS NULL OR h.TransferNo LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR h.SourceWarehouseId=@WarehouseId OR h.DestinationWarehouseId=@WarehouseId)AND(@From IS NULL OR h.TransferDate>=@From)AND(@To IS NULL OR h.TransferDate<=@To);
END;
GO

CREATE OR ALTER PROCEDURE inventory.PhysicalVerification_List @TenantId uniqueidentifier,@Search nvarchar(100)=NULL,@WarehouseId uniqueidentifier=NULL,@From datetimeoffset=NULL,@To datetimeoffset=NULL,@PageNumber int=1,@PageSize int=20 AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 WITH q AS(SELECT h.VerificationId Id,h.VerificationNo Number,h.VerificationDate Date,N'PHYSICAL_COUNT' Type,h.ApprovalStatus Status,h.Remarks,SUM(ABS(ISNULL(i.VarianceQuantity,0))) TotalQuantity,COUNT(i.VerificationItemId) ItemCount FROM inventory.PhysicalStockVerification h JOIN inventory.Warehouses w ON w.WarehouseId=h.WarehouseId AND w.TenantId=@TenantId LEFT JOIN inventory.PhysicalStockVerificationItems i ON i.VerificationId=h.VerificationId WHERE(@Search IS NULL OR h.VerificationNo LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR h.WarehouseId=@WarehouseId)GROUP BY h.VerificationId,h.VerificationNo,h.VerificationDate,h.ApprovalStatus,h.Remarks) SELECT * FROM q ORDER BY Date DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM inventory.PhysicalStockVerification h JOIN inventory.Warehouses w ON w.WarehouseId=h.WarehouseId AND w.TenantId=@TenantId WHERE(@Search IS NULL OR h.VerificationNo LIKE N'%'+@Search+N'%')AND(@WarehouseId IS NULL OR h.WarehouseId=@WarehouseId);
END;
GO

CREATE OR ALTER PROCEDURE gst.SyncTransactions @TenantId uniqueidentifier AS
BEGIN
 SET NOCOUNT ON;
 IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1;
 DECLARE @state char(2)=(SELECT TOP(1) StateCode FROM gst.GSTSettings WHERE TenantId=@TenantId AND IsActive=1 ORDER BY EffectiveDate DESC);
 IF @state IS NULL SELECT TOP(1) @state=StateCode FROM admin.Companies WHERE TenantId=@TenantId AND IsActive=1;
 MERGE gst.GSTTransactions t USING
 (
  SELECT N'SALE' SourceType,h.InvoiceId SourceId,i.InvoiceItemId SourceLineId,h.InvoiceNumber DocumentNumber,h.InvoiceDate DocumentDate,h.CustomerId PartyId,c.CustomerName PartyName,c.GSTIN PartyGSTIN,h.WarehouseId,i.ProductId,p.HSNCode,p.SACCode,i.TaxPercentage GSTRate,(i.Quantity-i.ReturnedQuantity) Quantity,ROUND((i.Quantity-i.ReturnedQuantity)*i.UnitPrice-i.DiscountAmount,2) TaxableAmount,CASE WHEN LEFT(ISNULL(c.GSTIN,@state),2)=@state THEN ROUND(i.TaxAmount/2,2) ELSE 0 END CGSTAmount,CASE WHEN LEFT(ISNULL(c.GSTIN,@state),2)=@state THEN i.TaxAmount-ROUND(i.TaxAmount/2,2) ELSE 0 END SGSTAmount,CASE WHEN LEFT(ISNULL(c.GSTIN,@state),2)<>@state THEN i.TaxAmount ELSE 0 END IGSTAmount,CAST(0 AS decimal(18,2)) CessAmount,h.RoundOff,CONVERT(bit,CASE WHEN c.GSTIN IS NOT NULL THEN 1 ELSE 0 END) IsB2B,LEFT(ISNULL(c.GSTIN,@state),2) PlaceOfSupply
  FROM sales.SalesInvoices h JOIN sales.SalesInvoiceItems i ON i.InvoiceId=h.InvoiceId JOIN master.Products p ON p.ProductId=i.ProductId AND p.TenantId=@TenantId LEFT JOIN sales.Customers c ON c.CustomerId=h.CustomerId AND c.TenantId=@TenantId WHERE h.TenantId=@TenantId AND h.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED')
  UNION ALL
  SELECT N'PURCHASE',h.PurchaseInvoiceId,i.PurchaseItemId,h.InvoiceNumber,h.InvoiceDate,h.SupplierId,s.SupplierName,s.GSTIN,h.WarehouseId,i.ProductId,p.HSNCode,p.SACCode,i.GSTPercentage,i.Quantity+i.FreeQuantity-i.ReturnedQuantity,ROUND(i.Quantity*i.PurchasePrice-i.DiscountAmount,2),CASE WHEN LEFT(ISNULL(s.GSTIN,@state),2)=@state THEN ROUND(i.GSTAmount/2,2) ELSE 0 END,CASE WHEN LEFT(ISNULL(s.GSTIN,@state),2)=@state THEN i.GSTAmount-ROUND(i.GSTAmount/2,2) ELSE 0 END,CASE WHEN LEFT(ISNULL(s.GSTIN,@state),2)<>@state THEN i.GSTAmount ELSE 0 END,0,h.RoundOff,CONVERT(bit,CASE WHEN s.GSTIN IS NOT NULL THEN 1 ELSE 0 END),LEFT(ISNULL(s.GSTIN,@state),2)
  FROM purchase.PurchaseInvoices h JOIN purchase.PurchaseInvoiceItems i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId JOIN master.Products p ON p.ProductId=i.ProductId AND p.TenantId=@TenantId JOIN purchase.Suppliers s ON s.SupplierId=h.SupplierId AND s.TenantId=@TenantId WHERE h.TenantId=@TenantId AND h.IsDeleted=0 AND h.Status NOT IN(N'DRAFT',N'CANCELLED')
 )s ON t.SourceType=s.SourceType AND t.SourceLineId=s.SourceLineId
 WHEN MATCHED THEN UPDATE SET DocumentNumber=s.DocumentNumber,DocumentDate=s.DocumentDate,PartyId=s.PartyId,PartyName=s.PartyName,PartyGSTIN=s.PartyGSTIN,WarehouseId=s.WarehouseId,ProductId=s.ProductId,HSNCode=s.HSNCode,SACCode=s.SACCode,GSTRate=s.GSTRate,Quantity=s.Quantity,TaxableAmount=s.TaxableAmount,CGSTAmount=s.CGSTAmount,SGSTAmount=s.SGSTAmount,IGSTAmount=s.IGSTAmount,CessAmount=s.CessAmount,RoundOff=s.RoundOff,IsB2B=s.IsB2B,PlaceOfSupply=s.PlaceOfSupply
 WHEN NOT MATCHED THEN INSERT(SourceType,SourceId,SourceLineId,DocumentNumber,DocumentDate,PartyId,PartyName,PartyGSTIN,WarehouseId,ProductId,HSNCode,SACCode,GSTRate,Quantity,TaxableAmount,CGSTAmount,SGSTAmount,IGSTAmount,CessAmount,RoundOff,IsB2B,PlaceOfSupply) VALUES(s.SourceType,s.SourceId,s.SourceLineId,s.DocumentNumber,s.DocumentDate,s.PartyId,s.PartyName,s.PartyGSTIN,s.WarehouseId,s.ProductId,s.HSNCode,s.SACCode,s.GSTRate,s.Quantity,s.TaxableAmount,s.CGSTAmount,s.SGSTAmount,s.IGSTAmount,s.CessAmount,s.RoundOff,s.IsB2B,s.PlaceOfSupply);
END;
GO

CREATE OR ALTER PROCEDURE gst.TaxSummary_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset,@WarehouseId uniqueidentifier=NULL,@GSTRate decimal(5,2)=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC gst.SyncTransactions @TenantId;
 SELECT g.SourceType,g.GSTRate,SUM(g.TaxableAmount),SUM(g.CGSTAmount),SUM(g.SGSTAmount),SUM(g.IGSTAmount),SUM(g.CessAmount),SUM(g.CGSTAmount+g.SGSTAmount+g.IGSTAmount+g.CessAmount) FROM gst.GSTTransactions g WHERE g.DocumentDate>=@From AND g.DocumentDate<@To AND(@WarehouseId IS NULL OR g.WarehouseId=@WarehouseId)AND(@GSTRate IS NULL OR g.GSTRate=@GSTRate)AND((g.SourceType=N'SALE' AND EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.InvoiceId=g.SourceId AND i.TenantId=@TenantId))OR(g.SourceType=N'PURCHASE' AND EXISTS(SELECT 1 FROM purchase.PurchaseInvoices i WHERE i.PurchaseInvoiceId=g.SourceId AND i.TenantId=@TenantId))) GROUP BY g.SourceType,g.GSTRate ORDER BY g.SourceType,g.GSTRate;
END;
GO

CREATE OR ALTER PROCEDURE gst.Register_Get @TenantId uniqueidentifier,@SourceType nvarchar(10),@From datetimeoffset,@To datetimeoffset,@WarehouseId uniqueidentifier=NULL,@PartyId uniqueidentifier=NULL,@GSTRate decimal(5,2)=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC gst.SyncTransactions @TenantId;
 SELECT g.GSTTransactionId,g.DocumentNumber,g.DocumentDate,g.PartyId,g.PartyName,g.PartyGSTIN,g.WarehouseId,g.ProductId,g.HSNCode,g.SACCode,g.GSTRate,g.Quantity,g.TaxableAmount,g.CGSTAmount,g.SGSTAmount,g.IGSTAmount,g.CessAmount,g.ReverseChargeAmount,g.TaxAdjustment,g.RoundOff,g.IsB2B,g.PlaceOfSupply FROM gst.GSTTransactions g WHERE g.SourceType=@SourceType AND g.DocumentDate>=@From AND g.DocumentDate<@To AND(@WarehouseId IS NULL OR g.WarehouseId=@WarehouseId)AND(@PartyId IS NULL OR g.PartyId=@PartyId)AND(@GSTRate IS NULL OR g.GSTRate=@GSTRate)AND((g.SourceType=N'SALE' AND EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.InvoiceId=g.SourceId AND i.TenantId=@TenantId))OR(g.SourceType=N'PURCHASE' AND EXISTS(SELECT 1 FROM purchase.PurchaseInvoices i WHERE i.PurchaseInvoiceId=g.SourceId AND i.TenantId=@TenantId))) ORDER BY g.DocumentDate,g.DocumentNumber;
END;
GO

CREATE OR ALTER PROCEDURE gst.HSNSummary_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset,@WarehouseId uniqueidentifier=NULL,@GSTRate decimal(5,2)=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC gst.SyncTransactions @TenantId;
 SELECT g.SourceType,COALESCE(NULLIF(g.HSNCode,N''),NULLIF(g.SACCode,N''),N'UNCLASSIFIED'),g.GSTRate,SUM(g.Quantity),SUM(g.TaxableAmount),SUM(g.CGSTAmount),SUM(g.SGSTAmount),SUM(g.IGSTAmount),SUM(g.CessAmount) FROM gst.GSTTransactions g WHERE g.DocumentDate>=@From AND g.DocumentDate<@To AND(@WarehouseId IS NULL OR g.WarehouseId=@WarehouseId)AND(@GSTRate IS NULL OR g.GSTRate=@GSTRate)AND((g.SourceType=N'SALE' AND EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.InvoiceId=g.SourceId AND i.TenantId=@TenantId))OR(g.SourceType=N'PURCHASE' AND EXISTS(SELECT 1 FROM purchase.PurchaseInvoices i WHERE i.PurchaseInvoiceId=g.SourceId AND i.TenantId=@TenantId))) GROUP BY g.SourceType,COALESCE(NULLIF(g.HSNCode,N''),NULLIF(g.SACCode,N''),N'UNCLASSIFIED'),g.GSTRate ORDER BY 2,g.GSTRate;
END;
GO

CREATE OR ALTER PROCEDURE gst.GSTR1_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset,@WarehouseId uniqueidentifier=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC gst.SyncTransactions @TenantId;
 SELECT CASE WHEN g.IsB2B=1 THEN N'B2B' ELSE N'B2C' END,g.GSTRate,COUNT(DISTINCT g.SourceId),SUM(g.TaxableAmount),SUM(g.CGSTAmount),SUM(g.SGSTAmount),SUM(g.IGSTAmount),SUM(g.CessAmount) FROM gst.GSTTransactions g WHERE g.SourceType=N'SALE' AND g.DocumentDate>=@From AND g.DocumentDate<@To AND(@WarehouseId IS NULL OR g.WarehouseId=@WarehouseId)AND EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.InvoiceId=g.SourceId AND i.TenantId=@TenantId) GROUP BY g.IsB2B,g.GSTRate ORDER BY 1,g.GSTRate;
END;
GO

CREATE OR ALTER PROCEDURE gst.GSTR3B_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset,@WarehouseId uniqueidentifier=NULL AS
BEGIN SET NOCOUNT ON; IF @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC gst.SyncTransactions @TenantId;
 SELECT SUM(CASE WHEN g.SourceType=N'SALE'THEN g.TaxableAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'SALE'THEN g.CGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'SALE'THEN g.SGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'SALE'THEN g.IGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'PURCHASE'THEN g.CGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'PURCHASE'THEN g.SGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'PURCHASE'THEN g.IGSTAmount ELSE 0 END),SUM(CASE WHEN g.SourceType=N'SALE'THEN g.CGSTAmount+g.SGSTAmount+g.IGSTAmount+g.CessAmount ELSE 0 END)-SUM(CASE WHEN g.SourceType=N'PURCHASE'THEN g.CGSTAmount+g.SGSTAmount+g.IGSTAmount+g.CessAmount ELSE 0 END) FROM gst.GSTTransactions g WHERE g.DocumentDate>=@From AND g.DocumentDate<@To AND(@WarehouseId IS NULL OR g.WarehouseId=@WarehouseId)AND((g.SourceType=N'SALE' AND EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.InvoiceId=g.SourceId AND i.TenantId=@TenantId))OR(g.SourceType=N'PURCHASE' AND EXISTS(SELECT 1 FROM purchase.PurchaseInvoices i WHERE i.PurchaseInvoiceId=g.SourceId AND i.TenantId=@TenantId)));
END;
GO
