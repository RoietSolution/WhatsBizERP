SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.ProductPublications',N'U') IS NULL OR OBJECT_ID(N'commerce.StoreDirectoryEntries',N'U') IS NULL
 OR OBJECT_ID(N'commerce.PublicCatalogAudit',N'U') IS NULL OR OBJECT_ID(N'commerce.PublicCatalogBaseline',N'U') IS NULL
 THROW 72000,N'Public catalog governance tables missing.',1;
IF OBJECT_ID(N'commerce.ProductPublicContentHash',N'FN') IS NULL OR OBJECT_ID(N'commerce.IsProductPubliclyApproved',N'FN') IS NULL
 THROW 72001,N'Public catalog eligibility functions missing.',1;
IF COL_LENGTH(N'commerce.ProductPublications',N'Version') IS NULL OR COL_LENGTH(N'commerce.StoreDirectoryEntries',N'Version') IS NULL
 THROW 72002,N'Catalog concurrency columns missing.',1;
IF NOT EXISTS(SELECT 1 FROM commerce.PublicCatalogBaseline WHERE Id=1)
 THROW 72003,N'Legacy baseline marker missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.ProductPublications') AND name=N'IX_ProductPublications_Review' AND is_disabled=0)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'IX_ProductImages_PublicContent' AND is_disabled=0)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicCatalogAudit') AND name=N'IX_PublicCatalogAudit_Product' AND is_disabled=0)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StoreDirectoryEntries') AND name=N'IX_StoreDirectoryEntries_Publication' AND is_disabled=0)
 THROW 72006,N'Catalog lookup indexes missing or disabled.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.ProductPublications') AND name=N'CK_ProductPublications_Status' AND is_disabled=0 AND is_not_trusted=0)
 THROW 72007,N'Publication status constraint missing or untrusted.',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name IN(N'FK_ProductPublications_TenantProduct',N'FK_PublicCatalogAudit_TenantProduct') AND (is_disabled=1 OR is_not_trusted=1))
 OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN(N'FK_ProductPublications_TenantProduct',N'FK_PublicCatalogAudit_TenantProduct'))<>2
 THROW 72004,N'Tenant-product foreign keys invalid.',1;
IF EXISTS(SELECT 1 FROM commerce.ProductPublications a JOIN master.Products p ON p.ProductId=a.ProductId WHERE p.TenantId<>a.TenantId)
 THROW 72005,N'Cross-tenant publication detected.',1;
SELECT N'V72 public catalog governance validated' AS Result;
