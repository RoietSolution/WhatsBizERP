using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Referrals;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/customer-referrals"), RequireFeature(FeatureKeys.CustomerReferralRewards)]
public sealed class CustomerReferralsController(ICustomerReferralService referrals, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("configuration"), HasPermission(Permissions.Customer.RewardsView)] public Task<ReferralConfigurationDto> Configuration(CancellationToken token)=>referrals.GetConfigurationAsync(TenantId(),token);
    [HttpPut("configuration"), HasPermission(Permissions.Customer.RewardsManage)] public Task<ReferralConfigurationDto> Configuration(ReferralConfigurationInput input,CancellationToken token)=>referrals.SaveConfigurationAsync(TenantId(),input,currentUser.Username,token);
    [HttpGet("customers/{customerId:guid}/code"), HasPermission(Permissions.Customer.RewardsView)] public Task<ReferralCodeDto> Code(Guid customerId,CancellationToken token)=>referrals.GetOrCreateCodeAsync(TenantId(),customerId,currentUser.Username,token);
    [HttpPut("customers/{customerId:guid}/code/active"), HasPermission(Permissions.Customer.RewardsManage)] public async Task<IActionResult> CodeActive(Guid customerId,SetReferralCodeActiveInput input,CancellationToken token){await referrals.SetCodeActiveAsync(TenantId(),customerId,input.IsActive,currentUser.Username,token);return NoContent();}
    [AllowAnonymous, HttpGet("resolve/{code}")] public async Task<ActionResult<ReferralResolutionDto>> Resolve(string code,CancellationToken token){var result=await referrals.ResolveCodeAsync(code,token);return result is null?NotFound():Ok(result);}
    [HttpPost("capture"), HasPermission(Permissions.Customer.RewardsManage)] public Task<ReferralDto> Capture(ReferralCaptureInput input,CancellationToken token)=>referrals.CaptureAsync(TenantId(),input,currentUser.Username,token);
    [HttpPost("{referralId:guid}/approve"), HasPermission(Permissions.Customer.RewardsManage)] public async Task<IActionResult> Approve(Guid referralId,CancellationToken token){await referrals.ApproveAsync(TenantId(),referralId,currentUser.Username,token);return NoContent();}
    [HttpGet("history"), HasPermission(Permissions.Customer.RewardsView)] public Task<IReadOnlyCollection<ReferralDto>> History([FromQuery]Guid? customerId,[FromQuery]int take=100,CancellationToken token=default)=>referrals.GetHistoryAsync(TenantId(),customerId,take,token);
    [HttpGet("metrics"), HasPermission(Permissions.Customer.RewardsView)] public Task<ReferralMetricsDto> Metrics(CancellationToken token)=>referrals.GetMetricsAsync(TenantId(),token);
    [HttpPost("adjustments"), HasPermission(Permissions.Customer.RewardsManage)] public async Task<IActionResult> Adjust(RewardAdjustmentInput input,CancellationToken token){await referrals.AdjustAsync(TenantId(),input,currentUser.Username,token);return NoContent();}
    [HttpPost("expire"), HasPermission(Permissions.Customer.RewardsManage)] public Task<int> Expire([FromQuery]int batchSize=1000,CancellationToken token=default)=>referrals.ExpireAsync(TenantId(),batchSize,currentUser.Username,token);
    private Guid TenantId()=>currentUser.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");
}
public sealed record SetReferralCodeActiveInput(bool IsActive);
