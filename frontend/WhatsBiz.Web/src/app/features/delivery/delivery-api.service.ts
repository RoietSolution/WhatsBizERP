import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface DeliveryAgent { deliveryAgentId:string; userId:string; displayName:string; mobile:string; isActive:boolean; isAvailable:boolean; isDefault:boolean; activeDeliveries:number; deliveredToday:number; }
export interface DeliveryUser { userId:string;userName:string;email?:string;mobile?:string; }
export interface DeliverySettings { deliveryEnabled:boolean; automaticAssignmentEnabled:boolean; defaultDeliveryAgentId?:string; defaultAgentValid:boolean; warning?:string; requireDeliveryOtp:boolean; deliveryOtpExpiryMinutes:number; maxOtpAttempts:number; otpResendCooldownSeconds:number; maxOtpResends:number; notifyOnAssigned:boolean; notifyOnOutForDelivery:boolean; notifyOnDelivered:boolean; notifyOnDeliveryFailed:boolean; requireCodConfirmation:boolean; assignedTemplateName?:string; outForDeliveryTemplateName?:string; otpTemplateName?:string; deliveredTemplateName?:string; failedTemplateName?:string; rescheduledTemplateName?:string; }
export interface Delivery { deliveryId:string; orderId:string; orderNumber:string; orderDate:string; status:string; deliveryAgentId?:string; deliveryAgentName?:string; assignmentSource?:string; customerName:string; customerMobile?:string; address:string; notes?:string; amount:number; codRequired:boolean; codAmount:number; codCollected:boolean; codPaymentMethod?:string; codCollectedAmount?:number; codCollectedAt?:string; codReference?:string; codConfirmedBy?:string; deliveredAt?:string; proofType?:string; scheduledDate?:string; timeWindow?:string; sourceChannel:string; otpVerified:boolean; updatedAt:string; navigationUrl:string; }
export interface DeliveryDashboard { unassigned:number; assigned:number; ready:number; outForDelivery:number; deliveredToday:number; failed:number; codPending:number; rescheduled:number; deliveries:Delivery[]; }
export interface MarketplaceFulfilmentItem { invoiceItemId:string; productId:string; productName:string; quantity:number; }
export interface MarketplaceFulfilmentEvent { eventId:number; eventType:string; previousStatus?:string|null; newStatus?:string|null; deliveryAgentId?:string|null; actorUserId?:string|null; notes?:string|null; createdAt:string; }
export interface MarketplaceFulfilmentGroup {
  fulfilmentGroupId:string; orderId:string; groupNumber:number; warehouseId:string; warehouseName:string; sellerKind:string; partnerSellerId?:string|null; supplierId?:string|null; sellerName?:string|null;
  status:string; deliveryAgentId?:string|null; deliveryAgentName?:string|null; assignmentSource?:string|null; deliveryAddress:string; trackingReference?:string|null;
  readyAt?:string|null; dispatchedAt?:string|null; deliveredAt?:string|null; cancelledAt?:string|null; items:MarketplaceFulfilmentItem[]; events:MarketplaceFulfilmentEvent[];
  parentLifecycleStatus:string; parentPaymentStatus:string; parentDeliveryStatus:string; parentRefundStatus:string; parentCodStatus?:string|null;
  codCollectionFulfilmentGroupId?:string|null; codExpectedAmount?:number|null; codCollectedAmount?:number|null; codCollectedAt?:string|null;
  refundStatus?:string|null; refundAmount?:number|null; refundReviewReason?:string|null;
}
export interface MarketplaceOrderResult { orderId:string; status:string; amount:number; fulfilmentGroupId?:string|null; collectorUserId?:string|null; collectedAt?:string|null; receiptReference?:string|null; isReplay:boolean; }
export interface MarketplaceDeliveryOrder { orderId:string;orderNumber:string;orderDate:string;customerName:string;total:number;paymentMethod:string;paymentStatus:string;assignedGroupCount:number; }
export interface DeliveryVerificationRequirements { requireDeliveryOtp:boolean;requireCodConfirmation:boolean; }

