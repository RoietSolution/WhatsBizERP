/* V53 - product policy terms and immutable sales-item return-policy snapshots. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'master.Products', N'ReturnPolicyDescription') IS NULL
    ALTER TABLE master.Products ADD ReturnPolicyDescription nvarchar(1000) NULL;

IF COL_LENGTH(N'sales.SalesInvoiceItems', N'ReturnPolicyModeSnapshot') IS NULL
    ALTER TABLE sales.SalesInvoiceItems ADD ReturnPolicyModeSnapshot varchar(20) NULL;
IF COL_LENGTH(N'sales.SalesInvoiceItems', N'ReturnWindowDaysSnapshot') IS NULL
    ALTER TABLE sales.SalesInvoiceItems ADD ReturnWindowDaysSnapshot int NULL;
IF COL_LENGTH(N'sales.SalesInvoiceItems', N'ReturnPolicyDescriptionSnapshot') IS NULL
    ALTER TABLE sales.SalesInvoiceItems ADD ReturnPolicyDescriptionSnapshot nvarchar(1000) NULL;

/*
   The columns above are conditionally added in this batch.  Compile the
   dependent constraint after those ALTER TABLE statements have completed;
   SQL Server otherwise binds the column reference before the new column is
   visible to the batch and raises "Invalid column name".
*/
EXEC(N'IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N''CK_SalesInvoiceItems_ReturnWindowSnapshot'' AND parent_object_id=OBJECT_ID(N''sales.SalesInvoiceItems''))
    ALTER TABLE sales.SalesInvoiceItems ADD CONSTRAINT CK_SalesInvoiceItems_ReturnWindowSnapshot CHECK (ReturnWindowDaysSnapshot IS NULL OR ReturnWindowDaysSnapshot BETWEEN 0 AND 3650);');

EXEC(N'CREATE OR ALTER TRIGGER sales.TR_SalesInvoiceItems_ReturnPolicySnapshot ON sales.SalesInvoiceItems AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 UPDATE i SET ReturnPolicyModeSnapshot=COALESCE(i.ReturnPolicyModeSnapshot,p.ReturnPolicyMode),
   ReturnWindowDaysSnapshot=CASE WHEN COALESCE(i.ReturnPolicyModeSnapshot,p.ReturnPolicyMode)=N''NON_RETURNABLE'' THEN NULL ELSE COALESCE(i.ReturnWindowDaysSnapshot,CASE WHEN p.ReturnPolicyMode=N''CUSTOM'' THEN p.ReturnWindowDays ELSE sc.DefaultReturnWindowDays END) END,
   ReturnPolicyDescriptionSnapshot=COALESCE(i.ReturnPolicyDescriptionSnapshot,p.ReturnPolicyDescription)
 FROM sales.SalesInvoiceItems i JOIN inserted n ON n.InvoiceItemId=i.InvoiceItemId
 JOIN master.Products p ON p.ProductId=i.ProductId
 JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId
 LEFT JOIN commerce.StorefrontConfigurations sc ON sc.TenantId=h.TenantId
 WHERE i.ReturnPolicyModeSnapshot IS NULL OR i.ReturnWindowDaysSnapshot IS NULL AND COALESCE(i.ReturnPolicyModeSnapshot,p.ReturnPolicyMode)<>N''NON_RETURNABLE'';
END');
