SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.CityMarketplaceMedia',N'U') IS NULL
    THROW 75100,N'V75 city media table is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'PK_CityMarketplaceMedia' AND is_primary_key=1)
    THROW 75101,N'V75 city media primary key is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UQ_CityMarketplaceMedia_CityResource' AND is_unique=1)
    THROW 75102,N'V75 city resource uniqueness is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CityMarketplaceMedia_City' AND is_disabled=0 AND is_not_trusted=0)
    THROW 75103,N'V75 city media ownership FK is missing or untrusted.',1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name IN(N'CK_CityMarketplaceMedia_Resource',N'CK_CityMarketplaceMedia_Storage',N'CK_CityMarketplaceMedia_Content') AND is_disabled=0 AND is_not_trusted=0)<>3
    THROW 75104,N'V75 city media validation constraints are missing or untrusted.',1;
IF EXISTS(SELECT 1 FROM commerce.CityMarketplaceMedia m LEFT JOIN commerce.Cities c ON c.CityId=m.CityId WHERE c.CityId IS NULL)
    THROW 75105,N'V75 city media contains an orphan record.',1;
IF EXISTS(SELECT 1 FROM commerce.Cities c WHERE (c.LogoMediaId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM commerce.CityMarketplaceMedia m WHERE m.MediaId=c.LogoMediaId AND m.CityId=c.CityId AND m.ResourceType='LOGO')) OR (c.BannerMediaId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM commerce.CityMarketplaceMedia m WHERE m.MediaId=c.BannerMediaId AND m.CityId=c.CityId AND m.ResourceType='BANNER')))
    THROW 75106,N'V75 city media association is missing or points to another city/resource.',1;
PRINT N'V75 city media validator passed.';
