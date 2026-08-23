CREATE PROCEDURE [finance].[PostSource]
    @SourceType nvarchar(30),
    @SourceId uniqueidentifier,
    @CreatedBy nvarchar(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS
    (
        SELECT 1
        FROM finance.JournalEntries
        WHERE ReferenceType = @SourceType
          AND ReferenceId = @SourceId
          AND TransactionType = @SourceType
    )
        RETURN;

    DECLARE @J uniqueidentifier = NEWID(),
            @No nvarchar(50) = CONCAT('JV-', UPPER(LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), '-', ''), 20))),
            @Date datetimeoffset,
            @RefNo nvarchar(50),
            @Narr nvarchar(500),
            @Customer uniqueidentifier,
            @Supplier uniqueidentifier,
            @Grand decimal(18,2),
            @Tax decimal(18,2),
            @Net decimal(18,2),
            @Paid decimal(18,2) = 0;

    DECLARE @Cash uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'CASH'),
            @Bank uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'BANK'),
            @Cust uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'CUSTOMER'),
            @Supp uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'SUPPLIER'),
            @Inv uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'INVENTORY'),
            @InGST uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'INPUT_GST'),
            @OutGST uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'OUTPUT_GST'),
            @Sales uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'SALES'),
            @PRet uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'PURCHASE_RETURN'),
            @SRet uniqueidentifier = (SELECT AccountId FROM finance.Accounts WHERE AccountCode = 'SALES_RETURN');

    IF @SourceType = 'PURCHASE'
    BEGIN
        SELECT @Date = InvoiceDate, @RefNo = InvoiceNumber, @Supplier = SupplierId,
               @Grand = GrandTotal, @Tax = TaxAmount, @Net = GrandTotal - TaxAmount, @Narr = Remarks
        FROM purchase.PurchaseInvoices
        WHERE PurchaseInvoiceId = @SourceId AND Status <> 'DRAFT';
        IF @Grand IS NULL RETURN;

        SELECT @Paid = ISNULL(SUM(p.Amount), 0)
        FROM purchase.PurchasePayments p
        WHERE p.PurchaseInvoiceId = @SourceId AND p.Status = 'COMPLETED';

        INSERT finance.JournalEntries(JournalEntryId, JournalNumber, EntryDate, TransactionType, ReferenceType, ReferenceId, Narration, CreatedBy)
        VALUES(@J, @No, @Date, @SourceType, @SourceType, @SourceId, @Narr, @CreatedBy);
        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
        VALUES(@J, @Inv, @Net, 0, 'Inventory and landed cost'),
              (@J, @InGST, @Tax, 0, 'Input GST'),
              (@J, @Supp, 0, @Grand, 'Supplier payable');
        IF @Paid > 0
            INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
            VALUES(@J, @Supp, @Paid, 0, 'Supplier payments');

        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
        SELECT @J, CASE WHEN fm.BookType = 'CASH' THEN @Cash ELSE @Bank END, 0, SUM(p.Amount), pm.MethodCode
        FROM purchase.PurchasePayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        JOIN finance.PaymentModes fm ON fm.ModeCode = pm.MethodCode
        WHERE p.PurchaseInvoiceId = @SourceId AND p.Status = 'COMPLETED' AND fm.BookType <> 'CREDIT'
        GROUP BY fm.BookType, pm.MethodCode;

        INSERT finance.SupplierLedger(SupplierId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
        VALUES(@Supplier, @J, @Date, 'PURCHASE', @SourceId, @RefNo, @Grand, 0, @Narr);
        IF @Paid > 0
            INSERT finance.SupplierLedger(SupplierId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
            VALUES(@Supplier, @J, @Date, 'PAYMENT', @SourceId, @RefNo, 0, @Paid, 'Purchase payment');

        INSERT finance.CashBook(JournalEntryId, EntryDate, EntryType, ReferenceId, AmountOut, Narration)
        SELECT @J, p.PaymentDate, 'CASH OUT', @SourceId, p.Amount, 'Purchase payment'
        FROM purchase.PurchasePayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        WHERE p.PurchaseInvoiceId = @SourceId AND pm.MethodCode = 'CASH';

        INSERT finance.BankBook(JournalEntryId, PaymentModeId, EntryDate, EntryType, ReferenceId, ReferenceNumber, AmountOut, Narration)
        SELECT @J, fm.PaymentModeId, p.PaymentDate, 'PAYMENT', @SourceId, p.ReferenceNumber, p.Amount, 'Purchase payment'
        FROM purchase.PurchasePayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        JOIN finance.PaymentModes fm ON fm.ModeCode = pm.MethodCode
        WHERE p.PurchaseInvoiceId = @SourceId AND fm.BookType = 'BANK';
    END
    ELSE IF @SourceType = 'SALE'
    BEGIN
        SELECT @Date = InvoiceDate, @RefNo = InvoiceNumber, @Customer = CustomerId,
               @Grand = GrandTotal, @Tax = TaxAmount, @Net = GrandTotal - TaxAmount, @Narr = Remarks
        FROM sales.SalesInvoices
        WHERE InvoiceId = @SourceId AND Status NOT IN ('HELD', 'SUSPENDED', 'CANCELLED', 'VOID');
        IF @Grand IS NULL RETURN;

        SELECT @Paid = ISNULL(SUM(p.Amount), 0)
        FROM sales.SalesPayments p
        WHERE p.InvoiceId = @SourceId AND p.Status = 'COMPLETED';

        INSERT finance.JournalEntries(JournalEntryId, JournalNumber, EntryDate, TransactionType, ReferenceType, ReferenceId, Narration, CreatedBy)
        VALUES(@J, @No, @Date, @SourceType, @SourceType, @SourceId, @Narr, @CreatedBy);
        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
        VALUES(@J, @Cust, @Grand, 0, 'Customer receivable'),
              (@J, @Sales, 0, @Net, 'Sales'),
              (@J, @OutGST, 0, @Tax, 'Output GST');

        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
        SELECT @J, CASE WHEN fm.BookType = 'CASH' THEN @Cash ELSE @Bank END, SUM(p.Amount), 0, pm.MethodCode
        FROM sales.SalesPayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        JOIN finance.PaymentModes fm ON fm.ModeCode = pm.MethodCode
        WHERE p.InvoiceId = @SourceId AND p.Status = 'COMPLETED' AND fm.BookType <> 'CREDIT'
        GROUP BY fm.BookType, pm.MethodCode;
        IF @Paid > 0
            INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount, Description)
            VALUES(@J, @Cust, 0, @Paid, 'Customer receipts');

        IF @Customer IS NOT NULL
        BEGIN
            INSERT finance.CustomerLedger(CustomerId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
            VALUES(@Customer, @J, @Date, 'SALE', @SourceId, @RefNo, @Grand, 0, @Narr);
            IF @Paid > 0
                INSERT finance.CustomerLedger(CustomerId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
                VALUES(@Customer, @J, @Date, 'RECEIPT', @SourceId, @RefNo, 0, @Paid, 'Sales receipt');
        END;

        INSERT finance.CashBook(JournalEntryId, EntryDate, EntryType, ReferenceId, AmountIn, Narration)
        SELECT @J, p.PaymentDate, 'CASH IN', @SourceId, p.Amount, 'Sales receipt'
        FROM sales.SalesPayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        WHERE p.InvoiceId = @SourceId AND pm.MethodCode = 'CASH';

        INSERT finance.BankBook(JournalEntryId, PaymentModeId, EntryDate, EntryType, ReferenceId, ReferenceNumber, AmountIn, Narration)
        SELECT @J, fm.PaymentModeId, p.PaymentDate, 'RECEIPT', @SourceId, p.ReferenceNumber, p.Amount, 'Sales receipt'
        FROM sales.SalesPayments p
        JOIN sales.PaymentMethods pm ON pm.PaymentMethodId = p.PaymentMethodId
        JOIN finance.PaymentModes fm ON fm.ModeCode = pm.MethodCode
        WHERE p.InvoiceId = @SourceId AND fm.BookType = 'BANK';
    END
    ELSE IF @SourceType = 'PURCHASE_RETURN'
    BEGIN
        SELECT @Date = MAX(r.ReturnDate), @Supplier = MAX(i.SupplierId), @RefNo = MAX(r.ReturnNumber),
               @Grand = SUM(r.AdjustmentAmount), @Narr = MAX(r.Reason)
        FROM purchase.PurchaseReturns r
        JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId = r.PurchaseInvoiceId
        WHERE r.PurchaseInvoiceId = @SourceId;
        IF @Grand IS NULL RETURN;

        INSERT finance.JournalEntries(JournalEntryId, JournalNumber, EntryDate, TransactionType, ReferenceType, ReferenceId, Narration, CreatedBy)
        VALUES(@J, @No, @Date, @SourceType, @SourceType, @SourceId, @Narr, @CreatedBy);
        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount)
        VALUES(@J, @Supp, @Grand, 0), (@J, @PRet, 0, @Grand);
        INSERT finance.SupplierLedger(SupplierId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
        VALUES(@Supplier, @J, @Date, 'PURCHASE_RETURN', @SourceId, @RefNo, 0, @Grand, @Narr);
    END
    ELSE IF @SourceType = 'SALE_RETURN'
    BEGIN
        SELECT @Date = MAX(r.ReturnDate), @Customer = MAX(i.CustomerId), @RefNo = MAX(r.ReturnNumber),
               @Grand = SUM(r.RefundAmount), @Narr = MAX(r.Reason)
        FROM sales.SalesInvoiceReturns r
        JOIN sales.SalesInvoices i ON i.InvoiceId = r.InvoiceId
        WHERE r.InvoiceId = @SourceId;
        IF @Grand IS NULL RETURN;

        INSERT finance.JournalEntries(JournalEntryId, JournalNumber, EntryDate, TransactionType, ReferenceType, ReferenceId, Narration, CreatedBy)
        VALUES(@J, @No, @Date, @SourceType, @SourceType, @SourceId, @Narr, @CreatedBy);
        INSERT finance.JournalEntryDetails(JournalEntryId, AccountId, DebitAmount, CreditAmount)
        VALUES(@J, @SRet, @Grand, 0), (@J, @Cust, 0, @Grand);
        IF @Customer IS NOT NULL
            INSERT finance.CustomerLedger(CustomerId, JournalEntryId, EntryDate, EntryType, ReferenceId, ReferenceNumber, DebitAmount, CreditAmount, Narration)
            VALUES(@Customer, @J, @Date, 'CREDIT_NOTE', @SourceId, @RefNo, 0, @Grand, @Narr);
    END;

    IF EXISTS (SELECT 1 FROM finance.JournalEntries WHERE JournalEntryId = @J)
    BEGIN
        DECLARE @Dr decimal(18,2) = (SELECT SUM(DebitAmount) FROM finance.JournalEntryDetails WHERE JournalEntryId = @J),
                @Cr decimal(18,2) = (SELECT SUM(CreditAmount) FROM finance.JournalEntryDetails WHERE JournalEntryId = @J);
        IF ABS(@Dr - @Cr) > .01 THROW 51300, 'Financial journal is not balanced.', 1;

        INSERT finance.LedgerEntries(JournalEntryId, AccountId, EntryDate, ReferenceType, ReferenceId, DebitAmount, CreditAmount, Narration)
        SELECT @J, AccountId, @Date, @SourceType, @SourceId, DebitAmount, CreditAmount, Description
        FROM finance.JournalEntryDetails
        WHERE JournalEntryId = @J;
        INSERT finance.DayBook(JournalEntryId, EntryDate, TransactionType, ReferenceType, ReferenceId, ReferenceNumber, DebitTotal, CreditTotal, Narration)
        VALUES(@J, @Date, @SourceType, @SourceType, @SourceId, @RefNo, @Dr, @Cr, @Narr);
    END;
END;
