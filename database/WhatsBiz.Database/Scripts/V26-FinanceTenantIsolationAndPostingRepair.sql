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

/* Backfill only a single source-derived tenant. Existing ownership is immutable. */
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
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.PostSource
 @TenantId uniqueidentifier=NULL,@SourceType nvarchar(30),@SourceId uniqueidentifier,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @SourceType NOT IN(N'PURCHASE',N'SALE',N'PURCHASE_RETURN',N'SALE_RETURN') THROW 51501,N'Unsupported finance source type.',1;
 /* V18/V24 internal callers omit the new parameter. Resolve ownership from the
    authoritative source document; SESSION_CONTEXT remains an independent guard. */
 IF @TenantId IS NULL
  SELECT @TenantId=CASE WHEN @SourceType IN(N'SALE',N'SALE_RETURN') THEN
   (SELECT TenantId FROM sales.SalesInvoices WHERE InvoiceId=@SourceId)
   ELSE (SELECT TenantId FROM purchase.PurchaseInvoices WHERE PurchaseInvoiceId=@SourceId) END;
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=@SourceType AND ReferenceId=@SourceId AND TransactionType=@SourceType AND TenantId<>@TenantId) THROW 51502,N'Finance source is owned by another tenant.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=@SourceType AND ReferenceId=@SourceId AND TransactionType=@SourceType AND TenantId=@TenantId) RETURN;
 DECLARE @J uniqueidentifier=NEWID(),@No nvarchar(50)=CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),@Date datetimeoffset,@RefNo nvarchar(50),@Narr nvarchar(500),@Customer uniqueidentifier,@Supplier uniqueidentifier,@Grand decimal(18,2),@Tax decimal(18,2),@Net decimal(18,2),@Paid decimal(18,2)=0;
 DECLARE @Cash uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'CASH'),@Bank uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'BANK'),@Cust uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'CUSTOMER'),@Supp uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'SUPPLIER'),@Inv uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'INVENTORY'),@InGST uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'INPUT_GST'),@OutGST uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'OUTPUT_GST'),@Sales uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'SALES'),@PRet uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'PURCHASE_RETURN'),@SRet uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'SALES_RETURN');
 IF @Cash IS NULL OR @Bank IS NULL OR @Cust IS NULL OR @Supp IS NULL OR @Inv IS NULL OR @InGST IS NULL OR @OutGST IS NULL OR @Sales IS NULL OR @PRet IS NULL OR @SRet IS NULL THROW 51503,N'Finance account configuration is incomplete.',1;
 IF @SourceType=N'PURCHASE'
 BEGIN
  SELECT @Date=i.InvoiceDate,@RefNo=i.InvoiceNumber,@Supplier=i.SupplierId,@Grand=i.GrandTotal,@Tax=i.TaxAmount,@Net=i.GrandTotal-i.TaxAmount,@Narr=i.Remarks FROM purchase.PurchaseInvoices i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId AND s.TenantId=@TenantId WHERE i.PurchaseInvoiceId=@SourceId AND i.TenantId=@TenantId AND i.Status<>N'DRAFT';
  IF @Grand IS NULL THROW 51504,N'Purchase source was not found for this tenant.',1; IF @Grand<=0 THROW 51505,N'Purchase journal total must be positive.',1;
  SELECT @Paid=ISNULL(SUM(p.Amount),0) FROM purchase.PurchasePayments p WHERE p.PurchaseInvoiceId=@SourceId AND p.Status=N'COMPLETED';
  INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,@SourceType,@SourceType,@SourceId,@Narr,@CreatedBy,@TenantId);
  IF @Net>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Inv,@Net,0,N'Inventory and landed cost');
  IF @Tax>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@InGST,@Tax,0,N'Input GST');
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Supp,0,@Grand,N'Supplier payable');
  IF @Paid>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Supp,@Paid,0,N'Supplier payments');
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) SELECT @J,CASE WHEN fm.BookType=N'CASH' THEN @Cash ELSE @Bank END,0,SUM(p.Amount),pm.MethodCode FROM purchase.PurchasePayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId JOIN finance.PaymentModes fm ON fm.ModeCode=pm.MethodCode WHERE p.PurchaseInvoiceId=@SourceId AND p.Status=N'COMPLETED' AND fm.BookType<>N'CREDIT' GROUP BY fm.BookType,pm.MethodCode HAVING SUM(p.Amount)>0;
  INSERT finance.SupplierLedger(SupplierId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Supplier,@J,@Date,N'PURCHASE',@SourceId,@RefNo,@Grand,0,@Narr);
  IF @Paid>0 INSERT finance.SupplierLedger(SupplierId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Supplier,@J,@Date,N'PAYMENT',@SourceId,@RefNo,0,@Paid,N'Purchase payment');
  INSERT finance.CashBook(JournalEntryId,EntryDate,EntryType,ReferenceId,AmountOut,Narration) SELECT @J,p.PaymentDate,N'CASH OUT',@SourceId,p.Amount,N'Purchase payment' FROM purchase.PurchasePayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId WHERE p.PurchaseInvoiceId=@SourceId AND pm.MethodCode=N'CASH' AND p.Amount>0;
  INSERT finance.BankBook(JournalEntryId,PaymentModeId,EntryDate,EntryType,ReferenceId,ReferenceNumber,AmountOut,Narration) SELECT @J,fm.PaymentModeId,p.PaymentDate,N'PAYMENT',@SourceId,p.ReferenceNumber,p.Amount,N'Purchase payment' FROM purchase.PurchasePayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId JOIN finance.PaymentModes fm ON fm.ModeCode=pm.MethodCode WHERE p.PurchaseInvoiceId=@SourceId AND fm.BookType=N'BANK' AND p.Amount>0;
 END
 ELSE IF @SourceType=N'SALE'
 BEGIN
  SELECT @Date=i.InvoiceDate,@RefNo=i.InvoiceNumber,@Customer=i.CustomerId,@Grand=i.GrandTotal,@Tax=i.TaxAmount,@Net=i.GrandTotal-i.TaxAmount,@Narr=i.Remarks FROM sales.SalesInvoices i LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE i.InvoiceId=@SourceId AND i.TenantId=@TenantId AND (i.CustomerId IS NULL OR c.TenantId=@TenantId) AND i.Status NOT IN(N'HELD',N'SUSPENDED',N'CANCELLED',N'VOID');
  IF @Grand IS NULL THROW 51506,N'Sale source was not found for this tenant.',1; IF @Grand<=0 THROW 51507,N'Sale journal total must be positive.',1;
  SELECT @Paid=ISNULL(SUM(p.Amount),0) FROM sales.SalesPayments p WHERE p.InvoiceId=@SourceId AND p.Status=N'COMPLETED';
  INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,@SourceType,@SourceType,@SourceId,@Narr,@CreatedBy,@TenantId);
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Cust,@Grand,0,N'Customer receivable');
  IF @Net>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Sales,0,@Net,N'Sales');
  IF @Tax>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@OutGST,0,@Tax,N'Output GST');
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) SELECT @J,CASE WHEN fm.BookType=N'CASH' THEN @Cash ELSE @Bank END,SUM(p.Amount),0,pm.MethodCode FROM sales.SalesPayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId JOIN finance.PaymentModes fm ON fm.ModeCode=pm.MethodCode WHERE p.InvoiceId=@SourceId AND p.Status=N'COMPLETED' AND fm.BookType<>N'CREDIT' GROUP BY fm.BookType,pm.MethodCode HAVING SUM(p.Amount)>0;
  IF @Paid>0 INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description) VALUES(@J,@Cust,0,@Paid,N'Customer receipts');
  IF @Customer IS NOT NULL BEGIN INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Customer,@J,@Date,N'SALE',@SourceId,@RefNo,@Grand,0,@Narr); IF @Paid>0 INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Customer,@J,@Date,N'RECEIPT',@SourceId,@RefNo,0,@Paid,N'Sales receipt'); END;
  INSERT finance.CashBook(JournalEntryId,EntryDate,EntryType,ReferenceId,AmountIn,Narration) SELECT @J,p.PaymentDate,N'CASH IN',@SourceId,p.Amount,N'Sales receipt' FROM sales.SalesPayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId WHERE p.InvoiceId=@SourceId AND pm.MethodCode=N'CASH' AND p.Amount>0;
  INSERT finance.BankBook(JournalEntryId,PaymentModeId,EntryDate,EntryType,ReferenceId,ReferenceNumber,AmountIn,Narration) SELECT @J,fm.PaymentModeId,p.PaymentDate,N'RECEIPT',@SourceId,p.ReferenceNumber,p.Amount,N'Sales receipt' FROM sales.SalesPayments p JOIN sales.PaymentMethods pm ON pm.PaymentMethodId=p.PaymentMethodId JOIN finance.PaymentModes fm ON fm.ModeCode=pm.MethodCode WHERE p.InvoiceId=@SourceId AND fm.BookType=N'BANK' AND p.Amount>0;
 END
 ELSE IF @SourceType=N'PURCHASE_RETURN'
 BEGIN
  SELECT @Date=MAX(r.ReturnDate),@Supplier=MAX(i.SupplierId),@RefNo=MAX(r.ReturnNumber),@Grand=SUM(r.AdjustmentAmount),@Narr=MAX(r.Reason) FROM purchase.PurchaseReturns r JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=r.PurchaseInvoiceId JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId AND s.TenantId=@TenantId WHERE r.PurchaseInvoiceId=@SourceId AND i.TenantId=@TenantId;
  IF ISNULL(@Grand,0)<=0 THROW 51508,N'Purchase return journal total must be positive.',1;
  INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,@SourceType,@SourceType,@SourceId,@Narr,@CreatedBy,@TenantId);
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Supp,@Grand,0),(@J,@PRet,0,@Grand);
  INSERT finance.SupplierLedger(SupplierId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Supplier,@J,@Date,N'PURCHASE_RETURN',@SourceId,@RefNo,0,@Grand,@Narr);
 END
 ELSE
 BEGIN
  SELECT @Date=MAX(r.ReturnDate),@Customer=MAX(i.CustomerId),@RefNo=MAX(r.ReturnNumber),@Grand=SUM(r.RefundAmount),@Narr=MAX(r.Reason) FROM sales.SalesInvoiceReturns r JOIN sales.SalesInvoices i ON i.InvoiceId=r.InvoiceId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE r.InvoiceId=@SourceId AND i.TenantId=@TenantId AND (i.CustomerId IS NULL OR c.TenantId=@TenantId);
  IF ISNULL(@Grand,0)<=0 THROW 51509,N'Sale return journal total must be positive.',1;
  INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,@SourceType,@SourceType,@SourceId,@Narr,@CreatedBy,@TenantId);
  INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@SRet,@Grand,0),(@J,@Cust,0,@Grand);
  IF @Customer IS NOT NULL INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration) VALUES(@Customer,@J,@Date,N'CREDIT_NOTE',@SourceId,@RefNo,0,@Grand,@Narr);
 END;
 DECLARE @Dr decimal(18,2)=(SELECT SUM(DebitAmount) FROM finance.JournalEntryDetails WHERE JournalEntryId=@J),@Cr decimal(18,2)=(SELECT SUM(CreditAmount) FROM finance.JournalEntryDetails WHERE JournalEntryId=@J);
 IF @Dr<=0 OR @Cr<=0 OR ABS(@Dr-@Cr)>.01 THROW 51510,N'Financial journal is not balanced.',1;
 INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,ReferenceId,DebitAmount,CreditAmount,Narration) SELECT @J,AccountId,@Date,@SourceType,@SourceId,DebitAmount,CreditAmount,Description FROM finance.JournalEntryDetails WHERE JournalEntryId=@J;
 INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,DebitTotal,CreditTotal,Narration) VALUES(@J,@Date,@SourceType,@SourceType,@SourceId,@RefNo,@Dr,@Cr,@Narr);
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.RefreshOutstanding @TenantId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @TenantId IS NULL OR (@SessionTenant IS NOT NULL AND @TenantId<>@SessionTenant) THROW 51500,N'Tenant context is missing or mismatched.',1;
 DECLARE @now date=CAST(SYSUTCDATETIME() AS date);
 MERGE finance.CustomerOutstanding WITH(HOLDLOCK) AS t
 USING(SELECT i.CustomerId,i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,DATEADD(day,ISNULL(pt.DueDays,30),i.InvoiceDate) DueDate,i.GrandTotal InvoiceAmount,i.PaidAmount ReceivedAmount,i.BalanceAmount OutstandingAmount,DATEDIFF(day,DATEADD(day,ISNULL(pt.DueDays,30),i.InvoiceDate),@now) AgeDays FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=@TenantId LEFT JOIN sales.CustomerPaymentTerms pt ON pt.PaymentTermId=c.PaymentTermId WHERE i.TenantId=@TenantId AND i.Status NOT IN(N'VOID',N'CANCELLED',N'DRAFT')) s
 ON t.InvoiceId=s.InvoiceId
 WHEN MATCHED THEN UPDATE SET CustomerId=s.CustomerId,InvoiceNumber=s.InvoiceNumber,InvoiceDate=s.InvoiceDate,DueDate=s.DueDate,InvoiceAmount=s.InvoiceAmount,ReceivedAmount=s.ReceivedAmount,OutstandingAmount=s.OutstandingAmount,AgeDays=s.AgeDays,AgeBucket=CASE WHEN s.AgeDays<=30 THEN N'0-30' WHEN s.AgeDays<=60 THEN N'31-60' WHEN s.AgeDays<=90 THEN N'61-90' ELSE N'ABOVE_90' END,LastUpdated=SYSUTCDATETIME()
 WHEN NOT MATCHED THEN INSERT(CustomerId,InvoiceId,InvoiceNumber,InvoiceDate,DueDate,InvoiceAmount,ReceivedAmount,OutstandingAmount,AgeDays,AgeBucket) VALUES(s.CustomerId,s.InvoiceId,s.InvoiceNumber,s.InvoiceDate,s.DueDate,s.InvoiceAmount,s.ReceivedAmount,s.OutstandingAmount,s.AgeDays,CASE WHEN s.AgeDays<=30 THEN N'0-30' WHEN s.AgeDays<=60 THEN N'31-60' WHEN s.AgeDays<=90 THEN N'61-90' ELSE N'ABOVE_90' END)
 WHEN NOT MATCHED BY SOURCE AND EXISTS(SELECT 1 FROM sales.SalesInvoices x WHERE x.InvoiceId=t.InvoiceId AND x.TenantId=@TenantId) THEN DELETE;
 MERGE finance.SupplierOutstanding WITH(HOLDLOCK) AS t
 USING(SELECT i.SupplierId,i.PurchaseInvoiceId,i.InvoiceNumber,i.InvoiceDate,COALESCE(i.DueDate,i.InvoiceDate) DueDate,i.GrandTotal InvoiceAmount,i.PaidAmount,purchase.PurchaseOutstanding(i.PurchaseInvoiceId) OutstandingAmount,DATEDIFF(day,COALESCE(i.DueDate,i.InvoiceDate),@now) AgeDays FROM purchase.PurchaseInvoices i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId AND s.TenantId=@TenantId WHERE i.TenantId=@TenantId AND i.IsDeleted=0 AND i.Status NOT IN(N'CANCELLED',N'DRAFT')) s
 ON t.PurchaseInvoiceId=s.PurchaseInvoiceId
 WHEN MATCHED THEN UPDATE SET SupplierId=s.SupplierId,InvoiceNumber=s.InvoiceNumber,InvoiceDate=s.InvoiceDate,DueDate=s.DueDate,InvoiceAmount=s.InvoiceAmount,PaidAmount=s.PaidAmount,OutstandingAmount=s.OutstandingAmount,AgeDays=s.AgeDays,AgeBucket=CASE WHEN s.AgeDays<=30 THEN N'0-30' WHEN s.AgeDays<=60 THEN N'31-60' WHEN s.AgeDays<=90 THEN N'61-90' ELSE N'ABOVE_90' END,LastUpdated=SYSUTCDATETIME()
 WHEN NOT MATCHED THEN INSERT(SupplierId,PurchaseInvoiceId,InvoiceNumber,InvoiceDate,DueDate,InvoiceAmount,PaidAmount,OutstandingAmount,AgeDays,AgeBucket) VALUES(s.SupplierId,s.PurchaseInvoiceId,s.InvoiceNumber,s.InvoiceDate,s.DueDate,s.InvoiceAmount,s.PaidAmount,s.OutstandingAmount,s.AgeDays,CASE WHEN s.AgeDays<=30 THEN N'0-30' WHEN s.AgeDays<=60 THEN N'31-60' WHEN s.AgeDays<=90 THEN N'61-90' ELSE N'ABOVE_90' END)
 WHEN NOT MATCHED BY SOURCE AND EXISTS(SELECT 1 FROM purchase.PurchaseInvoices x WHERE x.PurchaseInvoiceId=t.PurchaseInvoiceId AND x.TenantId=@TenantId) THEN DELETE;
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
CREATE OR ALTER PROCEDURE finance.PostPayment
 @TenantId uniqueidentifier=NULL,@Domain nvarchar(10),@PaymentId uniqueidentifier,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @Domain NOT IN(N'SALE',N'PURCHASE') THROW 51511,N'Unsupported payment domain.',1;
 /* Resolve legacy internal calls through the payment's tenant-owned invoice. */
 IF @TenantId IS NULL
  SELECT @TenantId=CASE WHEN @Domain=N'PURCHASE' THEN
   (SELECT i.TenantId FROM purchase.PurchasePayments p JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=p.PurchaseInvoiceId WHERE p.PurchasePaymentId=@PaymentId)
   ELSE (SELECT i.TenantId FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId WHERE p.PaymentId=@PaymentId) END;
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 DECLARE @Type nvarchar(30)=CONCAT(@Domain,N'_PAYMENT');
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=@Type AND ReferenceId=@PaymentId AND TenantId<>@TenantId) THROW 51502,N'Finance source is owned by another tenant.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=@Type AND ReferenceId=@PaymentId AND TenantId=@TenantId) RETURN;
 DECLARE @J uniqueidentifier=NEWID(),@No nvarchar(50)=CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),@Date datetimeoffset,@PartyId uniqueidentifier,@Amount decimal(18,2),@Mode nvarchar(30),@Reference nvarchar(100),@ModeId uniqueidentifier,@Book nvarchar(10),@PartyAccount uniqueidentifier,@Cash uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'CASH'),@Bank uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'BANK');
 IF @Domain=N'PURCHASE' SELECT @Date=p.PaymentDate,@PartyId=i.SupplierId,@Amount=p.Amount,@Mode=m.MethodCode,@Reference=p.ReferenceNumber FROM purchase.PurchasePayments p JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=p.PurchaseInvoiceId AND i.TenantId=@TenantId JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId AND s.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=p.PaymentMethodId WHERE p.PurchasePaymentId=@PaymentId;
 ELSE SELECT @Date=p.PaymentDate,@PartyId=i.CustomerId,@Amount=p.Amount,@Mode=m.MethodCode,@Reference=p.ReferenceNumber FROM sales.SalesPayments p JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=@TenantId JOIN sales.PaymentMethods m ON m.PaymentMethodId=p.PaymentMethodId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE p.PaymentId=@PaymentId AND (i.CustomerId IS NULL OR c.TenantId=@TenantId);
 SELECT @ModeId=PaymentModeId,@Book=BookType FROM finance.PaymentModes WHERE ModeCode=@Mode AND IsActive=1;
 IF @Amount IS NULL THROW 51512,N'Payment source was not found for this tenant.',1; IF @Amount<=0 THROW 51513,N'Payment journal total must be positive.',1; IF @ModeId IS NULL OR @Book=N'CREDIT' THROW 51514,N'Cash or bank payment mode is required.',1;
 SET @PartyAccount=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=CASE WHEN @Domain=N'PURCHASE' THEN N'SUPPLIER' ELSE N'CUSTOMER' END);
 IF @PartyAccount IS NULL OR @Cash IS NULL OR @Bank IS NULL THROW 51503,N'Finance account configuration is incomplete.',1;
 INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,@Type,@Type,@PaymentId,CONCAT(@Domain,N' payment'),@CreatedBy,@TenantId);
 IF @Domain=N'PURCHASE' INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@PartyAccount,@Amount,0),(@J,CASE WHEN @Book=N'CASH' THEN @Cash ELSE @Bank END,0,@Amount);
 ELSE INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,CASE WHEN @Book=N'CASH' THEN @Cash ELSE @Bank END,@Amount,0),(@J,@PartyAccount,0,@Amount);
 INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,ReferenceId,DebitAmount,CreditAmount,Narration) SELECT @J,AccountId,@Date,@Type,@PaymentId,DebitAmount,CreditAmount,CONCAT(@Domain,N' payment') FROM finance.JournalEntryDetails WHERE JournalEntryId=@J;
 INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,DebitTotal,CreditTotal,Narration) VALUES(@J,@Date,@Type,@Type,@PaymentId,@Reference,@Amount,@Amount,CONCAT(@Domain,N' payment'));
 IF @Domain=N'PURCHASE' INSERT finance.SupplierLedger(SupplierId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount) VALUES(@PartyId,@J,@Date,N'PAYMENT',@PaymentId,@Reference,0,@Amount);
 ELSE IF @PartyId IS NOT NULL INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,ReferenceId,ReferenceNumber,DebitAmount,CreditAmount) VALUES(@PartyId,@J,@Date,N'RECEIPT',@PaymentId,@Reference,0,@Amount);
 IF @Book=N'CASH' INSERT finance.CashBook(JournalEntryId,EntryDate,EntryType,ReferenceId,AmountIn,AmountOut,Narration) VALUES(@J,@Date,CASE WHEN @Domain=N'SALE' THEN N'CASH IN' ELSE N'CASH OUT' END,@PaymentId,CASE WHEN @Domain=N'SALE' THEN @Amount ELSE 0 END,CASE WHEN @Domain=N'PURCHASE' THEN @Amount ELSE 0 END,CONCAT(@Domain,N' payment'));
 ELSE INSERT finance.BankBook(JournalEntryId,PaymentModeId,EntryDate,EntryType,ReferenceId,ReferenceNumber,AmountIn,AmountOut,Narration) VALUES(@J,@ModeId,@Date,CASE WHEN @Domain=N'SALE' THEN N'RECEIPT' ELSE N'PAYMENT' END,@PaymentId,@Reference,CASE WHEN @Domain=N'SALE' THEN @Amount ELSE 0 END,CASE WHEN @Domain=N'PURCHASE' THEN @Amount ELSE 0 END,CONCAT(@Domain,N' payment'));
