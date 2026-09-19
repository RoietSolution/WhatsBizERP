CREATE PROCEDURE finance.PostPayment
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

