/*
 Read-only PROD schema precheck for current WhatsBiz database requirements.
 Every query is read-only; guarded dynamic SQL, where needed, contains SELECT only.
*/
GO
SELECT N'PROD_IDENTITY' AS CheckSection,
       CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) AS ServerName,
       DB_NAME() AS DatabaseName,
       CONVERT(nvarchar(128),SERVERPROPERTY(N'Edition')) AS SqlEdition,
       CONVERT(nvarchar(128),SERVERPROPERTY(N'ProductVersion')) AS SqlVersion;

GO
WITH Expected(MigrationVersion,ObjectKind,ParentName,ObjectName) AS
(
 SELECT * FROM (VALUES
  (N'V27',N'COLUMN',N'core.Users',N'AccountType'),
  (N'V27',N'CHECK',N'core.Users',N'CK_Users_AccountScope'),
  (N'V28',N'PROCEDURE',N'sales',N'POS_PostInvoice'),
  (N'V29',N'COLUMN',N'admin.Companies',N'TenantId'),
  (N'V29',N'FOREIGN_KEY',N'admin.Companies',N'FK_Companies_Tenants'),
  (N'V29',N'COLUMN',N'gst.GSTSettings',N'TenantId'),
  (N'V29',N'COLUMN',N'printing.PrinterConfigurations',N'TenantId'),
  (N'V30',N'COLUMN',N'core.Users',N'MustChangePassword'),
  (N'V30',N'INDEX',N'purchase.Suppliers',N'UX_Suppliers_Code'),
  (N'V30',N'INDEX',N'purchase.Suppliers',N'UX_Suppliers_GSTIN'),
  (N'V31',N'INDEX',N'sales.Customers',N'UX_Customers_Code'),
  (N'V31',N'INDEX',N'master.Products',N'UX_Products_ProductCode'),
  (N'V32',N'INDEX',N'purchase.Suppliers',N'UX_Suppliers_Name'),
  (N'V33',N'COLUMN',N'purchase.PurchaseInvoices',N'SupplierInvoiceNo'),
  (N'V34',N'TABLE',N'integration',N'WhatsAppCommerceConversations'),
  (N'V34',N'TABLE',N'integration',N'WhatsAppCommerceCartLines'),
  (N'V34',N'TABLE',N'integration',N'WhatsAppCommerceInbound'),
  (N'V34',N'TABLE',N'integration',N'WhatsAppCommerceOutbound'),
  (N'V35',N'TABLE',N'integration',N'WhatsAppMessageUsage'),
  (N'V36',N'TABLE',N'commerce',N'TenantPaymentConfigurations'),
  (N'V36',N'TABLE',N'commerce',N'TenantPaymentProviders'),
  (N'V36',N'TABLE',N'commerce',N'CommercePayments'),
  (N'V36',N'TABLE',N'commerce',N'PaymentProviderEvents'),
  (N'V36',N'TABLE',N'commerce',N'PaymentApplications'),
  (N'V37',N'TABLE',N'core',N'TenantResourceLimits'),
  (N'V37',N'CHECK',N'core.TenantResourceLimits',N'CK_TenantResourceLimits_ResourceType'),
  (N'V37',N'CHECK',N'core.TenantResourceLimits',N'CK_TenantResourceLimits_LimitValue'),
  (N'V38',N'TABLE',N'commerce',N'StorefrontMedia'),
  (N'V38',N'TABLE',N'commerce',N'StorefrontConfigurations'),
  (N'V38',N'TABLE',N'commerce',N'StorefrontBanners'),
  (N'V39',N'TABLE',N'commerce',N'StorefrontOtpChallenges'),
  (N'V39',N'TABLE',N'commerce',N'StorefrontWishlistItems'),
  (N'V39',N'TABLE',N'commerce',N'StorefrontRefunds'),
  (N'V39',N'TABLE',N'commerce',N'StorefrontRefundAttempts'),
  (N'V39',N'TABLE',N'sales',N'FullSaleReversals'),
  (N'V40',N'COLUMN',N'commerce.StorefrontConfigurations',N'AllCategoryMediaId'),
  (N'V40',N'FOREIGN_KEY',N'commerce.StorefrontConfigurations',N'FK_StorefrontConfigurations_AllCategoryMedia'),
  (N'V40',N'CHECK',N'commerce.StorefrontMedia',N'CK_StorefrontMedia_Resource')
 ) v(MigrationVersion,ObjectKind,ParentName,ObjectName)
)
SELECT e.MigrationVersion,e.ObjectKind,e.ParentName,e.ObjectName,
 CASE e.ObjectKind
  WHEN N'COLUMN' THEN CASE WHEN COL_LENGTH(e.ParentName,e.ObjectName) IS NULL THEN N'MISSING' ELSE N'PRESENT' END
  WHEN N'TABLE' THEN CASE WHEN OBJECT_ID(QUOTENAME(e.ParentName)+N'.'+QUOTENAME(e.ObjectName),N'U') IS NULL THEN N'MISSING' ELSE N'PRESENT' END
  WHEN N'PROCEDURE' THEN CASE WHEN OBJECT_ID(QUOTENAME(e.ParentName)+N'.'+QUOTENAME(e.ObjectName),N'P') IS NULL THEN N'MISSING' ELSE N'PRESENT' END
  WHEN N'INDEX' THEN CASE WHEN EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(e.ParentName) AND i.name=e.ObjectName) THEN N'PRESENT' ELSE N'MISSING' END
  WHEN N'CHECK' THEN CASE WHEN EXISTS(SELECT 1 FROM sys.check_constraints c WHERE c.parent_object_id=OBJECT_ID(e.ParentName) AND c.name=e.ObjectName) THEN N'PRESENT' ELSE N'MISSING' END
  WHEN N'FOREIGN_KEY' THEN CASE WHEN EXISTS(SELECT 1 FROM sys.foreign_keys f WHERE f.parent_object_id=OBJECT_ID(e.ParentName) AND f.name=e.ObjectName) THEN N'PRESENT' ELSE N'MISSING' END
 END AS Status