END;
GO

SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE finance.PostStockAdjustment
 @TenantId uniqueidentifier=NULL,@TransactionId uniqueidentifier,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 /* Resolve legacy internal calls through the tenant-owned inventory transaction. */
 IF @TenantId IS NULL SELECT @TenantId=TenantId FROM inventory.InventoryTransactions WHERE TransactionId=@TransactionId;
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=N'STOCK_ADJUSTMENT' AND ReferenceId=@TransactionId AND TenantId<>@TenantId) THROW 51502,N'Finance source is owned by another tenant.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=N'STOCK_ADJUSTMENT' AND ReferenceId=@TransactionId AND TenantId=@TenantId) RETURN;
 DECLARE @J uniqueidentifier=NEWID(),@No nvarchar(50)=CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),@Date datetimeoffset,@Type nvarchar(10),@Ref nvarchar(50),@Amount decimal(18,2),@Narr nvarchar(500),@Inv uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'INVENTORY'),@Adj uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'STOCK_ADJUST');
 SELECT @Date=t.TransactionDate,@Type=a.AdjustmentType,@Ref=a.AdjustmentNo,@Narr=t.Remarks,@Amount=SUM(d.Quantity*d.UnitCost) FROM inventory.StockAdjustments a JOIN inventory.InventoryTransactions t ON t.TransactionId=a.TransactionId AND t.TenantId=@TenantId JOIN inventory.Warehouses w ON w.WarehouseId=t.WarehouseId AND w.TenantId=@TenantId JOIN inventory.InventoryTransactionDetails d ON d.TransactionId=t.TransactionId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId WHERE a.TransactionId=@TransactionId GROUP BY t.TransactionDate,a.AdjustmentType,a.AdjustmentNo,t.Remarks;
 IF @Amount IS NULL THROW 51515,N'Stock adjustment source was not found for this tenant.',1; IF @Amount<=0 THROW 51516,N'Stock adjustment journal total must be positive.',1; IF @Inv IS NULL OR @Adj IS NULL THROW 51503,N'Finance account configuration is incomplete.',1;
 INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,N'STOCK_ADJUSTMENT',N'STOCK_ADJUSTMENT',@TransactionId,@Narr,@CreatedBy,@TenantId);
 IF @Type=N'INCREASE' INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Inv,@Amount,0),(@J,@Adj,0,@Amount); ELSE INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Adj,@Amount,0),(@J,@Inv,0,@Amount);
 INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,ReferenceId,DebitAmount,CreditAmount,Narration) SELECT @J,AccountId,@Date,N'STOCK_ADJUSTMENT',@TransactionId,DebitAmount,CreditAmount,@Narr FROM finance.JournalEntryDetails WHERE JournalEntryId=@J;
 INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,DebitTotal,CreditTotal,Narration) VALUES(@J,@Date,N'STOCK_ADJUSTMENT',N'STOCK_ADJUSTMENT',@TransactionId,@Ref,@Amount,@Amount,@Narr);
END;
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
