using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/marketplace/partner-settlements"),
 RequireFeature(FeatureKeys.PartnerSettlements), HasPermission(Permissions.Finance.SupplierOutstandingView)]
public sealed class MarketplacePartnerSettlementsController(
    IMarketplacePartnerSettlementService settlements, ICurrentUserService current) : ControllerBase
{
    private string Actor => current.Username ?? current.Email ?? "authenticated-user";

    [HttpGet]
    public Task<IReadOnlyCollection<MarketplacePartnerSettlementDto>> List(CancellationToken token)
        => settlements.ListAsync(token);

    [HttpPost("{settlementEntryId:guid}/review"), HasPermission(Permissions.Finance.PaymentCreate)]
    public Task<MarketplacePartnerSettlementDto> Review(Guid settlementEntryId,
        ReviewMarketplacePartnerSettlementInput input, CancellationToken token)
        => settlements.ReviewAsync(settlementEntryId, input, Actor, token);

    [HttpPost("{settlementEntryId:guid}/paid"), HasPermission(Permissions.Finance.PaymentCreate)]
    public Task<MarketplacePartnerSettlementDto> MarkPaid(Guid settlementEntryId,
        MarkMarketplacePartnerSettlementPaidInput input, CancellationToken token)
        => settlements.MarkPaidAsync(settlementEntryId, input, Actor, token);
}
