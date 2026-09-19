/* Shared finance baseline: global chart-of-accounts and payment references. */
SET NOCOUNT ON;

DECLARE @Groups TABLE(GroupCode nvarchar(30) NOT NULL PRIMARY KEY, GroupName nvarchar(100) NOT NULL, Nature nvarchar(20) NOT NULL);
INSERT @Groups VALUES
    (N'ASSET', N'Assets', N'ASSET'), (N'LIABILITY', N'Liabilities', N'LIABILITY'),
    (N'INCOME', N'Income', N'INCOME'), (N'EXPENSE', N'Expenses', N'EXPENSE');
MERGE finance.AccountGroups AS target
USING @Groups AS source ON source.GroupCode = target.GroupCode
WHEN MATCHED THEN UPDATE SET GroupName = source.GroupName, Nature = source.Nature, IsActive = 1
WHEN NOT MATCHED THEN INSERT (AccountGroupId, GroupCode, GroupName, ParentGroupId, Nature, IsActive)
VALUES (NEWID(), source.GroupCode, source.GroupName, NULL, source.Nature, 1);

DECLARE @Accounts TABLE(AccountCode nvarchar(30) NOT NULL PRIMARY KEY, AccountName nvarchar(150) NOT NULL, GroupCode nvarchar(30) NOT NULL);
INSERT @Accounts VALUES
    (N'CASH', N'Cash', N'ASSET'), (N'BANK', N'Bank', N'ASSET'),
    (N'CUSTOMER', N'Customer Receivables', N'ASSET'), (N'SUPPLIER', N'Supplier Payables', N'LIABILITY'),
    (N'INVENTORY', N'Inventory', N'ASSET'), (N'INPUT_GST', N'Input GST', N'ASSET'),
    (N'OUTPUT_GST', N'Output GST', N'LIABILITY'), (N'SALES', N'Sales', N'INCOME'),
    (N'PURCHASE_RETURN', N'Purchase Returns', N'INCOME'), (N'SALES_RETURN', N'Sales Returns', N'EXPENSE'),
    (N'STOCK_ADJUST', N'Stock Adjustments', N'EXPENSE');
MERGE finance.Accounts AS target
USING (SELECT a.AccountCode, a.AccountName, g.AccountGroupId FROM @Accounts a JOIN finance.AccountGroups g ON g.GroupCode = a.GroupCode) AS source
ON source.AccountCode = target.AccountCode
WHEN MATCHED THEN UPDATE SET AccountName = source.AccountName, AccountGroupId = source.AccountGroupId, IsSystem = 1, IsActive = 1
WHEN NOT MATCHED THEN INSERT (AccountId, AccountCode, AccountName, AccountGroupId, OpeningBalance, IsSystem, IsActive, CreatedOn)
VALUES (NEWID(), source.AccountCode, source.AccountName, source.AccountGroupId, 0, 1, 1, SYSUTCDATETIME());

MERGE finance.PaymentModes AS target
USING (VALUES (N'CASH', N'Cash', N'CASH'), (N'UPI', N'UPI', N'BANK'), (N'CARD', N'Card', N'BANK'),
              (N'BANK', N'Bank Transfer', N'BANK'), (N'WALLET', N'Wallet', N'BANK'), (N'CREDIT', N'Credit', N'CREDIT'))
       AS source(ModeCode, ModeName, BookType)
ON source.ModeCode = target.ModeCode
WHEN MATCHED THEN UPDATE SET ModeName = source.ModeName, BookType = source.BookType, IsActive = 1
WHEN NOT MATCHED THEN INSERT (PaymentModeId, ModeCode, ModeName, BookType, IsActive)
VALUES (NEWID(), source.ModeCode, source.ModeName, source.BookType, 1);
