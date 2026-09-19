CREATE TABLE [sales].[CustomerGroups]
(
    [CustomerGroupId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CustomerGroups_Id] DEFAULT NEWSEQUENTIALID(),
    [TenantId] UNIQUEIDENTIFIER NULL,
    [GroupCode] NVARCHAR(50) NOT NULL,
    [GroupName] NVARCHAR(150) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_CustomerGroups_Active] DEFAULT 1,
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_CustomerGroups_Created] DEFAULT SYSUTCDATETIME(),
    [CreatedBy] NVARCHAR(256) NULL,
    CONSTRAINT [PK_CustomerGroups] PRIMARY KEY ([CustomerGroupId]),
    CONSTRAINT [UQ_CustomerGroups_TenantCode] UNIQUE ([TenantId], [GroupCode]),
    CONSTRAINT [UQ_CustomerGroups_TenantName] UNIQUE ([TenantId], [GroupName])
);
GO
