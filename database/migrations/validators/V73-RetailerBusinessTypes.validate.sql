SET NOCOUNT ON;
IF OBJECT_ID(N'core.BusinessTypes',N'U') IS NULL OR COL_LENGTH(N'commerce.StoreDirectoryEntries',N'BusinessTypeCode') IS NULL
 THROW 73001,N'Business type catalog/schema missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StoreDirectoryEntries_BusinessType' AND is_disabled=0 AND is_not_trusted=0)
 THROW 73002,N'Business type FK missing or untrusted.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'core.BusinessTypes') AND name=N'UQ_BusinessTypes_Name' AND is_unique=1 AND is_disabled=0)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StoreDirectoryEntries') AND name=N'IX_StoreDirectoryEntries_BusinessType' AND is_disabled=0)
 THROW 73003,N'Business type indexes missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'core.BusinessTypes') AND name=N'CK_BusinessTypes_Code' AND is_disabled=0 AND is_not_trusted=0)
 THROW 73004,N'Business type code validation missing.',1;
IF EXISTS(SELECT 1 FROM commerce.StoreDirectoryEntries e WHERE e.BusinessTypeCode IS NOT NULL AND NOT EXISTS(SELECT 1 FROM core.BusinessTypes b WHERE b.Code=e.BusinessTypeCode))
 THROW 73005,N'Invalid linked business type.',1;
SELECT N'V73 validated; legacy NULL codes remain valid and require explicit completion when edited.' Result;
