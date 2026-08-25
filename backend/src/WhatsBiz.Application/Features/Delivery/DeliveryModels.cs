using MediatR;

namespace WhatsBiz.Application.Features.Delivery;

public static class DeliveryStatuses
{
    public const string Unassigned="UNASSIGNED", Assigned="ASSIGNED", Ready="READY_FOR_PICKUP", PickedUp="PICKED_UP", OutForDelivery="OUT_FOR_DELIVERY", Delivered="DELIVERED", Failed="DELIVERY_FAILED", Rescheduled="RESCHEDULED", Cancelled="CANCELLED", ReturnRequested="RETURN_REQUESTED", Returned="RETURNED";
}
public static class DeliveryTemplateKeys { public const string Assigned="DELIVERY_ASSIGNED", OutForDelivery="OUT_FOR_DELIVERY", Otp="DELIVERY_OTP", Delivered="ORDER_DELIVERED", Failed="DELIVERY_FAILED", Rescheduled="DELIVERY_RESCHEDULED"; }
public static class DeliveryFailureReasons { public static readonly IReadOnlySet<string> All=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Customer unavailable","Customer refused","Incorrect address","Unable to contact","Customer requested reschedule","Payment issue","Address inaccessible","Other"}; }
public sealed record DeliveryAssignmentDecision(Guid? AgentId,string? Source);
public static class DeliveryAssignmentPolicy
{
 public static DeliveryAssignmentDecision Resolve(Guid? explicitAgentId,bool automaticAssignmentEnabled,Guid? defaultAgentId,bool defaultAgentActive,bool defaultAgentAvailable)
     => explicitAgentId is not null?new(explicitAgentId,"MANUAL"):automaticAssignmentEnabled&&defaultAgentId is not null&&defaultAgentActive&&defaultAgentAvailable?new(defaultAgentId,"DEFAULT"):new(null,null);
}

public sealed record DeliveryAgentDto(Guid DeliveryAgentId,Guid UserId,string DisplayName,string Mobile,bool IsActive,bool IsAvailable,bool IsDefault,int ActiveDeliveries,int DeliveredToday);
public sealed record DeliveryUserDto(Guid UserId,string UserName,string? Email,string? Mobile);
public sealed record SaveDeliveryAgentInput(Guid UserId,string DisplayName,string Mobile,bool IsActive=true,bool IsAvailable=true);
public sealed record DeliverySettingsDto(bool DeliveryEnabled,bool AutomaticAssignmentEnabled,Guid? DefaultDeliveryAgentId,bool DefaultAgentValid,string? Warning,bool RequireDeliveryOtp,int DeliveryOtpExpiryMinutes,int MaxOtpAttempts,int OtpResendCooldownSeconds,int MaxOtpResends,bool NotifyOnAssigned,bool NotifyOnOutForDelivery,bool NotifyOnDelivered,bool NotifyOnDeliveryFailed,bool RequireCodConfirmation,string? AssignedTemplateName,string? OutForDeliveryTemplateName,string? OtpTemplateName,string? DeliveredTemplateName,string? FailedTemplateName,string? RescheduledTemplateName);
public sealed record SaveDeliverySettingsInput(bool DeliveryEnabled,bool AutomaticAssignmentEnabled,Guid? DefaultDeliveryAgentId,bool RequireDeliveryOtp,int DeliveryOtpExpiryMinutes,int MaxOtpAttempts,int OtpResendCooldownSeconds,int MaxOtpResends,bool NotifyOnAssigned,bool NotifyOnOutForDelivery,bool NotifyOnDelivered,bool NotifyOnDeliveryFailed,bool RequireCodConfirmation,string? AssignedTemplateName,string? OutForDeliveryTemplateName,string? OtpTemplateName,string? DeliveredTemplateName,string? FailedTemplateName,string? RescheduledTemplateName);
public sealed record DeliveryEventDto(long EventId,string EventType,string? PreviousStatus,string? NewStatus,Guid? PreviousDeliveryAgentId,Guid? DeliveryAgentId,Guid? ActorUserId,string? Notes,string? Metadata,DateTimeOffset CreatedAt);
public sealed record DeliveryDto(Guid DeliveryId,Guid OrderId,string OrderNumber,DateTimeOffset OrderDate,string Status,Guid? DeliveryAgentId,string? DeliveryAgentName,string? AssignmentSource,string CustomerName,string? CustomerMobile,string Address,string? Notes,decimal Amount,bool CodRequired,decimal CodAmount,bool CodCollected,string? CodPaymentMethod,DateTimeOffset? ScheduledDate,string? TimeWindow,string SourceChannel,bool OtpVerified,DateTimeOffset UpdatedAt,string NavigationUrl,IReadOnlyCollection<DeliveryEventDto>? History=null);
public sealed record DeliveryDashboardDto(int Unassigned,int Assigned,int Ready,int OutForDelivery,int DeliveredToday,int Failed,int CodPending,int Rescheduled,IReadOnlyCollection<DeliveryDto> Deliveries);
public sealed record DeliveryQuery(string? Status=null,Guid? AgentId=null,string? PaymentType=null,string? Search=null,DateTimeOffset? From=null,DateTimeOffset? To=null);
public sealed record ReadyDeliveryInput(Guid? DeliveryAgentId=null,string? DeliveryAddress=null,string? DeliveryNotes=null,bool? CodRequired=null);
public sealed record AssignDeliveryInput(Guid? DeliveryAgentId,string? Reason=null);
public sealed record FailDeliveryInput(string Reason,string? Details=null);
public sealed record RescheduleDeliveryInput(DateTimeOffset NewDate,string? TimeWindow,string Reason,Guid? DeliveryAgentId=null);
public sealed record ConfirmOtpInput(string Otp);
public sealed record CodCollectionInput(string PaymentMethod,decimal Amount,string? Reference=null);
public sealed record OverrideDeliveryInput(string Reason,bool CodException=false);
public sealed record OrderDeliveryCompletedEvent(Guid TenantId,Guid OrderId,Guid DeliveryId,Guid? CustomerId,string Actor) : INotification;

public interface IDeliveryService
{
 Task<IReadOnlyCollection<DeliveryAgentDto>> Agents(Guid tenantId,CancellationToken token); Task<IReadOnlyCollection<DeliveryUserDto>> EligibleUsers(Guid tenantId,CancellationToken token); Task<DeliveryAgentDto> SaveAgent(Guid tenantId,Guid? id,SaveDeliveryAgentInput input,string actor,CancellationToken token); Task SetDefault(Guid tenantId,Guid? agentId,string actor,CancellationToken token);
 Task<DeliverySettingsDto> Settings(Guid tenantId,CancellationToken token); Task<DeliverySettingsDto> SaveSettings(Guid tenantId,SaveDeliverySettingsInput input,string actor,CancellationToken token);
 Task<DeliveryDashboardDto> Deliveries(Guid tenantId,DeliveryQuery query,CancellationToken token); Task<IReadOnlyCollection<DeliveryDto>> MyDeliveries(Guid tenantId,Guid userId,string? status,CancellationToken token); Task<DeliveryDto> GetDelivery(Guid tenantId,Guid deliveryId,Guid? actingUser,bool manage,CancellationToken token);
 Task<DeliveryDto> Ready(Guid tenantId,Guid orderId,ReadyDeliveryInput input,Guid actorId,string actor,CancellationToken token); Task<DeliveryDto> Assign(Guid tenantId,Guid deliveryId,AssignDeliveryInput input,Guid actorId,string actor,CancellationToken token);
 Task<DeliveryDto> PickedUp(Guid tenantId,Guid deliveryId,Guid userId,string actor,bool manage,CancellationToken token); Task<DeliveryDto> OutForDelivery(Guid tenantId,Guid deliveryId,Guid userId,string actor,bool manage,CancellationToken token);
 Task RequestOtp(Guid tenantId,Guid deliveryId,Guid userId,string actor,bool manage,CancellationToken token); Task<DeliveryDto> ConfirmOtp(Guid tenantId,Guid deliveryId,Guid userId,ConfirmOtpInput input,string actor,bool manage,CancellationToken token);
 Task<DeliveryDto> RecordCod(Guid tenantId,Guid deliveryId,Guid userId,CodCollectionInput input,string actor,bool manage,CancellationToken token); Task<DeliveryDto> Fail(Guid tenantId,Guid deliveryId,Guid userId,FailDeliveryInput input,string actor,bool manage,CancellationToken token);
 Task<DeliveryDto> Reschedule(Guid tenantId,Guid deliveryId,Guid userId,RescheduleDeliveryInput input,string actor,bool manage,CancellationToken token); Task<DeliveryDto> OverrideDelivered(Guid tenantId,Guid deliveryId,OverrideDeliveryInput input,Guid actorId,string actor,CancellationToken token);
}