@Injectable({providedIn:'root'})
export class DeliveryApiService {
  constructor(private readonly http:HttpClient){}
  dashboard(filters:Record<string,string>={}){let params=new HttpParams();Object.entries(filters).forEach(([k,v])=>{if(v)params=params.set(k,v)});return this.http.get<DeliveryDashboard>('/api/deliveries',{params});}
  my(status=''){return this.http.get<Delivery[]>('/api/delivery/my-deliveries',{params:status?{status}:{}});}
  agents(){return this.http.get<DeliveryAgent[]>('/api/delivery-agents');}
  eligibleUsers(){return this.http.get<DeliveryUser[]>('/api/delivery-agents/eligible-users');}
  saveAgent(agent:Partial<DeliveryAgent>&{userId:string;displayName:string;mobile:string},id?:string){const body={userId:agent.userId,displayName:agent.displayName,mobile:agent.mobile,isActive:agent.isActive??true,isAvailable:agent.isAvailable??true};return id?this.http.put<DeliveryAgent>(`/api/delivery-agents/${id}`,body):this.http.post<DeliveryAgent>('/api/delivery-agents',body);}
  setDefault(id?:string){return this.http.post<void>(id?`/api/delivery-agents/${id}/set-default`:'/api/delivery-agents/remove-default',{});}
  settings(){return this.http.get<DeliverySettings>('/api/delivery-settings');} saveSettings(value:DeliverySettings){return this.http.put<DeliverySettings>('/api/delivery-settings',value);}
  assign(id:string,deliveryAgentId?:string,reason?:string){return this.http.post<Delivery>(`/api/deliveries/${id}/assign`,{deliveryAgentId:deliveryAgentId||null,reason});}
  action(id:string,action:string,body:unknown={}){return this.http.post<Delivery|void>(`/api/deliveries/${id}/${action}`,body);}
  marketplaceGroups(orderId:string){return this.http.get<MarketplaceFulfilmentGroup[]>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups`);}
  marketplaceAssignedOrders(){return this.http.get<MarketplaceDeliveryOrder[]>('/api/deliveries/marketplace-orders/assigned');}
  marketplaceVerificationRequirements(){return this.http.get<DeliveryVerificationRequirements>('/api/delivery/marketplace-fulfilment-requirements');}
  assignMarketplaceGroup(orderId:string,groupId:string,deliveryAgentId?:string,reason?:string){return this.http.post<MarketplaceFulfilmentGroup>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/assign`,{deliveryAgentId:deliveryAgentId||null,reason});}
  transitionMarketplaceGroup(orderId:string,groupId:string,status:string,reason?:string,trackingReference?:string){return this.http.post<MarketplaceFulfilmentGroup>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/status`,{status,reason,trackingReference});}
  requestMarketplaceGroupOtp(orderId:string,groupId:string){return this.http.post<void>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/request-otp`,{});}
  confirmMarketplaceGroupOtp(orderId:string,groupId:string,otp:string){return this.http.post<MarketplaceFulfilmentGroup>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/confirm-delivery-otp`,{otp});}
  confirmMarketplaceGroup(orderId:string,groupId:string,reason:string){return this.http.post<MarketplaceFulfilmentGroup>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/confirm-delivery`,{reason});}
  overrideMarketplaceGroup(orderId:string,groupId:string,reason:string){return this.http.post<MarketplaceFulfilmentGroup>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/override-delivered`,{reason});}
  cancelMarketplaceGroup(orderId:string,groupId:string,reason:string,restockTransactionId?:string){return this.http.post<{group:MarketplaceFulfilmentGroup;refundStatus:string;refundAmount?:number|null;refundReviewReason?:string|null}>(`/api/deliveries/marketplace-orders/${orderId}/fulfilment-groups/${groupId}/cancel`,{reason,restockTransactionId:restockTransactionId||null});}
  assignMarketplaceCod(orderId:string,fulfilmentGroupId:string,reason:string,idempotencyKey:string){return this.http.post<MarketplaceOrderResult>(`/api/deliveries/marketplace-orders/${orderId}/cod-collection/assign`,{fulfilmentGroupId,reason,idempotencyKey});}
}
