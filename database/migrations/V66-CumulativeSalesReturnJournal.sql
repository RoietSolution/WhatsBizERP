/* V66: keep the single SALE_RETURN journal synchronized with cumulative returns. */
SET XACT_ABORT ON;

IF OBJECT_ID(N'sales.POS_ReturnInvoice', N'P') IS NULL
    THROW 66000, N'V66 requires sales.POS_ReturnInvoice.', 1;
IF OBJECT_ID(N'finance.PostSource', N'P') IS NULL
    THROW 66001, N'V66 requires finance.PostSource.', 1;
IF COL_LENGTH(N'finance.JournalEntries', N'TenantId') IS NULL
    THROW 66002, N'V66 requires tenant-owned finance journal entries.', 1;

DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_ReturnInvoice', N'P'));
IF @definition IS NULL
    THROW 66003, N'V66 could not read sales.POS_ReturnInvoice.', 1;

SET @definition = LTRIM(@definition);
DECLARE @procedurePosition int = CHARINDEX(N'PROCEDURE', UPPER(@definition));
IF @procedurePosition = 0
    THROW 66007, N'V66 expected the existing return procedure definition.', 1;
SET @definition = STUFF(@definition, 1, @procedurePosition + LEN(N'PROCEDURE') - 1, N'CREATE OR ALTER PROCEDURE');

DECLARE @old nvarchar(max) = N'EXEC finance.PostSource @SourceType=''SALE_RETURN'',@SourceId=@InvoiceId,@CreatedBy=@CreatedBy;COMMIT;';
DECLARE @new nvarchar(max) = N'EXEC finance.PostSource @TenantId=@TenantId,@SourceType=''SALE_RETURN'',@SourceId=@InvoiceId,@CreatedBy=@CreatedBy;DECLARE @ReturnJournalId UNIQUEIDENTIFIER,@CumulativeRefund DECIMAL(18,2),@ReturnAccount UNIQUEIDENTIFIER=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N''SALES_RETURN''),@CustomerAccount UNIQUEIDENTIFIER=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N''CUSTOMER'');SELECT @CumulativeRefund=COALESCE(SUM(RefundAmount),0) FROM sales.SalesInvoiceReturns WHERE InvoiceId=@InvoiceId;SELECT @ReturnJournalId=JournalEntryId FROM finance.JournalEntries WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ReferenceType=N''SALE_RETURN'' AND ReferenceId=@InvoiceId AND TransactionType=N''SALE_RETURN'';IF @ReturnJournalId IS NULL OR @ReturnAccount IS NULL OR @CustomerAccount IS NULL THROW 66004,N''Cumulative sale-return journal is missing or financial accounts are incomplete.'',1;UPDATE d SET DebitAmount=CASE WHEN d.AccountId=@ReturnAccount THEN @CumulativeRefund ELSE 0 END,CreditAmount=CASE WHEN d.AccountId=@CustomerAccount THEN @CumulativeRefund ELSE 0 END FROM finance.JournalEntryDetails d WHERE d.JournalEntryId=@ReturnJournalId;UPDATE l SET DebitAmount=d.DebitAmount,CreditAmount=d.CreditAmount FROM finance.LedgerEntries l JOIN finance.JournalEntryDetails d ON d.JournalEntryId=l.JournalEntryId AND d.AccountId=l.AccountId WHERE l.JournalEntryId=@ReturnJournalId;UPDATE b SET DebitTotal=@CumulativeRefund,CreditTotal=@CumulativeRefund FROM finance.DayBook b WHERE b.JournalEntryId=@ReturnJournalId;IF (SELECT COALESCE(SUM(DebitAmount),0) FROM finance.JournalEntryDetails WHERE JournalEntryId=@ReturnJournalId)<>@CumulativeRefund OR (SELECT COALESCE(SUM(CreditAmount),0) FROM finance.JournalEntryDetails WHERE JournalEntryId=@ReturnJournalId)<>@CumulativeRefund THROW 66005,N''Cumulative sale-return journal is unbalanced after synchronization.'',1;COMMIT;';
IF CHARINDEX(@old, @definition) = 0
    THROW 66006, N'V66 could not locate the existing sale-return finance posting boundary.', 1;

SET @definition = REPLACE(@definition, @old, @new);
EXEC sys.sp_executesql @definition;
