CREATE PROCEDURE finance.PostSource
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

