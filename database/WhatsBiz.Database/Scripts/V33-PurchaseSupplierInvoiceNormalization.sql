/* Normalize optional supplier invoice values for the filtered unique index.
   The purchase tenant guard requires the current tenant context for writes. */
DECLARE @TenantId uniqueidentifier;
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT TenantId
    FROM purchase.PurchaseInvoices
    WHERE TenantId IS NOT NULL
      AND SupplierInvoiceNo IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(SupplierInvoiceNo)), N'') IS NULL;
OPEN tenants;
FETCH NEXT FROM tenants INTO @TenantId;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC sys.sp_set_session_context @key = N'TenantId', @value = @TenantId;
    UPDATE purchase.PurchaseInvoices
    SET SupplierInvoiceNo = NULL
    WHERE TenantId = @TenantId
      AND SupplierInvoiceNo IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(SupplierInvoiceNo)), N'') IS NULL;
    FETCH NEXT FROM tenants INTO @TenantId;
END;
CLOSE tenants;
DEALLOCATE tenants;
EXEC sys.sp_set_session_context @key = N'TenantId', @value = NULL;
GO
