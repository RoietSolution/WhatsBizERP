CREATE TABLE [inventory].[WarehouseTypes] (
    [WarehouseTypeId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WarehouseTypes_Id] DEFAULT NEWSEQUENTIALID(),
    [TypeCode] NVARCHAR(30) NOT NULL,
    [TypeName] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_WarehouseTypes_Active] DEFAULT 1,
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_WarehouseTypes_Created] DEFAULT SYSUTCDATETIME(),
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_WarehouseTypes] PRIMARY KEY ([WarehouseTypeId]),
    CONSTRAINT [UQ_WarehouseTypes_Code] UNIQUE ([TypeCode]),
    CONSTRAINT [UQ_WarehouseTypes_Name] UNIQUE ([TypeName])
);