FROM Expected e
ORDER BY e.MigrationVersion,e.ParentName,e.ObjectName;

GO
SELECT N'REQUIRED_COLUMN' AS CheckSection,v.TableName,v.ColumnName,
 CASE WHEN COL_LENGTH(v.TableName,v.ColumnName) IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status
FROM (VALUES
 (N'sales.Customers',N'MobileNormalized'),
 (N'master.Products',N'PackSize'),
 (N'commerce.StorefrontConfigurations',N'AllCategoryMediaId'),
 (N'commerce.StorefrontConfigurations',N'FreeDeliveryThreshold'),
 (N'commerce.StorefrontConfigurations',N'ShowProductRatings'),
 (N'commerce.StorefrontConfigurations',N'ShowProductReviews'),
 (N'commerce.StorefrontConfigurations',N'DeliveryEnabled'),
 (N'commerce.StorefrontConfigurations',N'StandardDeliveryCharge'),
 (N'commerce.StorefrontConfigurations',N'FreeDeliveryEnabled'),
 (N'commerce.StorefrontConfigurations',N'DeliveryChargeTaxEnabled'),
 (N'commerce.StorefrontConfigurations',N'DeliveryChargeIncomePostingEnabled'),
 (N'sales.SalesInvoices',N'DeliveryCharge'),
 (N'sales.SalesInvoices',N'DeliveryTaxAmount'),
 (N'sales.SalesInvoices',N'PromotionDiscountAmount'),
 (N'sales.SalesInvoices',N'AppliedPromotionName'),
 (N'sales.SalesInvoices',N'FreeDeliveryApplied'),
 (N'sales.SalesInvoices',N'FreeDeliveryThresholdSnapshot'),
 (N'sales.SalesInvoices',N'ServicePincode'),
 (N'sales.SalesInvoices',N'AppliedPromotionId')
) v(TableName,ColumnName)
ORDER BY v.TableName,v.ColumnName;

