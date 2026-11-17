using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class MarketplacePartnerSettlementService(
    IConfiguration configuration, ICurrentUserService currentUser, IFeatureService features) : IMarketplacePartnerSettlementService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");
    private Guid Tenant => currentUser.TenantId
        ?? throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("A tenant context is required.");

    public async Task<IReadOnlyCollection<MarketplacePartnerSettlementDto>> ListAsync(CancellationToken token)
    {
        var tenant = Tenant;
        await EnsureFeature(tenant, token);
        await using var connection = await Open(tenant, token);
        var rows = new List<SettlementListRow>();
        await using var command = new SqlCommand("""
            SELECT TOP(200) s.SettlementEntryId,s.MarketplaceOrderId,i.InvoiceNumber,i.Status,
              s.AllocationId,s.FulfilmentGroupId,g.GroupNumber,s.PartnerSellerId,ps.SellerName,
              a.InvoiceItemId,item.ProductId,p.ProductName,a.AllocatedQuantity,s.AgreementId,s.AgreementVersion,s.CalculationBasis,
              s.GrossAmount,s.CommissionAmount,s.SellerPayable,s.EntryType,s.SettlementStatus,s.AdjustmentDirection,
              s.ReviewReason,s.CalculationSnapshotJson,s.OccurredAt,s.CreatedBy,s.ApprovedAt,s.ApprovedBy,
              s.SettledAt,s.SettledBy,s.PaymentReference,a.WarehouseId,w.WarehouseName
            FROM commerce.PartnerSettlementEntries s
            JOIN commerce.MarketplaceOrderAllocations a ON a.TenantId=s.OperatingTenantId AND a.OrderId=s.MarketplaceOrderId AND a.AllocationId=s.AllocationId
            JOIN sales.SalesInvoices i ON i.TenantId=s.OperatingTenantId AND i.InvoiceId=s.MarketplaceOrderId
            JOIN commerce.MarketplaceFulfilmentGroups g ON g.TenantId=s.OperatingTenantId AND g.FulfilmentGroupId=s.FulfilmentGroupId
            JOIN commerce.PartnerSellers ps ON ps.OperatingTenantId=s.OperatingTenantId AND ps.PartnerSellerId=s.PartnerSellerId
            JOIN inventory.Warehouses w ON w.TenantId=a.TenantId AND w.WarehouseId=a.WarehouseId
            JOIN sales.SalesInvoiceItems item ON item.InvoiceId=a.OrderId AND item.InvoiceItemId=a.InvoiceItemId
            JOIN master.Products p ON p.TenantId=i.TenantId AND p.ProductId=item.ProductId
            WHERE s.OperatingTenantId=@tenant AND s.MarketplaceOrderId IS NOT NULL
            ORDER BY CASE s.SettlementStatus WHEN 'REVIEW_REQUIRED' THEN 0 WHEN 'DRAFT' THEN 1 WHEN 'APPROVED' THEN 2 WHEN 'PAID' THEN 3 ELSE 4 END,
              s.OccurredAt DESC,s.SettlementEntryId;
            """, connection);
        P(command, "@tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            rows.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetGuid(4),reader.GetGuid(5),
                reader.GetInt16(6),reader.GetGuid(7),reader.GetString(8),reader.GetGuid(9),reader.GetGuid(10),reader.GetString(11),
                reader.GetDecimal(12),reader.IsDBNull(13)?null:reader.GetGuid(13),reader.IsDBNull(14)?null:reader.GetInt32(14),
                S(reader,15),reader.GetDecimal(16),reader.GetDecimal(17),reader.GetDecimal(18),reader.GetString(19),
                reader.GetString(20),S(reader,21),S(reader,22),S(reader,23),reader.GetDateTimeOffset(24),reader.GetString(25),
                D(reader,26),S(reader,27),D(reader,28),S(reader,29),S(reader,30),reader.GetGuid(31),reader.GetString(32)));
        }
        await reader.DisposeAsync();
        var result = new List<MarketplacePartnerSettlementDto>(rows.Count);
        foreach(var row in rows)
            result.Add(row.ToDto(await ReadEvents(connection,tenant,row.SettlementEntryId,token)));
        return result;
    }

    public async Task<MarketplacePartnerSettlementDto> ReviewAsync(Guid settlementEntryId,
        ReviewMarketplacePartnerSettlementInput input, string actor, CancellationToken token)
    {
        var tenant = Tenant; await EnsureFeature(tenant, token);
        var reason = input.Reason?.Trim();
        if (settlementEntryId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new BusinessRuleException("A settlement entry and review reason are required.");
        await using var connection = await Open(tenant, token);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var row = await ReadLocked(connection,tx,tenant,settlementEntryId,token);
            var next = input.Approve ? "APPROVED" : "REJECTED";
            if (row.Status == next)
            {
                if (input.GrossAmount is not null && input.GrossAmount != row.Gross
                    || input.CommissionAmount is not null && input.CommissionAmount != row.Commission
                    || input.SellerPayable is not null && input.SellerPayable != row.Payable)
                    throw new BusinessRuleException("This settlement was already approved with different amounts.");
                await using var replay = new SqlCommand("SELECT COUNT(1) FROM commerce.PartnerSettlementEvents WHERE OperatingTenantId=@tenant AND SettlementEntryId=@id AND IdempotencyKey=@key AND Reason=@reason;",connection,tx);
                P(replay,"@tenant",tenant);P(replay,"@id",settlementEntryId);P(replay,"@key",$"REVIEW:{settlementEntryId:N}:{next}");P(replay,"@reason",reason);
                if(Convert.ToInt32(await replay.ExecuteScalarAsync(token),CultureInfo.InvariantCulture)==1)
                {
                    await tx.CommitAsync(token);
                    return (await ListAsync(token)).Single(x=>x.SettlementEntryId==settlementEntryId);
                }
                throw new BusinessRuleException("This settlement already has a different review decision.");
            }
            if (row.Status is not ("DRAFT" or "REVIEW_REQUIRED"))
                throw new BusinessRuleException("Only draft or review-required settlement entries can be reviewed.");
            decimal gross=row.Gross, commission=row.Commission, payable=row.Payable;
            string? snapshot=row.Snapshot;
            if (input.Approve)
            {
                await EnsureSettlementReadyForApproval(connection,tx,tenant,row,token);
                if (row.Status == "REVIEW_REQUIRED")
                {
                    if (input.GrossAmount is null || input.CommissionAmount is null || input.SellerPayable is null)
                        throw new BusinessRuleException("Enter the reviewed gross, commission and supplier payable amounts explicitly.");
                    gross=input.GrossAmount.Value; commission=input.CommissionAmount.Value; payable=input.SellerPayable.Value;
                    ValidateAmounts(gross,commission,payable);
                    snapshot=JsonSerializer.Serialize(new { original=ParseSnapshot(row.Snapshot), reviewer=actor,
                        reviewReason=reason, resolvedGrossAmount=gross, resolvedCommissionAmount=commission,
                        resolvedSellerPayable=payable, resolvedAt=DateTimeOffset.UtcNow });
                }
                else if (input.GrossAmount is not null || input.CommissionAmount is not null || input.SellerPayable is not null)
                    throw new BusinessRuleException("Automatically calculated draft amounts cannot be changed during approval.");
            }
            var eventKey=$"REVIEW:{settlementEntryId:N}:{next}";
            await InsertEvent(connection,tx,tenant,settlementEntryId,row.Status,next,actor,reason,null,snapshot,eventKey,token);
            await using var update = new SqlCommand("""
                UPDATE commerce.PartnerSettlementEntries SET SettlementStatus=@next,ReviewReason=@reviewReason,
                  GrossAmount=@gross,CommissionAmount=@commission,SellerPayable=@payable,CalculationSnapshotJson=@snapshot,
                  ApprovedAt=CASE WHEN @next='APPROVED' THEN SYSUTCDATETIME() ELSE ApprovedAt END,
                  ApprovedBy=CASE WHEN @next='APPROVED' THEN @actor ELSE ApprovedBy END,
                  RejectedAt=CASE WHEN @next='REJECTED' THEN SYSUTCDATETIME() ELSE RejectedAt END,
                  RejectedBy=CASE WHEN @next='REJECTED' THEN @actor ELSE RejectedBy END
                WHERE OperatingTenantId=@tenant AND SettlementEntryId=@id AND SettlementStatus=@current;
                IF @@ROWCOUNT<>1 THROW 91100,N'Settlement review changed concurrently.',1;
                """,connection,tx);
            P(update,"@next",next);P(update,"@reviewReason",reason);P(update,"@gross",gross);P(update,"@commission",commission);
            P(update,"@payable",payable);P(update,"@snapshot",(object?)snapshot??DBNull.Value);P(update,"@actor",actor);
            P(update,"@tenant",tenant);P(update,"@id",settlementEntryId);P(update,"@current",row.Status);
            await update.ExecuteNonQueryAsync(token); await tx.CommitAsync(token);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return (await ListAsync(token)).Single(x=>x.SettlementEntryId==settlementEntryId);
    }

    public async Task<MarketplacePartnerSettlementDto> MarkPaidAsync(Guid settlementEntryId,
        MarkMarketplacePartnerSettlementPaidInput input, string actor, CancellationToken token)
    {
        var tenant=Tenant; await EnsureFeature(tenant,token);
        var reference=input.PaymentReference?.Trim();
        if(settlementEntryId==Guid.Empty||string.IsNullOrWhiteSpace(reference)||reference.Length>160||input.SettledAt> DateTimeOffset.UtcNow.AddMinutes(5))
            throw new BusinessRuleException("A valid independently confirmed payment reference and time are required.");
        await using var connection=await Open(tenant,token);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var row=await ReadLocked(connection,tx,tenant,settlementEntryId,token);
            if(row.Status=="PAID")
            {
                if(row.PaymentReference==reference){await tx.CommitAsync(token);return (await ListAsync(token)).Single(x=>x.SettlementEntryId==settlementEntryId);}
                throw new BusinessRuleException("This settlement was already recorded as paid with another reference.");
            }
            if(row.Status!="APPROVED") throw new BusinessRuleException("Approve the settlement before recording payment confirmation.");
            await EnsureSettlementReadyForApproval(connection,tx,tenant,row,token);
            var snapshot=JsonSerializer.Serialize(new { row.Snapshot, paymentReference=reference, input.SettledAt, actor });
            await InsertEvent(connection,tx,tenant,settlementEntryId,"APPROVED","PAID",actor,"Independent supplier payment confirmation recorded.",reference,snapshot,$"PAID:{settlementEntryId:N}",token);
            await using var update=new SqlCommand("UPDATE commerce.PartnerSettlementEntries SET SettlementStatus='PAID',SettledAt=@settledAt,SettledBy=@actor,PaymentReference=@reference WHERE OperatingTenantId=@tenant AND SettlementEntryId=@id AND SettlementStatus='APPROVED';IF @@ROWCOUNT<>1 THROW 91101,N'Settlement payment confirmation changed concurrently.',1;",connection,tx);
            P(update,"@settledAt",input.SettledAt);P(update,"@actor",actor);P(update,"@reference",reference);P(update,"@tenant",tenant);P(update,"@id",settlementEntryId);
            await update.ExecuteNonQueryAsync(token);await tx.CommitAsync(token);
        }
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
        return (await ListAsync(token)).Single(x=>x.SettlementEntryId==settlementEntryId);
    }

    private static async Task EnsureSettlementReadyForApproval(SqlConnection c,SqlTransaction tx,Guid tenant,LockedSettlement row,CancellationToken token)
    {
        await using var q=new SqlCommand("""
            SELECT CASE WHEN
              (@entryType='SALE' AND g.FulfilmentStatus='DELIVERED'
                AND (EXISTS(SELECT 1 FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId AND p.Status IN('PAID','REFUNDED') AND p.Amount=i.GrandTotal)
                  OR EXISTS(SELECT 1 FROM commerce.MarketplaceCodCollections cod WHERE cod.TenantId=i.TenantId AND cod.OrderId=i.InvoiceId AND cod.CollectionStatus IN('COLLECTED','REFUND_DUE','REFUNDED') AND cod.CollectedAmount=cod.ExpectedAmount AND cod.ExpectedAmount=i.GrandTotal)))
              OR (@entryType='ADJUSTMENT' AND g.FulfilmentStatus='CANCELLED'
                AND EXISTS(SELECT 1 FROM commerce.MarketplaceFulfilmentRefundObligations r WHERE r.TenantId=g.TenantId AND r.OrderId=g.OrderId
                  AND r.FulfilmentGroupId=g.FulfilmentGroupId AND r.RefundStatus IN('NO_REFUND_REQUIRED','REFUNDED')))
              THEN 1 ELSE 0 END
            FROM sales.SalesInvoices i JOIN commerce.MarketplaceFulfilmentGroups g ON g.TenantId=i.TenantId AND g.OrderId=i.InvoiceId
            WHERE i.TenantId=@tenant AND i.InvoiceId=@order AND g.FulfilmentGroupId=@group AND i.OrderType='MARKETPLACE';
            """,c,tx);P(q,"@tenant",tenant);P(q,"@order",row.OrderId);P(q,"@group",row.GroupId);P(q,"@entryType",row.EntryType);
        if(await q.ExecuteScalarAsync(token) is not int ready||ready!=1)
            throw new BusinessRuleException(row.EntryType == "ADJUSTMENT"
                ? "A cancellation adjustment can be approved only after the group is cancelled and its customer refund obligation is resolved."
                : "A supplier payable can be approved only after the parent order is fully collected and this delivery group is delivered.");
    }

    private static async Task<LockedSettlement> ReadLocked(SqlConnection c,SqlTransaction tx,Guid tenant,Guid id,CancellationToken token)
    {
        await using var q=new SqlCommand("SELECT MarketplaceOrderId,FulfilmentGroupId,SettlementStatus,GrossAmount,CommissionAmount,SellerPayable,CalculationSnapshotJson,PaymentReference,EntryType FROM commerce.PartnerSettlementEntries WITH(UPDLOCK,HOLDLOCK) WHERE OperatingTenantId=@tenant AND SettlementEntryId=@id AND MarketplaceOrderId IS NOT NULL;",c,tx);P(q,"@tenant",tenant);P(q,"@id",id);
        await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new EntityNotFoundException("Marketplace supplier settlement was not found.");
        return new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),S(r,6),S(r,7),r.GetString(8));
    }

    private static async Task<IReadOnlyCollection<MarketplacePartnerSettlementEventDto>> ReadEvents(SqlConnection c,Guid tenant,Guid id,CancellationToken token)
    {
        var result=new List<MarketplacePartnerSettlementEventDto>();
        await using var q=new SqlCommand("SELECT FromStatus,ToStatus,Actor,Reason,PaymentReference,OccurredAt FROM commerce.PartnerSettlementEvents WHERE OperatingTenantId=@tenant AND SettlementEntryId=@id ORDER BY OccurredAt,SettlementEventId;",c);P(q,"@tenant",tenant);P(q,"@id",id);
        await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))result.Add(new(S(r,0)??string.Empty,r.GetString(1),r.GetString(2),S(r,3),S(r,4),r.GetDateTimeOffset(5)));
        return result;
    }

    private static async Task InsertEvent(SqlConnection c,SqlTransaction tx,Guid tenant,Guid id,string from,string to,string actor,string reason,string? reference,string? snapshot,string key,CancellationToken token)
    {
        await using var q=new SqlCommand("INSERT commerce.PartnerSettlementEvents(OperatingTenantId,SettlementEntryId,FromStatus,ToStatus,Actor,Reason,PaymentReference,SnapshotJson,IdempotencyKey)VALUES(@tenant,@id,@from,@to,@actor,@reason,@reference,@snapshot,@key);",c,tx);
        P(q,"@tenant",tenant);P(q,"@id",id);P(q,"@from",from);P(q,"@to",to);P(q,"@actor",actor);P(q,"@reason",reason);P(q,"@reference",reference);P(q,"@snapshot",snapshot);P(q,"@key",key);await q.ExecuteNonQueryAsync(token);
    }

    private async Task<SqlConnection> Open(Guid tenant,CancellationToken token)
    {
        var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var context=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;",c);P(context,"@tenant",tenant);await context.ExecuteNonQueryAsync(token);return c;
    }
    private async Task EnsureFeature(Guid tenant,CancellationToken token)
    {if(!await features.IsEnabledAsync(tenant,FeatureKeys.PartnerSettlements,token))throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("Partner supplier settlement management is not enabled for this retailer.");}
    private static void ValidateAmounts(decimal gross,decimal commission,decimal payable)
    {if(gross<0||commission<0||payable<0||commission>gross||decimal.Round(gross-commission,2,MidpointRounding.AwayFromZero)!=payable)throw new BusinessRuleException("Gross, commission and seller payable must reconcile without negative amounts.");}
    private static object ParseSnapshot(string? json)=>string.IsNullOrWhiteSpace(json)?new{}:JsonDocument.Parse(json).RootElement.Clone();
    private static string? S(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static DateTimeOffset? D(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDateTimeOffset(i);
    private static void P(SqlCommand q,string n,object? v)=>q.Parameters.AddWithValue(n,v??DBNull.Value);
    private sealed record SettlementListRow(Guid SettlementEntryId,Guid OrderId,string OrderNumber,string InvoiceStatus,
        Guid AllocationId,Guid GroupId,short GroupNumber,Guid PartnerSellerId,string SellerName,Guid InvoiceItemId,
        Guid ProductId,string ProductName,decimal Quantity,Guid? AgreementId,int? AgreementVersion,string? Basis,
        decimal Gross,decimal Commission,decimal Payable,string EntryType,string Status,string? Direction,
        string? ReviewReason,string? Snapshot,DateTimeOffset CreatedAt,string CreatedBy,DateTimeOffset? ApprovedAt,
        string? ApprovedBy,DateTimeOffset? SettledAt,string? SettledBy,string? PaymentReference,
        Guid WarehouseId,string WarehouseName)
    {
        public MarketplacePartnerSettlementDto ToDto(IReadOnlyCollection<MarketplacePartnerSettlementEventDto> events)
            =>new(SettlementEntryId,OrderId,OrderNumber,InvoiceStatus,AllocationId,GroupId,GroupNumber,PartnerSellerId,SellerName,
                ProductId,ProductName,Quantity,AgreementId,AgreementVersion,Basis,Gross,Commission,Payable,EntryType,Status,
                Direction,ReviewReason,Snapshot,CreatedAt,CreatedBy,ApprovedAt,ApprovedBy,SettledAt,SettledBy,PaymentReference,
                WarehouseId,WarehouseName,events);
    }
    private sealed record LockedSettlement(Guid OrderId,Guid GroupId,string Status,decimal Gross,decimal Commission,decimal Payable,string? Snapshot,string? PaymentReference,string EntryType);
}
