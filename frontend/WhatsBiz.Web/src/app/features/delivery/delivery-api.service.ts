import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface DeliveryAgent { deliveryAgentId:string; userId:string; displayName:string; mobile:string; isActive:boolean; isAvailable:boolean; isDefault:boolean; activeDeliveries:number; deliveredToday:number; }
export interface DeliveryUser { userId:string;userName:string;email?:string;mobile?:string; }
export interface DeliverySettings { deliveryEnabled:boolean; automaticAssignmentEnabled:boolean; defaultDeliveryAgentId?:string; defaultAgentValid:boolean; warning?:string; requireDeliveryOtp:boolean; deliveryOtpExpiryMinutes:number; maxOtpAttempts:number; otpResendCooldownSeconds:number; maxOtpResends:number; notifyOnAssigned:boolean; notifyOnOutForDelivery:boolean; notifyOnDelivered:boolean; notifyOnDeliveryFailed:boolean; requireCodConfirmation:boolean; assignedTemplateName?:string; outForDeliveryTemplateName?:string; otpTemplateName?:string; deliveredTemplateName?:string; failedTemplateName?:string; rescheduledTemplateName?:string; }
export interface Delivery { deliveryId:string; orderId:string; orderNumber:string; orderDate:string; status:string; deliveryAgentId?:string; deliveryAgentName?:string; assignmentSource?:string; customerName:string; customerMobile?:string; address:string; notes?:string; amount:number; codRequired:boolean; codAmount:number; codCollected:boolean; codPaymentMethod?:string; scheduledDate?:string; timeWindow?:string; sourceChannel:string; otpVerified:boolean; updatedAt:string; navigationUrl:string; }
export interface DeliveryDashboard { unassigned:number; assigned:number; ready:number; outForDelivery:number; deliveredToday:number; failed:number; codPending:number; rescheduled:number; deliveries:Delivery[]; }

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
}