GO
SELECT N'MOBILE_NORMALIZED_INDEX' AS CheckSection,i.name AS IndexName,
 i.is_unique AS IsUnique,i.is_disabled AS IsDisabled,i.has_filter AS HasFilter,
 i.filter_definition AS FilterDefinition,
 CASE WHEN i.index_id IS NULL THEN N'MISSING'
      WHEN i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=1
       AND i.filter_definition LIKE N'%TenantId%IS NOT NULL%'
       AND i.filter_definition LIKE N'%MobileNormalized%IS NOT NULL%'
       AND i.filter_definition LIKE N'%IsDeleted%0%'
      THEN N'PRESENT_AS_REQUIRED' ELSE N'DIFFERENT' END AS Status
FROM (VALUES(1)) anchor(n)
LEFT JOIN sys.indexes i ON i.object_id=OBJECT_ID(N'sales.Customers')
 AND i.name=N'UX_Customers_TenantMobileNormalized';

GO
GO
SELECT N'V37_RESOURCE_CAPACITY' AS CheckSection,v.ObjectName,
 CASE WHEN v.Kind=N'TABLE' AND OBJECT_ID(v.ObjectName,N'U') IS NOT NULL THEN N'PRESENT'
      WHEN v.Kind=N'TRIGGER' AND OBJECT_ID(v.ObjectName,N'TR') IS NOT NULL THEN N'PRESENT'
      ELSE N'MISSING' END AS Status,
 t.is_disabled AS IsDisabled,sm.uses_ansi_nulls AS UsesAnsiNulls,
 sm.uses_quoted_identifier AS UsesQuotedIdentifier
FROM (VALUES
 (N'TABLE',N'core.TenantResourceLimits'),
 (N'TRIGGER',N'core.TR_TenantResourceLimits_Ownership'),
 (N'TRIGGER',N'admin.TR_Branches_ResourceCapacity'),
 (N'TRIGGER',N'core.TR_Users_ResourceCapacity')
) v(Kind,ObjectName)
LEFT JOIN sys.triggers t ON t.object_id=OBJECT_ID(v.ObjectName)
LEFT JOIN sys.sql_modules sm ON sm.object_id=t.object_id
ORDER BY v.ObjectName;

GO
SELECT N'V39_STOREFRONT_TABLE' AS CheckSection,v.TableName,
 CASE WHEN OBJECT_ID(v.TableName,N'U') IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status
FROM (VALUES
 (N'commerce.StorefrontOtpChallenges'),
 (N'commerce.StorefrontWishlistItems'),(N'commerce.StorefrontProductReviews'),
 (N'commerce.StorefrontServiceablePincodes'),(N'commerce.StorefrontPromotions'),
 (N'commerce.StorefrontPromotionUses'),(N'commerce.StorefrontCancellationRequests'),
 (N'commerce.StorefrontRefunds'),(N'commerce.StorefrontRefundAttempts'),
 (N'sales.FullSaleReversals')
) v(TableName)
ORDER BY v.TableName;

GO
SELECT N'VERSIONED_COLUMN' AS CheckSection,v.TableName,v.ColumnName,
       CASE WHEN COL_LENGTH(v.TableName,v.ColumnName) IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status
FROM (VALUES
 (N'commerce.StorefrontConfigurations',N'ShowProductRatings'),
 (N'commerce.StorefrontConfigurations',N'ShowProductReviews'),
 (N'commerce.StorefrontConfigurations',N'DeliveryEnabled'),
 (N'commerce.StorefrontConfigurations',N'StandardDeliveryCharge'),
 (N'commerce.StorefrontConfigurations',N'FreeDeliveryEnabled'),
 (N'commerce.StorefrontConfigurations',N'DeliveryChargeTaxEnabled'),
 (N'commerce.StorefrontConfigurations',N'DeliveryChargeIncomePostingEnabled'),
 (N'sales.SalesInvoices',N'DeliveryTaxAmount'),
 (N'sales.SalesInvoices',N'PromotionDiscountAmount'),
 (N'sales.SalesInvoices',N'AppliedPromotionName'),
 (N'sales.SalesInvoices',N'FreeDeliveryApplied'),
 (N'sales.SalesInvoices',N'FreeDeliveryThresholdSnapshot'),
 (N'sales.SalesInvoices',N'ServicePincode')
) v(TableName,ColumnName)
ORDER BY v.TableName,v.ColumnName;

