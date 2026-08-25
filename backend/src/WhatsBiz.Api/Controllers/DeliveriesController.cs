using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;

[ApiController,Route("api/deliveries"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class DeliveriesController(IDeliveryService service,ICurrentUserService current) : ControllerBase
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
 [HttpPost("{id:guid}/cod-collected"),HasPermission(Permissions.Delivery.RecordCod)] public Task<DeliveryDto> Cod(Guid id,CodCollectionInput input,CancellationToken t)=>service.RecordCod(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/failed"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Failed(Guid id,FailDeliveryInput input,CancellationToken t)=>service.Fail(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/reschedule"),HasPermission(Permissions.Delivery.UpdateStatus)] public Task<DeliveryDto> Reschedule(Guid id,RescheduleDeliveryInput input,CancellationToken t)=>service.Reschedule(Tenant,id,UserId,input,Actor,Manage,t);
 [HttpPost("{id:guid}/override-delivered"),HasPermission(Permissions.Delivery.OverrideOtp)] public Task<DeliveryDto> Override(Guid id,OverrideDeliveryInput input,CancellationToken t)=>service.OverrideDelivered(Tenant,id,input,UserId,Actor,t);
}
[ApiController,Route("api/delivery"),RequireFeature(FeatureKeys.DeliveryManagement)]
public sealed class MyDeliveryController(IDeliveryService service,ICurrentUserService current) : ControllerBase
{ [HttpGet("my-deliveries"),HasPermission(Permissions.Delivery.View)] public Task<IReadOnlyCollection<DeliveryDto>> Mine([FromQuery]string? status,CancellationToken t)=>service.MyDeliveries(current.TenantId??throw new UnauthorizedAccessException("A tenant context is required."),current.UserId??throw new UnauthorizedAccessException("A user identity is required."),status,t); }
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
