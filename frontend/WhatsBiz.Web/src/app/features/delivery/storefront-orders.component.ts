import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';

interface StorefrontOrder {
  id:string; orderNumber:string; placedAt:string; customerName:string; customerMobile?:string;
  itemCount:number; total:number; paymentMethod:string; paymentStatus:string; status:string;
  deliveryStatus?:string; deliveryAgent?:string; deliveryId?:string; deliveryCharge:number; promotionDiscount:number; promotionName?:string; cancellationRequestStatus?:string; cancellationReason?:string; refundStatus:string; refundAmount:number; refundableAmount:number; refundedAt?:string; refundId?:string; refundAttemptStatus?:string;
}

@Component({standalone:true,imports:[CommonModule,FormsModule,RouterLink],template:`
<section class="page">
  <header><div><small>Orders / Storefront</small><h1>Storefront orders</h1><p>Orders placed through your customer storefront.</p></div><button type="button" (click)="load()">Refresh</button></header>
  <form class="filters" (ngSubmit)="load()">
    <input name="search" [(ngModel)]="search" placeholder="Order, customer or mobile" aria-label="Search orders">
    <label>From<input name="from" type="date" [(ngModel)]="from"></label><label>To<input name="to" type="date" [(ngModel)]="to"></label>
    <select name="orderStatus" [(ngModel)]="orderStatus" aria-label="Order status"><option value="">All order statuses</option><option value="ORDER_CONFIRMED">Order confirmed</option><option value="PACKED">Packed</option><option value="OUT_FOR_DELIVERY">Out for delivery</option><option value="DELIVERED">Delivered</option><option value="DELIVERY_FAILED">Delivery failed</option><option value="CANCELLED">Cancelled</option></select>
    <select name="paymentStatus" [(ngModel)]="paymentStatus" aria-label="Payment status"><option value="">All payment statuses</option><option value="PENDING">Pending</option><option value="COD_PENDING">COD pending</option><option value="PAID">Paid</option><option value="FAILED">Failed</option><option value="CANCELLED">Cancelled</option></select>
    <select name="deliveryStatus" [(ngModel)]="deliveryStatus" aria-label="Delivery status"><option value="">All delivery statuses</option><option value="UNASSIGNED">Unassigned</option><option value="READY_FOR_PICKUP">Packed</option><option value="OUT_FOR_DELIVERY">Out for delivery</option><option value="DELIVERED">Delivered</option><option value="DELIVERY_FAILED">Failed</option><option value="CANCELLED">Cancelled</option></select>
    <button type="submit">Apply filters</button>
  </form>
  @if(error()){<p class="error" role="alert">{{error()}}</p>}
  @if(loading()){<p>Loading orders...</p>}@else{<p class="count">{{orders().length}} orders shown (latest 200)</p>
    <div class="orders">@for(order of orders();track order.id){<article>
      <div class="identity"><strong>{{order.orderNumber}}</strong><small>{{order.placedAt|date:'medium'}}</small></div>
      <div><b>{{order.customerName}}</b><small>{{order.customerMobile||'No mobile'}}</small></div>
      <div><b>{{order.itemCount}} items · {{order.total|currency:'INR'}}</b><small>{{order.paymentMethod}} · {{order.paymentStatus}}</small></div>
      <div><span class="status">{{order.status}}</span><small>{{order.deliveryStatus?.replaceAll('_',' ')||'Delivery not prepared'}}</small><small>{{order.deliveryAgent||'Unassigned'}}</small></div>
      <div class="actions">@if(order.cancellationRequestStatus==='REQUESTED'){<div class="request"><strong>Cancellation requested</strong><small>{{order.cancellationReason}}</small><small>Collected refundable: {{order.refundableAmount|currency:'INR'}}</small>@if(permissions.has('pos.void')){<button type="button" [disabled]="deciding()===order.id" (click)="decide(order,true)">Approve</button><button type="button" [disabled]="deciding()===order.id" (click)="decide(order,false)">Reject</button>}</div>}@else if(order.cancellationRequestStatus==='APPROVING'){<strong>Approval needs completion</strong>@if(permissions.has('pos.void')){<button type="button" [disabled]="deciding()===order.id" (click)="decide(order,true)">Retry approval</button>}}@else if(order.cancellationRequestStatus==='REJECTED'){<small>Cancellation rejected</small>}@else if(order.cancellationRequestStatus==='APPROVED'){<strong>Cancellation approved</strong>}
        @if(order.refundStatus!=='NONE'){<small>Refund: {{order.refundStatus.replaceAll('_',' ')}} {{order.refundAmount|currency:'INR'}}</small>@if(order.refundAttemptStatus==='UNKNOWN'||order.refundAttemptStatus==='CONFIRMED'&&order.refundStatus!=='REFUNDED'){<small>Reconciliation required</small>}}
        @if(order.cancellationRequestStatus==='APPROVED'&&order.refundStatus==='REFUND_REQUIRED'&&!order.refundId&&canManageRefund()){<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'prepare')">Prepare refund</button>}
        @if(order.refundId&&order.paymentMethod==='RAZORPAY'&&canManageRefund()&&order.refundStatus!=='REFUNDED'){@if(order.refundStatus==='REFUND_REQUIRED'||order.refundStatus==='REFUND_FAILED'){<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'start')">Start Razorpay refund</button>}@else{<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'reconcile')">Reconcile refund</button>}}
        @if(order.refundId&&(order.paymentMethod==='DIRECT_UPI'||order.paymentMethod==='COD')&&canManageRefund()&&order.refundStatus!=='REFUNDED'){<button type="button" (click)="openManual(order)">Record external refund</button>@if(manualOrder()===order.id){<div class="manual"><select aria-label="Refund settlement method" [(ngModel)]="manualMode"><option value="BANK">Bank</option><option value="UPI">UPI</option>@if(order.paymentMethod==='COD'){<option value="CASH">Cash</option>}</select><input aria-label="External refund reference" [(ngModel)]="manualReference" placeholder="Bank, UPI or cash receipt reference"><input aria-label="Refund date and time" type="datetime-local" [(ngModel)]="manualDate"><button type="button" [disabled]="refunding()===order.id||!manualReference||!manualDate" (click)="refundAction(order,'manual')">Confirm actual refund</button></div>}}
        @if(!order.deliveryId&&permissions.has('delivery.manage')){<button type="button" [disabled]="preparing()===order.id" (click)="prepare(order)">{{preparing()===order.id?'Preparing...':'Prepare delivery'}}</button>}
        @if(order.deliveryId&&permissions.has('delivery.manage')){<a [routerLink]="['/orders/deliveries']">Manage delivery</a>}
      </div>
    </article>}@empty{<p class="empty">No matching storefront orders.</p>}</div>}
</section>`,styles:[`:host{display:block}.page{padding:20px;max-width:1500px}.page header{display:flex;justify-content:space-between;gap:12px;align-items:start}.page h1{margin:3px 0}.page p,.page small{color:#667085}.filters{display:flex;flex-wrap:wrap;gap:9px;margin:18px 0}.filters label{display:grid;font-size:11px;color:#667085}.filters input,.filters select,.page button{min-height:39px;border:1px solid #cbd5e1;border-radius:8px;padding:7px 10px;background:#fff}.page button{cursor:pointer}.filters>input{min-width:210px}.filters button,.actions button{background:#146c43;color:#fff;border-color:#146c43}.orders{display:grid;gap:9px}.orders article{display:grid;grid-template-columns:1.2fr 1.4fr 1.2fr 1.2fr auto;align-items:center;gap:12px;padding:14px;border:1px solid #dce3df;border-radius:12px;background:#fff}.orders article>div{min-width:0}.orders b,.orders strong,.orders small{display:block;overflow-wrap:anywhere}.orders small{font-size:11px}.status{display:inline-block;padding:5px 8px;border-radius:99px;background:#e8f5ed;color:#145c43;font-size:11px;font-weight:700}.actions a{color:#146c43;font-weight:700}.error{color:#b42318!important}.count{font-size:12px}.empty{padding:20px;background:#fff;border-radius:12px}@media(max-width:950px){.orders article{grid-template-columns:1fr 1fr}.actions{grid-column:1/-1}}@media(max-width:560px){.page{padding:12px}.page header{align-items:start}.orders article{grid-template-columns:1fr}.actions{grid-column:auto}.filters>*{flex:1 1 130px;min-width:0!important}.filters>input{flex-basis:100%}}`]})
export class StorefrontOrdersComponent {
  private readonly http=inject(HttpClient); readonly permissions=inject(PermissionService);
  readonly orders=signal<StorefrontOrder[]>([]); readonly loading=signal(false); readonly error=signal(''); readonly preparing=signal(''); readonly deciding=signal(''); readonly refunding=signal(''); readonly manualOrder=signal('');
  manualMode='BANK'; manualReference=''; manualDate='';
  search='';from='';to='';orderStatus='';paymentStatus='';deliveryStatus='';
  constructor(){void this.load();}
  async load():Promise<void>{this.loading.set(true);this.error.set('');try{let params=new HttpParams();const values={search:this.search,from:this.from,to:this.to?new Date(this.to+'T23:59:59.999').toISOString():'',orderStatus:this.orderStatus,paymentStatus:this.paymentStatus,deliveryStatus:this.deliveryStatus};for(const [key,value] of Object.entries(values))if(value)params=params.set(key,value);this.orders.set(await firstValueFrom(this.http.get<StorefrontOrder[]>('/api/storefront-orders',{params})));}catch{this.error.set('Could not load storefront orders.');}finally{this.loading.set(false);}}
  async decide(order:StorefrontOrder,approve:boolean):Promise<void>{
    const note=approve?null:prompt('Reason for rejecting this cancellation request?')?.trim();
    if(!approve&&!note)return;
    if(!confirm((approve?'Approve':'Reject')+' cancellation for '+order.orderNumber+'?'))return;
    this.deciding.set(order.id);this.error.set('');
    try{
      await firstValueFrom(this.http.post('/api/storefront-orders/'+order.id+'/cancellation/'+(approve?'approve':'reject'),{note}));
      await this.load();
    }catch(error){
      const message=(error as {error?:{message?:string}})?.error?.message;
      this.error.set(message||'Cancellation decision could not be completed.');
    }finally{this.deciding.set('');}
  }  async prepare(order:StorefrontOrder):Promise<void>{if(!confirm(`Prepare delivery for ${order.orderNumber}?`))return;this.preparing.set(order.id);this.error.set('');try{await firstValueFrom(this.http.post(`/api/deliveries/orders/${order.id}/ready`,{}));await this.load();}catch{this.error.set('Could not prepare delivery. Check delivery settings and order status.');}finally{this.preparing.set('');}}
  canManageRefund():boolean{return this.permissions.has('pos.void')&&this.permissions.has('payment.create');}
  openManual(order:StorefrontOrder):void{this.manualOrder.set(order.id);this.manualMode=order.paymentMethod==='COD'?'CASH':'UPI';this.manualReference='';this.manualDate='';}
  async refundAction(order:StorefrontOrder,action:'prepare'|'start'|'reconcile'|'manual'):Promise<void>{
    const date=action==='manual'?new Date(this.manualDate):null;
    if(action==='manual'&&(!this.manualReference.trim()||!date||Number.isNaN(date.valueOf()))){this.error.set('Enter a valid refund reference and date.');return;}
    if(action==='manual'&&!confirm(`Confirm that ${order.refundAmount} was actually refunded outside the system?`))return;
    if(action==='start'&&!confirm(`Start the Razorpay refund for ${order.orderNumber}?`))return;
    this.refunding.set(order.id);this.error.set('');
    const base='/api/storefront-refunds';
    const url=action==='prepare'?`${base}/orders/${order.id}/prepare`:
      `${base}/${order.refundId}/${action==='manual'?'manual/confirm':`razorpay/${action}`}`;
    const body=action==='manual'?{settlementMode:this.manualMode,externalReference:this.manualReference.trim(),settledAt:date!.toISOString()}:{};
    try{await firstValueFrom(this.http.post(url,body));this.manualOrder.set('');await this.load();}
    catch(error){this.error.set((error as {error?:{message?:string}})?.error?.message||'Refund action needs attention. Refresh before retrying.');}
    finally{this.refunding.set('');}
  }
}