GO
SELECT N'VERSIONED_INDEX' AS CheckSection,v.ObjectName,
       CASE WHEN i.index_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status,
       i.is_unique AS IsUnique,i.is_disabled AS IsDisabled,i.has_filter AS HasFilter,
       i.filter_definition AS FilterDefinition
FROM (VALUES
 (N'UX_Customers_TenantMobileNormalized'),(N'IX_StorefrontOtpChallenges_Lookup'),
 (N'IX_StorefrontOtpChallenges_Expiry'),(N'IX_StorefrontWishlistItems_CustomerCreated'),
 (N'IX_StorefrontProductReviews_ProductStatus'),(N'IX_StorefrontPromotions_Active'),
 (N'IX_StorefrontPromotionUses_Customer'),(N'UX_StorefrontCancellationRequests_Active'),
 (N'IX_StorefrontCancellationRequests_Order'),(N'UX_StorefrontRefunds_FullCancelPayment'),
 (N'UX_StorefrontRefunds_ProviderId'),(N'UX_StorefrontRefunds_SettlementJournal'),
 (N'IX_StorefrontRefunds_Order'),(N'IX_FullSaleReversals_TenantCreated')
) v(ObjectName)
LEFT JOIN sys.indexes i ON i.name=v.ObjectName
ORDER BY v.ObjectName;

GO
SELECT N'VERSIONED_FOREIGN_KEY' AS CheckSection,v.ObjectName,
       CASE WHEN f.object_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status,
       f.is_disabled AS IsDisabled,f.is_not_trusted AS IsNotTrusted
FROM (VALUES
 (N'FK_StorefrontConfigurations_AllCategoryMedia'),(N'FK_StorefrontConfigurations_Logo'),
 (N'FK_StorefrontOtpChallenges_Tenant'),(N'FK_StorefrontWishlistItems_Customer'),
 (N'FK_StorefrontWishlistItems_Product'),(N'FK_StorefrontWishlistItems_Tenant'),
 (N'FK_StorefrontProductReviews_Customer'),(N'FK_StorefrontProductReviews_Product'),
 (N'FK_StorefrontProductReviews_Tenant'),(N'FK_StorefrontPromotions_Tenant'),
 (N'FK_StorefrontPromotionUses_Customer'),(N'FK_StorefrontPromotionUses_Invoice'),
 (N'FK_StorefrontPromotionUses_Promotion'),(N'FK_SalesInvoices_AppliedPromotion'),
 (N'FK_StorefrontCancellationRequests_Customer'),(N'FK_StorefrontCancellationRequests_Invoice'),
 (N'FK_StorefrontCancellationRequests_Tenant'),(N'FK_StorefrontRefunds_Cancellation'),
 (N'FK_StorefrontRefunds_Invoice'),(N'FK_StorefrontRefunds_Payment'),
 (N'FK_StorefrontRefunds_SettlementJournal'),(N'FK_StorefrontRefunds_Tenant'),
 (N'FK_StorefrontRefundAttempts_Refund'),(N'FK_StorefrontRefundAttempts_Tenant'),
 (N'FK_FullSaleReversals_Invoice'),(N'FK_FullSaleReversals_OriginalSaleJournal'),
 (N'FK_FullSaleReversals_OriginalStock'),(N'FK_FullSaleReversals_RestoredStock'),
 (N'FK_FullSaleReversals_ReversalJournal'),(N'FK_FullSaleReversals_Tenant')
) v(ObjectName)
LEFT JOIN sys.foreign_keys f ON f.name=v.ObjectName
ORDER BY v.ObjectName;

GO
SELECT N'VERSIONED_CHECK_CONSTRAINT' AS CheckSection,v.ObjectName,
       CASE WHEN c.object_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status,
       c.is_disabled AS IsDisabled,c.is_not_trusted AS IsNotTrusted,c.definition
