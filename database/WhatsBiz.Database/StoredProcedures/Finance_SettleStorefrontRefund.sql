CREATE PROCEDURE finance.SettleStorefrontRefund
    @TenantId uniqueidentifier,
    @RefundId uniqueidentifier,
    @SettlementMode nvarchar(30),
    @ExternalReference nvarchar(100),
    @SettledAt datetimeoffset,
    @CreatedBy nvarchar(256)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @TenantId IS NULL OR @TenantId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'))
        THROW 51500,N'Tenant context is missing or mismatched.',1;
    IF @RefundId IS NULL OR NULLIF(LTRIM(RTRIM(@ExternalReference)),N'') IS NULL
        OR LEN(@ExternalReference)>100 OR @SettledAt IS NULL
        THROW 51900,N'A confirmed refund reference and settlement date are required.',1;

    BEGIN TRY
        BEGIN TRAN;
        DECLARE @InvoiceId uniqueidentifier,@CustomerId uniqueidentifier,@PaymentId uniqueidentifier,
            @Provider nvarchar(30),@Amount decimal(18,2),@Status nvarchar(30),
            @Obligation decimal(18,2),@OriginalCollection decimal(18,2),
            @ReversedAt datetimeoffset,@ExistingJournal uniqueidentifier;
        SELECT @InvoiceId=r.InvoiceId,@PaymentId=r.PaymentId,@Provider=r.Provider,
            @Amount=r.RefundAmount,@Status=r.RefundStatus,@ExistingJournal=r.SettlementJournalId
        FROM commerce.StorefrontRefunds r WITH(UPDLOCK,HOLDLOCK)
        WHERE r.TenantId=@TenantId AND r.RefundId=@RefundId AND r.RefundType=N'FULL_CANCEL';
        IF @InvoiceId IS NULL THROW 51901,N'Refund was not found for this tenant.',1;
        IF @Status=N'REFUNDED'
        BEGIN
            IF @ExistingJournal IS NULL THROW 51902,N'Refund settlement is inconsistent.',1;
            SELECT @RefundId RefundId,@ExistingJournal JournalEntryId,@Amount Amount,
                (SELECT SettlementMode FROM commerce.StorefrontRefunds WHERE RefundId=@RefundId) SettlementMode,
                CAST(1 AS bit) AlreadySettled;
            COMMIT;
            RETURN;
        END;
        IF @Status<>N'REFUND_PENDING' OR @ExistingJournal IS NOT NULL
            THROW 51903,N'Refund is not awaiting confirmed settlement.',1;
        SELECT @Obligation=v.RefundRequiredAmount,@OriginalCollection=v.CollectedAmount,
            @ReversedAt=v.CreatedAt
        FROM sales.FullSaleReversals v WITH(UPDLOCK,HOLDLOCK)
        WHERE v.TenantId=@TenantId AND v.InvoiceId=@InvoiceId
          AND v.RefundStatus IN(N'REFUND_REQUIRED',N'REFUND_PENDING');
        SELECT @CustomerId=i.CustomerId FROM sales.SalesInvoices i WITH(UPDLOCK,HOLDLOCK)
        WHERE i.TenantId=@TenantId AND i.InvoiceId=@InvoiceId AND i.Status=N'CANCELLED';
        IF @CustomerId IS NULL OR @Obligation IS NULL OR @Amount<=0 OR @Amount<>@Obligation
            OR @Amount>@OriginalCollection
            OR @SettledAt<@ReversedAt OR @SettledAt>DATEADD(minute,5,SYSUTCDATETIME())
            THROW 51904,N'Refund does not match the committed reversal obligation.',1;
        IF NOT EXISTS(SELECT 1 FROM commerce.CommercePayments p JOIN commerce.PaymentApplications a
            ON a.TenantId=p.TenantId AND a.PaymentId=p.PaymentId AND a.InvoiceId=p.InvoiceId
            WHERE p.TenantId=@TenantId AND p.PaymentId=@PaymentId AND p.InvoiceId=@InvoiceId
              AND p.Provider=@Provider AND p.Status=N'PAID' AND a.Amount>=@Amount)
            THROW 51905,N'An applied collection is required before refund settlement.',1;
        IF EXISTS(SELECT 1 FROM commerce.StorefrontRefunds other
            WHERE other.TenantId=@TenantId AND other.InvoiceId=@InvoiceId AND other.RefundId<>@RefundId
              AND other.RefundStatus=N'REFUNDED')
            THROW 51906,N'The sale has another settled refund.',1;
        IF NOT EXISTS(SELECT 1 FROM commerce.StorefrontRefundAttempts a
            WHERE a.TenantId=@TenantId AND a.RefundId=@RefundId AND a.Status=N'CONFIRMED'
              AND (@Provider<>N'RAZORPAY' OR a.ProviderRefundId IS NOT NULL))
            THROW 51907,N'External money movement has not been confirmed.',1;
        IF (@Provider=N'RAZORPAY' AND @SettlementMode<>N'RAZORPAY')
           OR (@Provider=N'DIRECT_UPI' AND @SettlementMode NOT IN(N'UPI',N'BANK'))
           OR (@Provider=N'COD' AND @SettlementMode NOT IN(N'CASH',N'UPI',N'BANK'))
            THROW 51908,N'Refund settlement mode is invalid for the collection provider.',1;
        IF EXISTS(SELECT 1 FROM finance.JournalEntries
            WHERE ReferenceType=N'CUSTOMER' AND ReferenceId=@RefundId AND TransactionType=N'PAYMENT')
            THROW 51909,N'Refund journal exists without a completed settlement.',1;

        DECLARE @posted table(JournalEntryId uniqueidentifier,JournalNumber nvarchar(50));
        INSERT @posted EXEC finance.PostPartyTransaction
            @TenantId=@TenantId,@TransactionType=N'PAYMENT',@PartyType=N'CUSTOMER',
            @PartyId=@CustomerId,@PaymentMode=@SettlementMode,@Amount=@Amount,
            @EntryDate=@SettledAt,@ReferenceNumber=@ExternalReference,
            @Narration=N'Storefront full-cancellation refund',@CreatedBy=@CreatedBy,@SourceId=@RefundId;
        SELECT @ExistingJournal=JournalEntryId FROM @posted;
        IF @ExistingJournal IS NULL THROW 51910,N'ERP refund posting returned no journal.',1;
        UPDATE commerce.StorefrontRefunds SET RefundStatus=N'REFUNDED',RefundedAt=@SettledAt,
            SettlementJournalId=@ExistingJournal,SettlementMode=@SettlementMode,
            SettlementReference=@ExternalReference,UpdatedAt=SYSUTCDATETIME()
        WHERE TenantId=@TenantId AND RefundId=@RefundId AND RefundStatus=N'REFUND_PENDING';
        IF @@ROWCOUNT<>1 THROW 51911,N'Refund status changed during settlement.',1;
        UPDATE sales.FullSaleReversals SET RefundStatus=N'REFUNDED'
        WHERE TenantId=@TenantId AND InvoiceId=@InvoiceId AND RefundStatus IN(N'REFUND_REQUIRED',N'REFUND_PENDING');
        IF @@ROWCOUNT<>1 THROW 51912,N'Reversal obligation changed during settlement.',1;
        COMMIT;
        SELECT @RefundId RefundId,@ExistingJournal JournalEntryId,@Amount Amount,
            @SettlementMode SettlementMode,CAST(0 AS bit) AlreadySettled;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK;
        THROW;
    END CATCH;
END;
