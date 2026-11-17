using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;

[ApiController,Route("api/deliveries"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class DeliveriesController(IDeliveryService service,ICommercePaymentService payments,ICurrentUserService current) : ControllerBase
{
 private Guid Tenant=>current.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");private Guid UserId=>current.UserId??throw new UnauthorizedAccessException("A user identity is required.");private string Actor=>current.Username??UserId.ToString();private bool Manage=>current.Permissions.Contains(Permissions.Delivery.Manage);
 [HttpGet,HasPermission(Permissions.Delivery.Manage)] public Task<DeliveryDashboardDto> List([FromQuery]string? status,[FromQuery]Guid? agentId,[FromQuery]string? paymentType,[FromQuery]string? search,[FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,CancellationToken t)=>service.Deliveries(Tenant,new(status,agentId,paymentType,search,from,to),t);
 [HttpGet("{id:guid}"),HasPermission(Permissions.Delivery.View)] public Task<DeliveryDto> Get(Guid id,CancellationToken t)=>service.GetDelivery(Tenant,id,UserId,Manage,t);
 [HttpPost("orders/{orderId:guid}/ready"),HasPermission(Permissions.Delivery.Manage)] public Task<DeliveryDto> Ready(Guid orderId,ReadyDeliveryInput input,CancellationToken t)=>service.Ready(Tenant,orderId,input,UserId,Actor,t);
 [HttpPost("{id:guid}/assign"),HasPermission(Permissions.Delivery.Assign)] public Task<DeliveryDto> Assign(Guid id,AssignDeliveryInput input,CancellationToken t)=>service.Assign(Tenant,id,input,UserId,Actor,t);
 [HttpPost("{id:guid}/unassign"),HasPermission(Permissions.Delivery.Assign)] public Task<DeliveryDto> Unassign(Guid id,CancellationToken t)=>service.Assign(Tenant,id,new(null,"Manually unassigned"),UserId,Actor,t);
 [HttpPost("{id:guid}/picked-up"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Picked(Guid id,CancellationToken t)=>service.PickedUp(Tenant,id,UserId,Actor,Manage,t);
 [HttpPost("{id:guid}/out-for-delivery"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Out(Guid id,CancellationToken t)=>service.OutForDelivery(Tenant,id,UserId,Actor,Manage,t);
 [HttpPost("{id:guid}/request-otp"),HasPermission(Permissions.Delivery.Confirm)] public async Task<IActionResult> Otp(Guid id,CancellationToken t){await service.RequestOtp(Tenant,id,UserId,Actor,Manage,t);return NoContent();}
 [HttpPost("{id:guid}/confirm-delivery"),HasPermission(Permissions.Delivery.Confirm)] public Task<DeliveryDto> Confirm(Guid id,ConfirmOtpInput input,CancellationToken t)=>service.ConfirmOtp(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/confirm-delivery-without-otp"),HasPermission(Permissions.Delivery.Confirm)] public Task<DeliveryDto> ConfirmWithoutOtp(Guid id,ConfirmDeliveryWithoutOtpInput input,CancellationToken t)=>service.ConfirmWithoutOtp(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/cod-collected"),HasPermission(Permissions.Delivery.RecordCod)] public Task<DeliveryDto> Cod(Guid id,CodCollectionInput input,CancellationToken t)=>service.RecordCod(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/cod-collected"),HasPermission(Permissions.Delivery.RecordCod)]
 public Task<MarketplaceCodCollectionResult> MarketplaceCod(Guid orderId,Guid groupId,CodCollectionInput input,[FromHeader(Name="Idempotency-Key")] string idempotencyKey,CancellationToken t)
     =>payments.CollectMarketplaceCodAsync(orderId,new(groupId,input.Amount,input.PaymentMethod,input.Reference,idempotencyKey),Actor,t);
 [HttpPost("marketplace-orders/{orderId:guid}/cod-collection/assign"),HasPermission(Permissions.Delivery.Manage)]
 public Task<MarketplaceCodCollectionResult> AssignMarketplaceCod(Guid orderId,MarketplaceCodAssignmentInput input,CancellationToken t)
     =>payments.AssignMarketplaceCodCollectionAsync(Tenant,orderId,input,UserId,Actor,t);
 [HttpGet("marketplace-orders/{orderId:guid}/fulfilment-groups"),HasPermission(Permissions.Delivery.View)]
 public Task<IReadOnlyCollection<MarketplaceFulfilmentGroupDto>> MarketplaceGroups(Guid orderId,CancellationToken t)
     =>service.MarketplaceGroups(Tenant,orderId,UserId,Manage,t);
 [HttpGet("marketplace-orders/assigned"),HasPermission(Permissions.Delivery.View)]
 public Task<IReadOnlyCollection<MarketplaceDeliveryOrderDto>> MarketplaceAssignedOrders(CancellationToken t)
     =>service.MarketplaceDeliveryOrders(Tenant,UserId,Manage,t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/request-otp"),HasPermission(Permissions.Delivery.Confirm)]
 public async Task<IActionResult> RequestMarketplaceGroupOtp(Guid orderId,Guid groupId,CancellationToken t){await service.RequestMarketplaceGroupOtp(Tenant,orderId,groupId,UserId,Actor,Manage,t);return NoContent();}
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/assign"),HasPermission(Permissions.Delivery.Assign)]
 public Task<MarketplaceFulfilmentGroupDto> AssignMarketplaceGroup(Guid orderId,Guid groupId,MarketplaceGroupAssignmentInput input,CancellationToken t)
     =>service.AssignMarketplaceGroup(Tenant,orderId,groupId,input,UserId,Actor,Manage,current.Permissions.Contains(Permissions.Delivery.Assign),t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/status"),HasPermission(Permissions.Delivery.UpdateStatus)]
 public Task<MarketplaceFulfilmentGroupDto> MarketplaceGroupStatus(Guid orderId,Guid groupId,MarketplaceGroupTransitionInput input,CancellationToken t)
     =>service.TransitionMarketplaceGroup(Tenant,orderId,groupId,input,UserId,Actor,Manage,current.Permissions.Contains(Permissions.Delivery.Confirm),current.Permissions.Contains(Permissions.Delivery.OverrideOtp),t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/confirm-delivery"),HasPermission(Permissions.Delivery.Confirm)]
 public Task<MarketplaceFulfilmentGroupDto> ConfirmMarketplaceGroup(Guid orderId,Guid groupId,ConfirmDeliveryWithoutOtpInput input,CancellationToken t)
     =>service.TransitionMarketplaceGroup(Tenant,orderId,groupId,new("DELIVERED",input.Reason),UserId,Actor,Manage,true,false,t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/confirm-delivery-otp"),HasPermission(Permissions.Delivery.Confirm)]
 public Task<MarketplaceFulfilmentGroupDto> ConfirmMarketplaceGroupOtp(Guid orderId,Guid groupId,ConfirmOtpInput input,CancellationToken t)
     =>service.TransitionMarketplaceGroup(Tenant,orderId,groupId,new("DELIVERED",Otp:input.Otp),UserId,Actor,Manage,true,false,t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/cancel"),HasPermission(Permissions.Delivery.Manage)]
 public Task<MarketplaceGroupCancellationResult> CancelMarketplaceGroup(Guid orderId,Guid groupId,MarketplaceGroupCancellationInput input,CancellationToken t)
     =>service.CancelMarketplaceGroup(Tenant,orderId,groupId,input,UserId,Actor,Manage,t);
 [HttpPost("marketplace-orders/{orderId:guid}/fulfilment-groups/{groupId:guid}/override-delivered"),HasPermission(Permissions.Delivery.OverrideOtp)]
 public Task<MarketplaceFulfilmentGroupDto> OverrideMarketplaceGroup(Guid orderId,Guid groupId,OverrideDeliveryInput input,CancellationToken t)
     =>service.TransitionMarketplaceGroup(Tenant,orderId,groupId,new("DELIVERED",input.Reason),UserId,Actor,Manage,true,true,t);
 [HttpPost("{id:guid}/failed"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Failed(Guid id,FailDeliveryInput input,CancellationToken t)=>service.Fail(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/reschedule"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Reschedule(Guid id,RescheduleDeliveryInput input,CancellationToken t)=>service.Reschedule(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/override-delivered"),HasPermission(Permissions.Delivery.OverrideOtp)] public Task<DeliveryDto> Override(Guid id,OverrideDeliveryInput input,CancellationToken t)=>service.OverrideDelivered(Tenant,id,input,UserId,Actor,t);
}
[ApiController,Route("api/delivery"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class MyDeliveryController(IDeliveryService service,ICurrentUserService current) : ControllerBase
{
 [HttpGet("my-deliveries"),HasPermission(Permissions.Delivery.View)] public Task<IReadOnlyCollection<DeliveryDto>> Mine([FromQuery]string? status,CancellationToken t)=>service.MyDeliveries(current.TenantId??throw new UnauthorizedAccessException("A tenant context is required."),current.UserId??throw new UnauthorizedAccessException("A user identity is required."),status,t);
 [HttpGet("marketplace-fulfilment-requirements"),HasPermission(Permissions.Delivery.View)]
 public async Task<DeliveryVerificationRequirementsDto> MarketplaceRequirements(CancellationToken t){var settings=await service.Settings(current.TenantId??throw new UnauthorizedAccessException("A tenant context is required."),t);return new(settings.RequireDeliveryOtp,settings.RequireCodConfirmation);}
}
[ApiController,Route("api/delivery-agents"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class DeliveryAgentsController(IDeliveryService service,ICurrentUserService current) : ControllerBase
{
 private Guid Tenant=>current.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");private string Actor=>current.Username??"unknown";
 [HttpGet,HasPermission(Permissions.Delivery.AgentManage)] public Task<IReadOnlyCollection<DeliveryAgentDto>> List(CancellationToken t)=>service.Agents(Tenant,t);
 [HttpGet("eligible-users"),HasPermission(Permissions.Delivery.AgentManage)] public Task<IReadOnlyCollection<DeliveryUserDto>> Users(CancellationToken t)=>service.EligibleUsers(Tenant,t);
 [HttpPost,HasPermission(Permissions.Delivery.AgentManage)] public Task<DeliveryAgentDto> Create(SaveDeliveryAgentInput input,CancellationToken t)=>service.SaveAgent(Tenant,null,input,Actor,t);
 [HttpPut("{id:guid}"),HasPermission(Permissions.Delivery.AgentManage)] public Task<DeliveryAgentDto> Update(Guid id,SaveDeliveryAgentInput input,CancellationToken t)=>service.SaveAgent(Tenant,id,input,Actor,t);
 [HttpPost("{id:guid}/set-default"),HasPermission(Permissions.Delivery.Settings)] public async Task<IActionResult> Default(Guid id,CancellationToken t){await service.SetDefault(Tenant,id,Actor,t);return NoContent();}
 [HttpPost("remove-default"),HasPermission(Permissions.Delivery.Settings)] public async Task<IActionResult> RemoveDefault(CancellationToken t){await service.SetDefault(Tenant,null,Actor,t);return NoContent();}
}
[ApiController,Route("api/delivery-settings"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class DeliverySettingsController(IDeliveryService service,ICurrentUserService current) : ControllerBase
{
 private Guid Tenant=>current.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");
 [HttpGet,HasPermission(Permissions.Delivery.Settings)] public Task<DeliverySettingsDto> Get(CancellationToken t)=>service.Settings(Tenant,t);
 [HttpPut,HasPermission(Permissions.Delivery.Settings)] public Task<DeliverySettingsDto> Save(SaveDeliverySettingsInput input,CancellationToken t)=>service.SaveSettings(Tenant,input,current.Username??"unknown",t);
}