FROM (VALUES
 (N'CK_TenantResourceLimits_ResourceType'),(N'CK_TenantResourceLimits_LimitValue'),
 (N'CK_StorefrontOtpChallenges_Attempts'),(N'CK_StorefrontOtpChallenges_Expiry'),
 (N'CK_StorefrontOtpChallenges_Mobile'),(N'CK_StorefrontConfigurations_FreeDeliveryThreshold'),
 (N'CK_StorefrontConfigurations_DeliveryPricing'),(N'CK_SalesInvoices_StorefrontAmounts'),
 (N'CK_StorefrontCancellationRequests_Status'),(N'CK_StorefrontCancellationRequests_Reason'),
 (N'CK_StorefrontCancellationRequests_Decision'),(N'CK_StorefrontRefunds_Provider'),
 (N'CK_StorefrontRefunds_Type'),(N'CK_StorefrontRefunds_Status'),(N'CK_StorefrontRefunds_Amount'),
 (N'CK_StorefrontRefunds_Final'),(N'CK_StorefrontRefundAttempts_Status'),
 (N'CK_StorefrontRefundAttempts_Number'),(N'CK_FullSaleReversals_Amounts'),
 (N'CK_FullSaleReversals_RefundStatus'),(N'CK_FullSaleReversals_Reason'),
 (N'CK_StorefrontMedia_Resource')
) v(ObjectName)
LEFT JOIN sys.check_constraints c ON c.name=v.ObjectName
ORDER BY v.ObjectName;

GO
SELECT N'V40_RESOURCE_CONSTRAINT' AS CheckSection,c.name AS ConstraintName,
       c.definition,c.is_disabled AS IsDisabled,c.is_not_trusted AS IsNotTrusted,
       CASE WHEN c.object_id IS NULL THEN N'MISSING'
            WHEN c.definition LIKE N'%category-all%' THEN N'PRESENT_AS_REQUIRED' ELSE N'DIFFERENT' END AS Status
FROM (VALUES(1)) anchor(n)
LEFT JOIN sys.check_constraints c ON c.parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia')
 AND c.name=N'CK_StorefrontMedia_Resource';

GO
SELECT N'SALE_REVERSAL_INVENTORY_CONSTRAINT' AS CheckSection,c.name AS ConstraintName,
       c.definition,c.is_disabled AS IsDisabled,c.is_not_trusted AS IsNotTrusted,
       CASE WHEN c.object_id IS NULL THEN N'MISSING'
            WHEN c.definition LIKE N'%SALE_REVERSAL%' AND c.is_disabled=0 THEN N'PRESENT_AS_REQUIRED'
            ELSE N'DIFFERENT' END AS Status
FROM (VALUES(1)) anchor(n)
LEFT JOIN sys.check_constraints c ON c.parent_object_id=OBJECT_ID(N'inventory.InventoryTransactions')
 AND c.name=N'CK_InventoryTransactions_Type';

GO
SELECT N'TENANT_GUARD_TRIGGER' AS CheckSection,
       OBJECT_SCHEMA_NAME(t.parent_id) AS TableSchema,OBJECT_NAME(t.parent_id) AS TableName,
       v.TriggerName,t.is_disabled AS IsDisabled,
       sm.uses_ansi_nulls AS UsesAnsiNulls,sm.uses_quoted_identifier AS UsesQuotedIdentifier,
       CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),OBJECT_DEFINITION(t.object_id))),2) AS DefinitionSha256,
       CASE WHEN t.object_id IS NULL THEN N'MISSING' WHEN t.is_disabled=1 THEN N'DISABLED' ELSE N'ENABLED' END AS Status
