/* Phase 3: deterministic finance ownership, runtime guards, and posting repair. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

/* Existing installations retain the historical backfill by default. Fresh
   production initialization sets FreshProductionInitialization=True and
   installs only the current procedure/guard definitions below. */
IF N'$(FreshProductionInitialization)' <> N'True'
BEGIN
DECLARE @Ownership TABLE(JournalEntryId uniqueidentifier NOT NULL,TenantId uniqueidentifier NOT NULL,OwnershipPath nvarchar(100) NOT NULL);
INSERT @Ownership SELECT j.JournalEntryId,i.TenantId,N'SALE' FROM finance.JournalEntries j JOIN sales.SalesInvoices i ON i.InvoiceId=j.ReferenceId WHERE j.ReferenceType IN(N'SALE',N'SALE_RETURN') AND i.TenantId IS NOT NULL;
INSERT @Ownership SELECT j.JournalEntryId,i.TenantId,N'PURCHASE' FROM finance.JournalEntries j JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=j.ReferenceId WHERE j.ReferenceType IN(N'PURCHASE',N'PURCHASE_RETURN') AND i.TenantId IS NOT NULL;
INSERT @Ownership SELECT j.JournalEntryId,i.TenantId,N'SALE_PAYMENT' FROM finance.JournalEntries j JOIN sales.SalesPayments p ON p.PaymentId=j.ReferenceId JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId WHERE j.ReferenceType=N'SALE_PAYMENT' AND i.TenantId IS NOT NULL;
INSERT @Ownership SELECT j.JournalEntryId,i.TenantId,N'PURCHASE_PAYMENT' FROM finance.JournalEntries j JOIN purchase.PurchasePayments p ON p.PurchasePaymentId=j.ReferenceId JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=p.PurchaseInvoiceId WHERE j.ReferenceType=N'PURCHASE_PAYMENT' AND i.TenantId IS NOT NULL;
INSERT @Ownership SELECT DISTINCT j.JournalEntryId,c.TenantId,N'CUSTOMER' FROM finance.JournalEntries j JOIN finance.CustomerLedger l ON l.JournalEntryId=j.JournalEntryId JOIN sales.Customers c ON c.CustomerId=l.CustomerId WHERE j.ReferenceType=N'CUSTOMER' AND c.TenantId IS NOT NULL;
INSERT @Ownership SELECT DISTINCT j.JournalEntryId,s.TenantId,N'SUPPLIER' FROM finance.JournalEntries j JOIN finance.SupplierLedger l ON l.JournalEntryId=j.JournalEntryId JOIN purchase.Suppliers s ON s.SupplierId=l.SupplierId WHERE j.ReferenceType=N'SUPPLIER' AND s.TenantId IS NOT NULL;
INSERT @Ownership SELECT j.JournalEntryId,t.TenantId,N'STOCK_ADJUSTMENT' FROM finance.JournalEntries j JOIN inventory.InventoryTransactions t ON t.TransactionId=j.ReferenceId WHERE j.ReferenceType=N'STOCK_ADJUSTMENT' AND t.TenantId IS NOT NULL;

BEGIN TRY
 BEGIN TRAN;
 ;WITH Deterministic AS
 (
   SELECT JournalEntryId,CONVERT(uniqueidentifier,MIN(CONVERT(char(36),TenantId))) TenantId
   FROM @Ownership GROUP BY JournalEntryId HAVING COUNT(DISTINCT TenantId)=1
 )
 UPDATE j SET TenantId=d.TenantId
 FROM finance.JournalEntries j JOIN Deterministic d ON d.JournalEntryId=j.JournalEntryId
 WHERE j.TenantId IS NULL;
 COMMIT;
END TRY
BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;

SELECT COUNT_BIG(*) TotalJournals,COUNT_BIG(TenantId) OwnedJournals,
       SUM(CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END) UnownedJournals
