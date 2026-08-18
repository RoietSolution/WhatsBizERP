CREATE TABLE [core].[IdempotencyRequests]
(
    [IdempotencyRequestId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_IdempotencyRequests_Id] DEFAULT NEWSEQUENTIALID(),
    [IdempotencyKey] UNIQUEIDENTIFIER NOT NULL,
    [OperationType] NVARCHAR(80) NOT NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [RequestedBy] NVARCHAR(256) NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ResponseJson] NVARCHAR(MAX) NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_IdempotencyRequests_CreatedOn] DEFAULT SYSUTCDATETIME(),
    [CompletedOn] DATETIMEOFFSET NULL,
    CONSTRAINT [PK_IdempotencyRequests] PRIMARY KEY ([IdempotencyRequestId]),
    CONSTRAINT [UQ_IdempotencyRequests_Key] UNIQUE ([IdempotencyKey]),
    CONSTRAINT [CK_IdempotencyRequests_Status] CHECK ([Status] IN (N'PROCESSING', N'COMPLETED')),
    CONSTRAINT [CK_IdempotencyRequests_Response] CHECK
    (
        ([Status] = N'PROCESSING' AND [ResponseJson] IS NULL AND [CompletedOn] IS NULL)
        OR ([Status] = N'COMPLETED' AND [ResponseJson] IS NOT NULL AND [CompletedOn] IS NOT NULL)
    )
);
GO
CREATE INDEX [IX_IdempotencyRequests_StatusCreated]
    ON [core].[IdempotencyRequests] ([Status], [CreatedOn]);
GO
