SET NOCOUNT ON;

IF OBJECT_ID(N'sales.POS_ReturnInvoice', N'P') IS NULL
    THROW 66100, N'V66 validation failed: sales.POS_ReturnInvoice is missing.', 1;
IF OBJECT_ID(N'finance.PostSource', N'P') IS NULL
    THROW 66101, N'V66 validation failed: finance.PostSource is missing.', 1;

DECLARE @definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_ReturnInvoice',N'P'));
IF @definition IS NULL OR CHARINDEX(N'@CumulativeRefund',@definition)=0 OR CHARINDEX(N'@ReturnJournalId',@definition)=0
    THROW 66102, N'V66 validation failed: cumulative return-journal synchronization is missing.', 1;

SELECT N'V66_CUMULATIVE_SALES_RETURN_JOURNAL_VALID' AS ValidationResult;
