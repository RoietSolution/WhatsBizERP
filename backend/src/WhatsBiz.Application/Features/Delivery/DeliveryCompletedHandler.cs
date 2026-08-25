using MediatR;
using WhatsBiz.Application.Features.Loyalty;
namespace WhatsBiz.Application.Features.Delivery;
public sealed class DeliveryCompletedHandler(ILoyaltyService loyalty) : INotificationHandler<OrderDeliveryCompletedEvent>
{ public Task Handle(OrderDeliveryCompletedEvent notification,CancellationToken cancellationToken)=>loyalty.ProcessOrderAsync(notification.TenantId,notification.OrderId,"DELIVERED",notification.Actor,cancellationToken); }