FROM finance.JournalEntries;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.Outstanding_List @TenantId uniqueidentifier,@PartyType nvarchar(10),@PartyId uniqueidentifier=NULL,@Search nvarchar(100)=NULL,@AgeBucket nvarchar(20)=NULL,@OnlyOpen bit=1
AS
BEGIN
 SET NOCOUNT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR (@SessionTenant IS NOT NULL AND @TenantId<>@SessionTenant) THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC finance.RefreshOutstanding @TenantId=@TenantId;
 IF @PartyType=N'CUSTOMER' SELECT o.CustomerOutstandingId Id,o.CustomerId PartyId,c.CustomerCode PartyCode,c.CustomerName PartyName,o.InvoiceId,o.InvoiceNumber,o.InvoiceDate,o.DueDate,o.InvoiceAmount,o.ReceivedAmount PaidAmount,o.OutstandingAmount,o.AgeDays,o.AgeBucket FROM finance.CustomerOutstanding o JOIN sales.Customers c ON c.CustomerId=o.CustomerId AND c.TenantId=@TenantId JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId AND i.TenantId=@TenantId WHERE(@PartyId IS NULL OR o.CustomerId=@PartyId) AND(@OnlyOpen=0 OR o.OutstandingAmount>0) AND(@AgeBucket IS NULL OR o.AgeBucket=@AgeBucket) AND(@Search IS NULL OR c.CustomerName LIKE N'%'+@Search+N'%' OR o.InvoiceNumber LIKE N'%'+@Search+N'%') ORDER BY o.DueDate;
 ELSE IF @PartyType=N'SUPPLIER' SELECT o.SupplierOutstandingId,o.SupplierId,s.SupplierCode,s.SupplierName,o.PurchaseInvoiceId,o.InvoiceNumber,o.InvoiceDate,o.DueDate,o.InvoiceAmount,o.PaidAmount,o.OutstandingAmount,o.AgeDays,o.AgeBucket FROM finance.SupplierOutstanding o JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId AND s.TenantId=@TenantId JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId AND i.TenantId=@TenantId WHERE(@PartyId IS NULL OR o.SupplierId=@PartyId) AND(@OnlyOpen=0 OR o.OutstandingAmount>0) AND(@AgeBucket IS NULL OR o.AgeBucket=@AgeBucket) AND(@Search IS NULL OR s.SupplierName LIKE N'%'+@Search+N'%' OR o.InvoiceNumber LIKE N'%'+@Search+N'%') ORDER BY o.DueDate;
 ELSE THROW 51518,N'Unsupported outstanding party type.',1;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.ReceivablePayable_List @TenantId uniqueidentifier,@Kind nvarchar(20),@Search nvarchar(100)=NULL,@PartyId uniqueidentifier=NULL,@From datetimeoffset=NULL,@To datetimeoffset=NULL,@PageNumber int=1,@PageSize int=20