FROM (VALUES
 (N'core.TR_Users_ResourceCapacity'),
 (N'core.TR_TenantResourceLimits_Ownership'),
 (N'admin.TR_Branches_ResourceCapacity'),
 (N'commerce.TR_StorefrontWishlistItems_TenantGuard'),
 (N'commerce.TR_StorefrontProductReviews_TenantGuard'),
 (N'commerce.TR_StorefrontPromotionUses_TenantGuard'),
 (N'commerce.TR_StorefrontCancellationRequests_TenantGuard'),
 (N'commerce.TR_StorefrontRefunds_TenantGuard'),
 (N'commerce.TR_StorefrontRefundAttempts_TenantGuard'),
 (N'sales.TR_FullSaleReversals_TenantGuard')
) v(TriggerName)
LEFT JOIN sys.triggers t ON t.object_id=OBJECT_ID(v.TriggerName)
LEFT JOIN sys.sql_modules sm ON sm.object_id=t.object_id
ORDER BY v.TriggerName;

GO
SELECT N'PROCEDURE_COMPARISON' AS CheckSection,v.SchemaName,v.ProcedureName,
       CASE WHEN p.object_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Status,
       sm.uses_ansi_nulls AS UsesAnsiNulls,sm.uses_quoted_identifier AS UsesQuotedIdentifier,
       p.create_date AS CreateDate,p.modify_date AS ModifyDate,
       CASE WHEN sm.definition IS NULL THEN NULL
            ELSE CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),sm.definition)),2) END AS DefinitionSha256,
       sm.definition AS ProcedureDefinition
FROM (VALUES
 (N'finance',N'PostSource'),(N'sales',N'POS_AddPayment'),
 (N'sales',N'POS_PostInvoice'),(N'sales',N'POS_ReverseFullSale'),
 (N'finance',N'SettleStorefrontRefund')
) v(SchemaName,ProcedureName)
LEFT JOIN sys.schemas s ON s.name=v.SchemaName
LEFT JOIN sys.procedures p ON p.schema_id=s.schema_id AND p.name=v.ProcedureName
LEFT JOIN sys.sql_modules sm ON sm.object_id=p.object_id
ORDER BY v.SchemaName,v.ProcedureName;

GO
SELECT N'REQUIRED_CONSTRAINT' AS CheckSection,
       OBJECT_SCHEMA_NAME(c.parent_object_id) AS TableSchema,
       OBJECT_NAME(c.parent_object_id) AS TableName,c.name AS ConstraintName,
       c.is_disabled AS IsDisabled,c.is_not_trusted AS IsNotTrusted,c.definition
FROM sys.check_constraints c
WHERE (c.parent_object_id=OBJECT_ID(N'core.Users') AND c.name=N'CK_Users_AccountScope')
ORDER BY TableSchema,TableName,ConstraintName;

GO
IF OBJECT_ID(N'sales.Customers',N'U') IS NULL
    SELECT N'MOBILE_NORMALIZED_DUPLICATES' AS CheckSection,N'TABLE_MISSING' AS Status,
           CAST(NULL AS bigint) AS DuplicateValueGroups,CAST(NULL AS bigint) AS ExtraRowsBlockingUniqueIndex
ELSE IF COL_LENGTH(N'sales.Customers',N'MobileNormalized') IS NULL
    SELECT N'MOBILE_NORMALIZED_DUPLICATES' AS CheckSection,N'COLUMN_MISSING' AS Status,
           CAST(NULL AS bigint) AS DuplicateValueGroups,CAST(NULL AS bigint) AS ExtraRowsBlockingUniqueIndex
ELSE
    EXEC sys.sp_executesql N'
        SELECT N''MOBILE_NORMALIZED_DUPLICATES'' AS CheckSection,N''CHECKED'' AS Status,
               COUNT_BIG(*) AS DuplicateValueGroups,
               COALESCE(SUM(d.DuplicateRows-1),0) AS ExtraRowsBlockingUniqueIndex
        FROM
        (
            SELECT c.TenantId,c.MobileNormalized,COUNT_BIG(*) AS DuplicateRows
            FROM sales.Customers c
            WHERE c.TenantId IS NOT NULL AND c.MobileNormalized IS NOT NULL AND c.IsDeleted=0
            GROUP BY c.TenantId,c.MobileNormalized
            HAVING COUNT_BIG(*)>1
        ) d;';
GO
