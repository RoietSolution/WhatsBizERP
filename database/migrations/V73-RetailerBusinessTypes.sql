SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.StoreDirectoryEntries',N'U') IS NULL
 THROW 73000,N'V73 requires V72 directory schema.',1;
IF OBJECT_ID(N'core.BusinessTypes',N'U') IS NULL
 CREATE TABLE core.BusinessTypes(
  Code varchar(40) NOT NULL CONSTRAINT PK_BusinessTypes PRIMARY KEY,
  Name nvarchar(80) NOT NULL,
  NormalizedName AS UPPER(LTRIM(RTRIM(Name))) PERSISTED,
  IsActive bit NOT NULL CONSTRAINT DF_BusinessTypes_Active DEFAULT(1),
  Version rowversion NOT NULL,CreatedBy nvarchar(256) NULL,ModifiedBy nvarchar(256) NULL,
  CONSTRAINT UQ_BusinessTypes_Name UNIQUE(NormalizedName),
  CONSTRAINT CK_BusinessTypes_Code CHECK(Code COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%' AND LEFT(Code,1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z]'),
  CONSTRAINT CK_BusinessTypes_Name CHECK(LEN(LTRIM(RTRIM(Name)))>0)
 );
IF COL_LENGTH(N'commerce.StoreDirectoryEntries',N'BusinessTypeCode') IS NULL
 ALTER TABLE commerce.StoreDirectoryEntries ADD BusinessTypeCode varchar(40) NULL;
GO
/* Catalog choices only, never fictitious retailer rows. Never overwrite owner edits. */
INSERT core.BusinessTypes(Code,Name,CreatedBy)
SELECT Code,Name,N'V73' FROM (VALUES
 ('GROCERY',N'Grocery & Essentials'),('FOOD',N'Food & Snacks'),('FASHION',N'Clothing & Fashion'),
 ('ELECTRONICS',N'Electronics'),('GENERAL_RETAIL',N'General Retail'),('OTHER',N'Other')) choices(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM core.BusinessTypes b WHERE b.Code=choices.Code OR b.NormalizedName=UPPER(choices.Name));
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StoreDirectoryEntries_BusinessType')
 ALTER TABLE commerce.StoreDirectoryEntries WITH CHECK ADD CONSTRAINT FK_StoreDirectoryEntries_BusinessType
 FOREIGN KEY(BusinessTypeCode) REFERENCES core.BusinessTypes(Code);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StoreDirectoryEntries') AND name=N'IX_StoreDirectoryEntries_BusinessType')
 CREATE INDEX IX_StoreDirectoryEntries_BusinessType ON commerce.StoreDirectoryEntries(BusinessTypeCode);
/* Preserve every legacy label, description and publication decision. Link only
   unambiguous exact normalized code/name matches; unknown values remain NULL. */
;WITH matches AS(
 SELECT e.TenantId,MIN(b.Code) Code,COUNT(*) MatchCount
 FROM commerce.StoreDirectoryEntries e JOIN core.BusinessTypes b
 ON b.Code=UPPER(LTRIM(RTRIM(e.BusinessType))) OR b.NormalizedName=UPPER(LTRIM(RTRIM(e.BusinessType)))
 WHERE e.BusinessTypeCode IS NULL GROUP BY e.TenantId
)
UPDATE e SET BusinessTypeCode=m.Code FROM commerce.StoreDirectoryEntries e JOIN matches m ON m.TenantId=e.TenantId AND m.MatchCount=1;
GO