AS
BEGIN
 SET NOCOUNT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR (@SessionTenant IS NOT NULL AND @TenantId<>@SessionTenant) THROW 51500,N'Tenant context is missing or mismatched.',1;
 IF @Kind=N'RECEIPT' BEGIN SELECT r.ReceiptId Id,r.ReceiptNumber Number,r.ReceiptDate EntryDate,c.CustomerCode PartyCode,c.CustomerName PartyName,r.ReceiptType EntryType,r.TotalAmount,r.AllocatedAmount,r.OnAccountAmount,r.Status,r.ReferenceNumber,r.Remarks FROM finance.Receipts r JOIN sales.Customers c ON c.CustomerId=r.CustomerId AND c.TenantId=@TenantId WHERE(@PartyId IS NULL OR r.CustomerId=@PartyId)AND(@Search IS NULL OR r.ReceiptNumber LIKE N'%'+@Search+N'%' OR c.CustomerName LIKE N'%'+@Search+N'%')AND(@From IS NULL OR r.ReceiptDate>=@From)AND(@To IS NULL OR r.ReceiptDate<=@To) ORDER BY r.ReceiptDate DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; SELECT COUNT(*) FROM finance.Receipts r JOIN sales.Customers c ON c.CustomerId=r.CustomerId AND c.TenantId=@TenantId WHERE(@PartyId IS NULL OR r.CustomerId=@PartyId)AND(@Search IS NULL OR r.ReceiptNumber LIKE N'%'+@Search+N'%' OR c.CustomerName LIKE N'%'+@Search+N'%')AND(@From IS NULL OR r.ReceiptDate>=@From)AND(@To IS NULL OR r.ReceiptDate<=@To); END
 ELSE IF @Kind=N'PAYMENT' BEGIN SELECT p.PaymentId,p.PaymentNumber,p.PaymentDate,s.SupplierCode,s.SupplierName,p.PaymentType,p.TotalAmount,p.AllocatedAmount,p.OnAccountAmount,p.Status,p.ReferenceNumber,p.Remarks FROM finance.Payments p JOIN purchase.Suppliers s ON s.SupplierId=p.SupplierId AND s.TenantId=@TenantId WHERE(@PartyId IS NULL OR p.SupplierId=@PartyId)AND(@Search IS NULL OR p.PaymentNumber LIKE N'%'+@Search+N'%' OR s.SupplierName LIKE N'%'+@Search+N'%')AND(@From IS NULL OR p.PaymentDate>=@From)AND(@To IS NULL OR p.PaymentDate<=@To) ORDER BY p.PaymentDate DESC OFFSET(CASE WHEN @PageNumber<1 THEN 0 ELSE @PageNumber-1 END)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; SELECT COUNT(*) FROM finance.Payments p JOIN purchase.Suppliers s ON s.SupplierId=p.SupplierId AND s.TenantId=@TenantId WHERE(@PartyId IS NULL OR p.SupplierId=@PartyId)AND(@Search IS NULL OR p.PaymentNumber LIKE N'%'+@Search+N'%' OR s.SupplierName LIKE N'%'+@Search+N'%')AND(@From IS NULL OR p.PaymentDate>=@From)AND(@To IS NULL OR p.PaymentDate<=@To); END
 ELSE THROW 51519,N'Unsupported finance history kind.',1;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dashboard.Finance_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset
