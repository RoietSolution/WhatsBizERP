using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Infrastructure.Storefront;

internal static class MarketplacePartnerSettlementWriter
{
    internal static async Task CreateDraftsAsync(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, Guid orderId, string actor, CancellationToken token)
    {
        var lines = await ReadPartnerLines(connection, transaction, tenantId, orderId, token);
        foreach (var line in lines.GroupBy(x => x.InvoiceItemId).Select(x => x.ToArray()))
        {
            var first = line[0];
            if (line.Sum(x => x.AllocatedQuantity) != first.ItemQuantity)
                throw new InvalidOperationException("Marketplace partner allocation quantities do not reconcile to the invoice line.");

            var sharedDiscount = Math.Max(0m, first.InvoiceDiscount - first.ItemDiscountTotal)
                + Math.Max(0m, first.PromotionDiscount);
            var basis = first.CalculationBasisSnapshot switch
            {
                "ITEM_GROSS" => first.LineTotal,
                "ITEM_TAXABLE" => first.LineTotal - first.TaxAmount,
                _ => first.LineTotal
            };
            var reason = line.Select(x => DetermineReviewReason(x, sharedDiscount)).FirstOrDefault(x => x is not null);
            IReadOnlyDictionary<Guid, SettlementAllocationAmount> amounts = new Dictionary<Guid, SettlementAllocationAmount>();
            if (reason is null)
            {
                var percent = first.CommissionValueSnapshot!.Value;
                amounts = MarketplacePartnerSettlementPolicy.Apportion(basis, percent,
                        line.Select(x => new SettlementAllocationShare(x.AllocationId, x.AllocatedQuantity)))
                    .ToDictionary(x => x.AllocationId);
            }

            foreach (var allocation in line.OrderBy(x => x.AllocationId))
            {
                var calculation = amounts.TryGetValue(allocation.AllocationId, out var saved)
                    ? saved : new SettlementAllocationAmount(allocation.AllocationId, 0, 0, 0);
                var status = reason is null ? "DRAFT" : "REVIEW_REQUIRED";
                var snapshot = JsonSerializer.Serialize(new
                {
                    source = "MARKETPLACE_INVOICE",
                    orderId,
                    invoiceNumber = allocation.InvoiceNumber,
                    invoiceItemId = allocation.InvoiceItemId,
                    productId = allocation.ProductId,
                    allocationId = allocation.AllocationId,
                    fulfilmentGroupId = allocation.FulfilmentGroupId,
                    allocatedQuantity = allocation.AllocatedQuantity,
                    itemQuantity = allocation.ItemQuantity,
                    unitPrice = allocation.UnitPrice,
                    itemDiscount = allocation.ItemDiscount,
                    itemDiscountTotal = allocation.ItemDiscountTotal,
                    lineTaxAmount = allocation.TaxAmount,
                    lineTotal = allocation.LineTotal,
                    invoiceDiscount = allocation.InvoiceDiscount,
                    promotionDiscount = allocation.PromotionDiscount,
                    deliveryCharge = allocation.DeliveryCharge,
                    agreementId = allocation.AgreementId,
                    agreementVersion = allocation.AgreementVersionSnapshot,
                    commissionType = allocation.CommissionTypeSnapshot,
                    commissionValue = allocation.CommissionValueSnapshot,
                    calculationBasis = allocation.CalculationBasisSnapshot,
                    discountResponsibility = allocation.DiscountResponsibilitySnapshot,
                    deliveryResponsibility = allocation.DeliveryResponsibilitySnapshot,
                    paymentFeeResponsibility = allocation.PaymentFeeResponsibilitySnapshot,
                    returnTreatment = allocation.ReturnTreatmentSnapshot,
                    calculatedBasisAmount = reason is null ? calculation.GrossAmount : (decimal?)null,
                    calculatedCommissionAmount = reason is null ? calculation.CommissionAmount : (decimal?)null,
                    calculatedSellerPayable = reason is null ? calculation.SellerPayable : (decimal?)null,
                    reviewReason = reason
                });
                await using var insert = new SqlCommand("""
                    INSERT commerce.PartnerSettlementEntries(OperatingTenantId,PartnerSellerId,OrderReference,OrderLineReference,
                        EntryType,GrossAmount,CommissionAmount,SellerPayable,AgreementId,AgreementVersion,IdempotencyKey,
                        MarketplaceOrderId,AllocationId,FulfilmentGroupId,SettlementStatus,CalculationBasis,
                        CalculationSnapshotJson,ReviewReason,CreatedBy)
                    VALUES(@tenant,@seller,@orderNumber,@lineId,'SALE',@gross,@commission,@payable,@agreement,@agreementVersion,
                        @key,@order,@allocation,@group,@status,@basis,@snapshot,@reviewReason,@actor);
                    """, connection, transaction);
                Add(insert, "@tenant", tenantId); Add(insert, "@seller", allocation.PartnerSellerId);
                Add(insert, "@orderNumber", allocation.InvoiceNumber); Add(insert, "@lineId", allocation.InvoiceItemId.ToString("D"));
                Add(insert, "@gross", calculation.GrossAmount); Add(insert, "@commission", calculation.CommissionAmount);
                Add(insert, "@payable", calculation.SellerPayable); Add(insert, "@agreement", allocation.AgreementId);
                Add(insert, "@agreementVersion", allocation.AgreementVersionSnapshot);
                Add(insert, "@key", $"MARKETPLACE-SALE:{orderId:N}:{allocation.AllocationId:N}");
                Add(insert, "@order", orderId); Add(insert, "@allocation", allocation.AllocationId);
                Add(insert, "@group", allocation.FulfilmentGroupId); Add(insert, "@status", status);
                Add(insert, "@basis", allocation.CalculationBasisSnapshot); Add(insert, "@snapshot", snapshot);
                Add(insert, "@reviewReason", reason); Add(insert, "@actor", actor);
                await insert.ExecuteNonQueryAsync(token);

                await using var audit = new SqlCommand("""
                    INSERT commerce.PartnerSettlementEvents(OperatingTenantId,SettlementEntryId,FromStatus,ToStatus,
                        Actor,Reason,SnapshotJson,IdempotencyKey)
                    SELECT @tenant,SettlementEntryId,NULL,@status,@actor,@reason,@snapshot,@eventKey
                    FROM commerce.PartnerSettlementEntries WHERE OperatingTenantId=@tenant AND IdempotencyKey=@key;
                    """, connection, transaction);
                Add(audit, "@tenant", tenantId); Add(audit, "@status", status); Add(audit, "@actor", actor);
                Add(audit, "@reason", reason ?? "Marketplace supplier payable draft created.");
                Add(audit, "@snapshot", snapshot); Add(audit, "@eventKey", $"SETTLEMENT-CREATED:{orderId:N}:{allocation.AllocationId:N}");
                Add(audit, "@key", $"MARKETPLACE-SALE:{orderId:N}:{allocation.AllocationId:N}");
                await audit.ExecuteNonQueryAsync(token);
            }
        }
    }

