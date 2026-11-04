/* Mandatory governance for new public products. Legacy is an explicit, bounded baseline,
   NOT approval. No existing storefront is opted into the directory automatically. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.Products') AND name=N'UX_Products_PublicationTenant')
 CREATE UNIQUE INDEX UX_Products_PublicationTenant ON master.Products(TenantId,ProductId);
IF OBJECT_ID(N'commerce.PublicCatalogBaseline',N'U') IS NULL
 CREATE TABLE commerce.PublicCatalogBaseline(Id int NOT NULL PRIMARY KEY CHECK(Id=1),InstalledAt datetimeoffset NOT NULL);
IF OBJECT_ID(N'commerce.ProductPublications',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.ProductPublications(
  TenantId uniqueidentifier NOT NULL REFERENCES core.Tenants(TenantId),
  ProductId uniqueidentifier NOT NULL,
  Status varchar(24) NOT NULL,
  ContentHash varbinary(32) NOT NULL,
  SubmittedAt datetimeoffset NULL, DecidedAt datetimeoffset NULL,
  Reason nvarchar(1000) NULL, Reviewer nvarchar(256) NULL,
  Version rowversion NOT NULL,
  CONSTRAINT PK_ProductPublications PRIMARY KEY(TenantId,ProductId),
  CONSTRAINT FK_ProductPublications_TenantProduct FOREIGN KEY(TenantId,ProductId) REFERENCES master.Products(TenantId,ProductId),
  CONSTRAINT CK_ProductPublications_Status CHECK(Status IN('LEGACY','DRAFT','PENDING_APPROVAL','APPROVED','REJECTED','SUSPENDED'))
 );
 CREATE INDEX IX_ProductPublications_Review ON commerce.ProductPublications(Status,SubmittedAt,TenantId);
END;
IF OBJECT_ID(N'commerce.PublicCatalogAudit',N'U') IS NULL
 CREATE TABLE commerce.PublicCatalogAudit(
  AuditId bigint IDENTITY PRIMARY KEY,TenantId uniqueidentifier NOT NULL REFERENCES core.Tenants(TenantId),
  ProductId uniqueidentifier NULL,
  Action varchar(32) NOT NULL, Actor nvarchar(256) NOT NULL, Reason nvarchar(1000) NULL,
  ContentSnapshot nvarchar(max) NULL, OccurredAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT FK_PublicCatalogAudit_TenantProduct FOREIGN KEY(TenantId,ProductId) REFERENCES master.Products(TenantId,ProductId)
 );
IF OBJECT_ID(N'commerce.StoreDirectoryEntries',N'U') IS NULL
 CREATE TABLE commerce.StoreDirectoryEntries(
  TenantId uniqueidentifier NOT NULL PRIMARY KEY REFERENCES core.Tenants(TenantId),
  BusinessType nvarchar(80) NOT NULL, ShortDescription nvarchar(180) NOT NULL,
  OptedIn bit NOT NULL DEFAULT 0, OwnerApproved bit NOT NULL DEFAULT 0,
  Version rowversion NOT NULL
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'IX_ProductImages_PublicContent')
 CREATE INDEX IX_ProductImages_PublicContent ON master.ProductImages(TenantId,ProductId,IsActive,IsDeleted) INCLUDE(RowVersion);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicCatalogAudit') AND name=N'IX_PublicCatalogAudit_Product')
 CREATE INDEX IX_PublicCatalogAudit_Product ON commerce.PublicCatalogAudit(TenantId,ProductId,AuditId DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StoreDirectoryEntries') AND name=N'IX_StoreDirectoryEntries_Publication')
 CREATE INDEX IX_StoreDirectoryEntries_Publication ON commerce.StoreDirectoryEntries(OwnerApproved,OptedIn,TenantId);
GO
/* Fingerprint only public content. Inventory, purchase cost, audit timestamps and
   stock movements do not change it. Media/specification versions cover all edits. */
CREATE OR ALTER FUNCTION commerce.ProductPublicContentHash(@tenant uniqueidentifier,@product uniqueidentifier)
RETURNS varbinary(32)
AS
BEGIN
 DECLARE @content nvarchar(max),@images nvarchar(max),@specs nvarchar(max);
 SELECT @images=STRING_AGG(CONVERT(nvarchar(max),CONCAT(CONVERT(varchar(36),ProductImageId),':',CONVERT(varchar(18),RowVersion,1))),',') WITHIN GROUP(ORDER BY ProductImageId)
 FROM master.ProductImages WHERE TenantId=@tenant AND ProductId=@product AND IsActive=1 AND IsDeleted=0;
 SELECT @specs=STRING_AGG(CONVERT(nvarchar(max),CONCAT(CONVERT(varchar(36),ProductSpecificationId),':',CONVERT(varchar(18),RowVersion,1))),',') WITHIN GROUP(ORDER BY ProductSpecificationId)
 FROM master.ProductSpecifications WHERE TenantId=@tenant AND ProductId=@product AND IsActive=1 AND IsDeleted=0;
 SELECT @content=CONCAT(CONVERT(nvarchar(max),ProductId), '|',TenantId,'|',CategoryId,'|',BrandId,'|',UnitId,'|',SellingPrice,'|',MRP,'|',GSTPercentage,'|',GstPricingType,'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'hsn:',HSNCode)),2),'|',CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'sac:',SACCode)),2),'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),ProductName)),2),'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'code:',ProductCode)),2),'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(max),N'short:'),ShortDescription)),2),'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(max),N'long:'),LongDescription)),2),'|',
  CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(max),N'pack:'),PackSize)),2),'|',Weight,'|',Length,'|',Width,'|',Height,'|',
  ReturnPolicyMode,'|',ReturnWindowDays,'|',CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(max),N'return:'),ReturnPolicyDescription)),2),'|',
  @images,'|',@specs)
 FROM master.Products WHERE TenantId=@tenant AND ProductId=@product;
 RETURN HASHBYTES('SHA2_256',@content);
END;
GO
CREATE OR ALTER FUNCTION commerce.IsProductPubliclyApproved(@tenant uniqueidentifier,@product uniqueidentifier)
RETURNS bit
AS
BEGIN
 RETURN CASE WHEN EXISTS(SELECT 1 FROM commerce.ProductPublications
  WHERE TenantId=@tenant AND ProductId=@product AND Status IN('LEGACY','APPROVED')
   AND ContentHash=commerce.ProductPublicContentHash(@tenant,@product)) THEN 1 ELSE 0 END;
END;
GO
/* Seed once only. Reruns MUST NOT grandfather products created after installation. */
IF NOT EXISTS(SELECT 1 FROM commerce.PublicCatalogBaseline WHERE Id=1)
BEGIN
 INSERT commerce.ProductPublications(TenantId,ProductId,Status,ContentHash)
 SELECT p.TenantId,p.ProductId,'LEGACY',commerce.ProductPublicContentHash(p.TenantId,p.ProductId)
 FROM master.Products p WHERE NOT EXISTS(SELECT 1 FROM commerce.ProductPublications a WHERE a.TenantId=p.TenantId AND a.ProductId=p.ProductId);
 INSERT commerce.PublicCatalogAudit(TenantId,Action,Actor,Reason)
 SELECT TenantId,'LEGACY_BASELINE','V72',CONCAT('Existing catalog grandfathered, not approved. Products: ',COUNT_BIG(*))
 FROM commerce.ProductPublications GROUP BY TenantId;
 INSERT commerce.PublicCatalogBaseline(Id,InstalledAt) VALUES(1,SYSDATETIMEOFFSET());
END;
GO