AS
BEGIN
 SET NOCOUNT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC finance.RefreshOutstanding @TenantId=@TenantId;
 SELECT ISNULL((SELECT SUM(b.AmountIn-b.AmountOut) FROM finance.CashBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId WHERE j.TenantId=@TenantId),0) CashBalance,ISNULL((SELECT SUM(b.AmountIn-b.AmountOut) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId WHERE j.TenantId=@TenantId),0) BankBalance,ISNULL((SELECT SUM(o.OutstandingAmount) FROM finance.CustomerOutstanding o JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId WHERE i.TenantId=@TenantId AND o.OutstandingAmount>0),0) Receivables,ISNULL((SELECT SUM(o.OutstandingAmount) FROM finance.SupplierOutstanding o JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId WHERE i.TenantId=@TenantId AND o.OutstandingAmount>0),0) Payables,ISNULL((SELECT SUM((i.Quantity-i.ReturnedQuantity)*(i.UnitPrice-p.PurchasePrice)-i.DiscountAmount) FROM sales.SalesInvoiceItems i JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId AND h.TenantId=@TenantId JOIN master.Products p ON p.ProductId=i.ProductId AND p.TenantId=@TenantId WHERE h.InvoiceDate>=@From AND h.InvoiceDate<@To AND h.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED')),0)-ISNULL((SELECT SUM(e.Amount) FROM purchase.PurchaseExpenses e JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=e.PurchaseInvoiceId WHERE h.TenantId=@TenantId AND h.InvoiceDate>=@From AND h.InvoiceDate<@To),0) ProfitToday;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dashboard.Customers_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset
AS
BEGIN
 SET NOCOUNT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC finance.RefreshOutstanding @TenantId=@TenantId;
 SELECT (SELECT COUNT(*) FROM sales.Customers WHERE TenantId=@TenantId AND CreatedOn>=@From AND CreatedOn<@To AND IsDeleted=0) NewCustomers,ISNULL((SELECT SUM(o.OutstandingAmount) FROM finance.CustomerOutstanding o JOIN sales.Customers c ON c.CustomerId=o.CustomerId WHERE c.TenantId=@TenantId AND o.OutstandingAmount>0),0) CustomerOutstanding,(SELECT COUNT(*) FROM sales.Customers c WHERE c.TenantId=@TenantId AND c.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM sales.SalesInvoices i WHERE i.CustomerId=c.CustomerId AND i.TenantId=@TenantId AND i.InvoiceDate>=DATEADD(day,-90,@To))) InactiveCustomers;
 SELECT TOP(10)c.CustomerName Label,SUM(i.GrandTotal) Value FROM sales.Customers c JOIN sales.SalesInvoices i ON i.CustomerId=c.CustomerId AND i.TenantId=@TenantId WHERE c.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.Status IN(N'COMPLETED',N'PARTIALLY_RETURNED',N'RETURNED') GROUP BY c.CustomerName ORDER BY Value DESC;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dashboard.Suppliers_Get @TenantId uniqueidentifier,@From datetimeoffset,@To datetimeoffset
AS
BEGIN
 SET NOCOUNT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1; EXEC finance.RefreshOutstanding @TenantId=@TenantId;
 SELECT ISNULL((SELECT SUM(o.OutstandingAmount) FROM finance.SupplierOutstanding o JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId WHERE s.TenantId=@TenantId AND o.OutstandingAmount>0),0) SupplierOutstanding,(SELECT COUNT(*) FROM finance.SupplierOutstanding o JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId WHERE s.TenantId=@TenantId AND o.OutstandingAmount>0) PendingPayments;
 SELECT TOP(10)s.SupplierName Label,SUM(i.GrandTotal) Value FROM purchase.Suppliers s JOIN purchase.PurchaseInvoices i ON i.SupplierId=s.SupplierId AND i.TenantId=@TenantId WHERE s.TenantId=@TenantId AND i.InvoiceDate>=@From AND i.InvoiceDate<@To AND i.IsDeleted=0 AND i.Status NOT IN(N'DRAFT',N'CANCELLED') GROUP BY s.SupplierName ORDER BY Value DESC;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dashboard.Notifications_Get @TenantId uniqueidentifier,@Top int=50
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 EXEC finance.RefreshOutstanding @TenantId=@TenantId;
 ;WITH TenantNotifications AS
 (
  SELECT a.InventoryAlertId DashboardNotificationId,a.AlertType NotificationType,
   CONVERT(nvarchar(20),CASE WHEN a.AlertType IN(N'NEGATIVE_STOCK',N'EXPIRED_STOCK') THEN N'CRITICAL' ELSE N'WARNING' END) Severity,
   CONVERT(nvarchar(250),REPLACE(a.AlertType,N'_',N' ')) Title,a.Detail Message,
   CONVERT(nvarchar(50),N'INVENTORY_ALERT') ReferenceType,a.InventoryAlertId ReferenceId,
   CONVERT(bit,0) IsRead,a.GeneratedOn
  FROM inventory.InventoryAlerts a
  JOIN master.Products p ON p.ProductId=a.ProductId AND p.TenantId=@TenantId
  JOIN inventory.Warehouses w ON w.WarehouseId=a.WarehouseId AND w.TenantId=@TenantId
  WHERE a.Status=N'ACTIVE'
  UNION ALL
  SELECT o.CustomerOutstandingId,N'PENDING_COLLECTION',N'WARNING',N'Customer payment pending',
   CONCAT(N'Invoice: ',o.InvoiceNumber,N' | Outstanding amount: ',FORMAT(o.OutstandingAmount,N'N2')),
   N'CUSTOMER_OUTSTANDING',o.InvoiceId,CONVERT(bit,0),o.LastUpdated
  FROM finance.CustomerOutstanding o JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId AND i.TenantId=@TenantId
  WHERE o.OutstandingAmount>0
  UNION ALL
  SELECT o.SupplierOutstandingId,N'PENDING_PAYMENT',N'WARNING',N'Supplier payment pending',
   CONCAT(N'Invoice: ',o.InvoiceNumber,N' | Outstanding amount: ',FORMAT(o.OutstandingAmount,N'N2')),
   N'SUPPLIER_OUTSTANDING',o.PurchaseInvoiceId,CONVERT(bit,0),o.LastUpdated
  FROM finance.SupplierOutstanding o JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId AND i.TenantId=@TenantId
  WHERE o.OutstandingAmount>0
 )
 SELECT TOP(@Top) DashboardNotificationId,NotificationType,Severity,Title,Message,ReferenceType,ReferenceId,IsRead,GeneratedOn
 FROM TenantNotifications ORDER BY CASE Severity WHEN N'CRITICAL' THEN 1 WHEN N'WARNING' THEN 2 ELSE 3 END,GeneratedOn DESC;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.Receipt_Post
 @TenantId uniqueidentifier,@CustomerId uniqueidentifier,@ReceiptType nvarchar(30),@ReceiptDate datetimeoffset,@ReferenceNumber nvarchar(100)=NULL,@Remarks nvarchar(1000)=NULL,@ItemsJson nvarchar(max),@AllocationsJson nvarchar(max)=NULL,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1; IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE CustomerId=@CustomerId AND TenantId=@TenantId AND IsDeleted=0) THROW 51517,N'Finance party was not found for this tenant.',1;
 BEGIN TRAN; BEGIN TRY
  DECLARE @id uniqueidentifier=NEWID(),@no nvarchar(50)=CONCAT(N'RCT-',FORMAT(SYSUTCDATETIME(),N'yyyyMMddHHmmssfff')),@total decimal(18,2),@allocated decimal(18,2);
  DECLARE @items table(PaymentMode nvarchar(30),Amount decimal(18,2),ReferenceNumber nvarchar(100),ChequeNumber nvarchar(50),ChequeDate date,BankName nvarchar(150)); INSERT @items SELECT * FROM OPENJSON(@ItemsJson) WITH(PaymentMode nvarchar(30)'$.PaymentMode',Amount decimal(18,2)'$.Amount',ReferenceNumber nvarchar(100)'$.ReferenceNumber',ChequeNumber nvarchar(50)'$.ChequeNumber',ChequeDate date'$.ChequeDate',BankName nvarchar(150)'$.BankName');
  DECLARE @alloc table(InvoiceId uniqueidentifier,Amount decimal(18,2)); INSERT @alloc SELECT * FROM OPENJSON(ISNULL(@AllocationsJson,N'[]')) WITH(InvoiceId uniqueidentifier'$.InvoiceId',Amount decimal(18,2)'$.Amount');
  SELECT @total=SUM(Amount) FROM @items; SELECT @allocated=ISNULL(SUM(Amount),0) FROM @alloc; IF ISNULL(@total,0)<=0 OR EXISTS(SELECT 1 FROM @items WHERE Amount<=0) OR @allocated>@total THROW 51320,N'Receipt totals or allocations are invalid.',1;
  IF EXISTS(SELECT 1 FROM @alloc a LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=a.InvoiceId AND i.CustomerId=@CustomerId AND i.TenantId=@TenantId WHERE i.InvoiceId IS NULL OR a.Amount<=0 OR a.Amount>i.BalanceAmount) THROW 51321,N'Receipt allocation exceeds invoice outstanding or invoice is invalid.',1;
  INSERT finance.Receipts(ReceiptId,ReceiptNumber,ReceiptDate,CustomerId,ReceiptType,TotalAmount,AllocatedAmount,ReferenceNumber,Remarks,CreatedBy) VALUES(@id,@no,@ReceiptDate,@CustomerId,@ReceiptType,@total,@allocated,@ReferenceNumber,@Remarks,@CreatedBy);
  INSERT finance.ReceiptItems(ReceiptId,PaymentModeId,Amount,ReferenceNumber,ChequeNumber,ChequeDate,BankName) SELECT @id,m.PaymentModeId,i.Amount,i.ReferenceNumber,i.ChequeNumber,i.ChequeDate,i.BankName FROM @items i JOIN finance.PaymentModes m ON m.ModeCode=i.PaymentMode AND m.IsActive=1; IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @items) THROW 51322,N'One or more payment modes are invalid.',1;
  INSERT finance.ReceiptAllocation(ReceiptId,InvoiceId,AllocatedAmount,CreatedBy) SELECT @id,InvoiceId,Amount,@CreatedBy FROM @alloc; UPDATE i SET PaidAmount=i.PaidAmount+a.Amount,ModifiedOn=SYSUTCDATETIME() FROM sales.SalesInvoices i JOIN @alloc a ON a.InvoiceId=i.InvoiceId WHERE i.TenantId=@TenantId;
  DECLARE @mode nvarchar(30),@amount decimal(18,2),@itemref nvarchar(100),@journal uniqueidentifier,@jn nvarchar(50); DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT PaymentMode,Amount,COALESCE(ReferenceNumber,@ReferenceNumber) FROM @items; OPEN c; FETCH NEXT FROM c INTO @mode,@amount,@itemref;
  WHILE @@FETCH_STATUS=0 BEGIN DECLARE @r table(Id uniqueidentifier,Number nvarchar(50)); INSERT @r EXEC finance.PostPartyTransaction @TenantId=@TenantId,@TransactionType=N'RECEIPT',@PartyType=N'CUSTOMER',@PartyId=@CustomerId,@PaymentMode=@mode,@Amount=@amount,@EntryDate=@ReceiptDate,@ReferenceNumber=@itemref,@Narration=@Remarks,@CreatedBy=@CreatedBy,@SourceId=NULL; SELECT TOP(1) @journal=Id,@jn=Number FROM @r; UPDATE TOP(1) finance.ReceiptItems SET JournalEntryId=@journal WHERE ReceiptId=@id AND JournalEntryId IS NULL AND Amount=@amount; DELETE @r; FETCH NEXT FROM c INTO @mode,@amount,@itemref; END; CLOSE c; DEALLOCATE c;
  EXEC finance.RefreshOutstanding @TenantId=@TenantId; COMMIT; SELECT @id DocumentId,@no DocumentNumber,@total TotalAmount,@allocated AllocatedAmount;
 END TRY BEGIN CATCH IF CURSOR_STATUS('local','c')>=0 CLOSE c; IF CURSOR_STATUS('local','c')>=-1 DEALLOCATE c; IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.Payment_Post
 @TenantId uniqueidentifier,@SupplierId uniqueidentifier,@PaymentType nvarchar(30),@PaymentDate datetimeoffset,@ReferenceNumber nvarchar(100)=NULL,@Remarks nvarchar(1000)=NULL,@ItemsJson nvarchar(max),@AllocationsJson nvarchar(max)=NULL,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1; IF NOT EXISTS(SELECT 1 FROM purchase.Suppliers WHERE SupplierId=@SupplierId AND TenantId=@TenantId AND IsDeleted=0) THROW 51517,N'Finance party was not found for this tenant.',1;
 BEGIN TRAN; BEGIN TRY
  DECLARE @id uniqueidentifier=NEWID(),@no nvarchar(50)=CONCAT(N'PAY-',FORMAT(SYSUTCDATETIME(),N'yyyyMMddHHmmssfff')),@total decimal(18,2),@allocated decimal(18,2);
  DECLARE @items table(PaymentMode nvarchar(30),Amount decimal(18,2),ReferenceNumber nvarchar(100),ChequeNumber nvarchar(50),ChequeDate date,BankName nvarchar(150)); INSERT @items SELECT * FROM OPENJSON(@ItemsJson) WITH(PaymentMode nvarchar(30)'$.PaymentMode',Amount decimal(18,2)'$.Amount',ReferenceNumber nvarchar(100)'$.ReferenceNumber',ChequeNumber nvarchar(50)'$.ChequeNumber',ChequeDate date'$.ChequeDate',BankName nvarchar(150)'$.BankName');
  DECLARE @alloc table(PurchaseInvoiceId uniqueidentifier,Amount decimal(18,2)); INSERT @alloc SELECT * FROM OPENJSON(ISNULL(@AllocationsJson,N'[]')) WITH(PurchaseInvoiceId uniqueidentifier'$.PurchaseInvoiceId',Amount decimal(18,2)'$.Amount');
  SELECT @total=SUM(Amount) FROM @items; SELECT @allocated=ISNULL(SUM(Amount),0) FROM @alloc; IF ISNULL(@total,0)<=0 OR EXISTS(SELECT 1 FROM @items WHERE Amount<=0) OR @allocated>@total THROW 51330,N'Payment totals or allocations are invalid.',1;
  IF EXISTS(SELECT 1 FROM @alloc a LEFT JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=a.PurchaseInvoiceId AND i.SupplierId=@SupplierId AND i.TenantId=@TenantId WHERE i.PurchaseInvoiceId IS NULL OR a.Amount<=0 OR a.Amount>i.BalanceAmount) THROW 51331,N'Payment allocation exceeds invoice outstanding or invoice is invalid.',1;
  INSERT finance.Payments(PaymentId,PaymentNumber,PaymentDate,SupplierId,PaymentType,TotalAmount,AllocatedAmount,ReferenceNumber,Remarks,CreatedBy) VALUES(@id,@no,@PaymentDate,@SupplierId,@PaymentType,@total,@allocated,@ReferenceNumber,@Remarks,@CreatedBy);
  INSERT finance.PaymentItems(PaymentId,PaymentModeId,Amount,ReferenceNumber,ChequeNumber,ChequeDate,BankName) SELECT @id,m.PaymentModeId,i.Amount,i.ReferenceNumber,i.ChequeNumber,i.ChequeDate,i.BankName FROM @items i JOIN finance.PaymentModes m ON m.ModeCode=i.PaymentMode AND m.IsActive=1; IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @items) THROW 51332,N'One or more payment modes are invalid.',1;
  INSERT finance.PaymentAllocation(PaymentId,PurchaseInvoiceId,AllocatedAmount,CreatedBy) SELECT @id,PurchaseInvoiceId,Amount,@CreatedBy FROM @alloc; UPDATE i SET PaidAmount=i.PaidAmount+a.Amount,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy FROM purchase.PurchaseInvoices i JOIN @alloc a ON a.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE i.TenantId=@TenantId;
  DECLARE @mode nvarchar(30),@amount decimal(18,2),@itemref nvarchar(100),@journal uniqueidentifier,@jn nvarchar(50); DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT PaymentMode,Amount,COALESCE(ReferenceNumber,@ReferenceNumber) FROM @items; OPEN c; FETCH NEXT FROM c INTO @mode,@amount,@itemref;
  WHILE @@FETCH_STATUS=0 BEGIN DECLARE @r table(Id uniqueidentifier,Number nvarchar(50)); INSERT @r EXEC finance.PostPartyTransaction @TenantId=@TenantId,@TransactionType=N'PAYMENT',@PartyType=N'SUPPLIER',@PartyId=@SupplierId,@PaymentMode=@mode,@Amount=@amount,@EntryDate=@PaymentDate,@ReferenceNumber=@itemref,@Narration=@Remarks,@CreatedBy=@CreatedBy,@SourceId=NULL; SELECT TOP(1) @journal=Id,@jn=Number FROM @r; UPDATE TOP(1) finance.PaymentItems SET JournalEntryId=@journal WHERE PaymentId=@id AND JournalEntryId IS NULL AND Amount=@amount; DELETE @r; FETCH NEXT FROM c INTO @mode,@amount,@itemref; END; CLOSE c; DEALLOCATE c;
  EXEC finance.RefreshOutstanding @TenantId=@TenantId; COMMIT; SELECT @id DocumentId,@no DocumentNumber,@total TotalAmount,@allocated AllocatedAmount;
 END TRY BEGIN CATCH IF CURSOR_STATUS('local','c')>=0 CLOSE c; IF CURSOR_STATUS('local','c')>=-1 DEALLOCATE c; IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.PostPartyTransaction
 @TenantId uniqueidentifier,@TransactionType nvarchar(10),@PartyType nvarchar(10),@PartyId uniqueidentifier,@PaymentMode nvarchar(30),@Amount decimal(18,2),@EntryDate datetimeoffset,@ReferenceNumber nvarchar(100)=NULL,@Narration nvarchar(500)=NULL,@CreatedBy nvarchar(256)=NULL,@SourceId uniqueidentifier=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 IF @TransactionType NOT IN(N'RECEIPT',N'PAYMENT') OR @PartyType NOT IN(N'CUSTOMER',N'SUPPLIER') OR @Amount<=0 THROW 51300,N'Invalid financial transaction.',1;
 IF (@PartyType=N'CUSTOMER' AND NOT EXISTS(SELECT 1 FROM sales.Customers WHERE CustomerId=@PartyId AND TenantId=@TenantId AND IsDeleted=0)) OR (@PartyType=N'SUPPLIER' AND NOT EXISTS(SELECT 1 FROM purchase.Suppliers WHERE SupplierId=@PartyId AND TenantId=@TenantId AND IsDeleted=0)) THROW 51517,N'Finance party was not found for this tenant.',1;
 DECLARE @ModeId uniqueidentifier,@Book nvarchar(10); SELECT @ModeId=PaymentModeId,@Book=BookType FROM finance.PaymentModes WHERE ModeCode=@PaymentMode AND IsActive=1; IF @ModeId IS NULL OR @Book=N'CREDIT' THROW 51300,N'Cash or bank payment mode is required.',1;
 BEGIN TRAN;
 DECLARE @Source uniqueidentifier=ISNULL(@SourceId,NEWID()),@J uniqueidentifier=NEWID(),@No nvarchar(50)=CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),@Cash uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'CASH'),@Bank uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'BANK'),@Party uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=CASE WHEN @PartyType=N'CUSTOMER' THEN N'CUSTOMER' ELSE N'SUPPLIER' END),@BookAccount uniqueidentifier;
 SET @BookAccount=CASE WHEN @Book=N'CASH' THEN @Cash ELSE @Bank END; IF @Party IS NULL OR @BookAccount IS NULL BEGIN ROLLBACK; THROW 51503,N'Finance account configuration is incomplete.',1; END;
 INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@EntryDate,@TransactionType,@PartyType,@Source,@Narration,@CreatedBy,@TenantId);
 IF @TransactionType=N'RECEIPT' INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@BookAccount,@Amount,0),(@J,@Party,0,@Amount); ELSE INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Party,@Amount,0),(@J,@BookAccount,0,@Amount);
 INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,ReferenceId,DebitAmount,CreditAmount,Narration) SELECT @J,AccountId,@EntryDate,@PartyType,@Source,DebitAmount,CreditAmount,@Narration FROM finance.JournalEntryDetails WHERE JournalEntryId=@J;
 INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,DebitTotal,CreditTotal,Narration) VALUES(@J,@EntryDate,@TransactionType,@PartyType,@Source,@ReferenceNumber,@Amount,@Amount,@Narration);
 IF @PartyType=N'CUSTOMER' INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@PartyId,@J,@EntryDate,@TransactionType,@Source,@ReferenceNumber,CASE WHEN @TransactionType=N'PAYMENT' THEN @Amount ELSE 0 END,CASE WHEN @TransactionType=N'RECEIPT' THEN @Amount ELSE 0 END,@Narration); ELSE INSERT finance.SupplierLedger(SupplierId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@PartyId,@J,@EntryDate,@TransactionType,@Source,@ReferenceNumber,CASE WHEN @TransactionType=N'RECEIPT' THEN @Amount ELSE 0 END,CASE WHEN @TransactionType=N'PAYMENT' THEN @Amount ELSE 0 END,@Narration);
 IF @Book=N'CASH' INSERT finance.CashBook(JournalEntryId,EntryDate,EntryType,ReferenceId,AmountIn,AmountOut,Narration) VALUES(@J,@EntryDate,CASE WHEN @TransactionType=N'RECEIPT' THEN N'CASH IN' ELSE N'CASH OUT' END,@Source,CASE WHEN @TransactionType=N'RECEIPT' THEN @Amount ELSE 0 END,CASE WHEN @TransactionType=N'PAYMENT' THEN @Amount ELSE 0 END,@Narration); ELSE INSERT finance.BankBook(JournalEntryId,PaymentModeId,EntryDate,EntryType,ReferenceId,ReferenceNumber,AmountIn,AmountOut,Narration) VALUES(@J,@ModeId,@EntryDate,@TransactionType,@Source,@ReferenceNumber,CASE WHEN @TransactionType=N'RECEIPT' THEN @Amount ELSE 0 END,CASE WHEN @TransactionType=N'PAYMENT' THEN @Amount ELSE 0 END,@Narration);
 COMMIT; SELECT @J JournalEntryId,@No JournalNumber;
END;
GO