    private static string? DetermineReviewReason(PartnerSettlementSourceRow source, decimal sharedDiscount)
    {
        if (source.AgreementId is null || source.AgreementVersionSnapshot is null
            || source.CommissionTypeSnapshot is null || source.CommissionValueSnapshot is null
            || source.CalculationBasisSnapshot is null || source.DiscountResponsibilitySnapshot is null
            || source.DeliveryResponsibilitySnapshot is null || source.PaymentFeeResponsibilitySnapshot is null
            || source.ReturnTreatmentSnapshot is null)
            return "The order allocation has no complete, immutable commercial-agreement snapshot.";
        return MarketplacePartnerSettlementPolicy.CanCalculateAutomatically(source.CommissionTypeSnapshot,
            source.CommissionValueSnapshot.Value, source.CalculationBasisSnapshot, source.ItemDiscount,
            sharedDiscount, source.DeliveryCharge, source.PaymentFeeResponsibilitySnapshot, out var reason)
            ? null : reason;
    }

    private static async Task<IReadOnlyCollection<PartnerSettlementSourceRow>> ReadPartnerLines(SqlConnection connection,
        SqlTransaction transaction, Guid tenantId, Guid orderId, CancellationToken token)
    {
        var result = new List<PartnerSettlementSourceRow>();
        await using var command = new SqlCommand("""
            SELECT i.InvoiceNumber,si.InvoiceItemId,si.ProductId,si.Quantity,si.UnitPrice,si.DiscountAmount,si.TaxAmount,si.LineTotal,
              i.DiscountAmount,
              (SELECT SUM(ii.DiscountAmount) FROM sales.SalesInvoiceItems ii WHERE ii.InvoiceId=i.InvoiceId),
              i.PromotionDiscountAmount,i.DeliveryCharge,
              a.AllocationId,a.AllocatedQuantity,a.PartnerSellerId,a.AgreementId,gi.FulfilmentGroupId,
              a.AgreementVersionSnapshot,a.CommissionTypeSnapshot,a.CommissionValueSnapshot,a.CalculationBasisSnapshot,
              a.DiscountResponsibilitySnapshot,a.DeliveryResponsibilitySnapshot,a.PaymentFeeResponsibilitySnapshot,
              a.ReturnTreatmentSnapshot
            FROM commerce.MarketplaceOrderAllocations a
            JOIN sales.SalesInvoices i ON i.TenantId=a.TenantId AND i.InvoiceId=a.OrderId AND i.OrderType='MARKETPLACE'
            JOIN sales.SalesInvoiceItems si ON si.InvoiceId=a.OrderId AND si.InvoiceItemId=a.InvoiceItemId
            JOIN commerce.MarketplaceFulfilmentGroupItems gi ON gi.TenantId=a.TenantId AND gi.AllocationId=a.AllocationId
            WHERE a.TenantId=@tenant AND a.OrderId=@order AND a.SellerKind='PARTNER'
            ORDER BY si.InvoiceItemId,a.AllocationId;
            """, connection, transaction);
        Add(command, "@tenant", tenantId); Add(command, "@order", orderId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetDecimal(3),reader.GetDecimal(4),
                reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),
                reader.IsDBNull(9)?0:reader.GetDecimal(9),reader.GetDecimal(10),reader.GetDecimal(11),
                reader.GetGuid(12),reader.GetDecimal(13),reader.GetGuid(14),reader.IsDBNull(15)?null:reader.GetGuid(15),
                reader.GetGuid(16),reader.IsDBNull(17)?null:reader.GetInt32(17),reader.IsDBNull(18)?null:reader.GetString(18),
                reader.IsDBNull(19)?null:reader.GetDecimal(19),reader.IsDBNull(20)?null:reader.GetString(20),
                reader.IsDBNull(21)?null:reader.GetString(21),reader.IsDBNull(22)?null:reader.GetString(22),
                reader.IsDBNull(23)?null:reader.GetString(23),reader.IsDBNull(24)?null:reader.GetString(24)));
        return result;
    }

    private static void Add(SqlCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record PartnerSettlementSourceRow(string InvoiceNumber, Guid InvoiceItemId, Guid ProductId,
        decimal ItemQuantity, decimal UnitPrice, decimal ItemDiscount, decimal TaxAmount, decimal LineTotal,
        decimal InvoiceDiscount, decimal ItemDiscountTotal, decimal PromotionDiscount, decimal DeliveryCharge,
        Guid AllocationId, decimal AllocatedQuantity, Guid PartnerSellerId, Guid? AgreementId,
        Guid FulfilmentGroupId, int? AgreementVersionSnapshot, string? CommissionTypeSnapshot,
        decimal? CommissionValueSnapshot, string? CalculationBasisSnapshot, string? DiscountResponsibilitySnapshot,
        string? DeliveryResponsibilitySnapshot, string? PaymentFeeResponsibilitySnapshot, string? ReturnTreatmentSnapshot);
}
