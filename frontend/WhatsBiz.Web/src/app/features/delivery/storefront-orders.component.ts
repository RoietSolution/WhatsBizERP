import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { ShippingLabel4UpPreviewComponent } from './shipping-label-4up-preview.component';
import { StorefrontShippingLabel } from './shipping-label.models';

interface StorefrontOrder {
  id:string; orderNumber:string; placedAt:string; customerName:string; customerMobile?:string;
  itemCount:number; total:number; paymentMethod:string; paymentStatus:string; status:string;
  deliveryStatus?:string; deliveryAgent?:string; deliveryId?:string; deliveryCharge:number; promotionDiscount:number; promotionName?:string; cancellationRequestStatus?:string; cancellationReason?:string; refundStatus:string; refundAmount:number; refundableAmount:number; refundedAt?:string; refundId?:string; refundAttemptStatus?:string;
}
interface StorefrontReturnRequest { requestId:string;orderId:string;invoiceItemId:string;orderNumber:string;productName:string;productCode?:string;quantity:number;reason:string;explanation?:string;status:string;requestedAtUtc:string;decidedAtUtc?:string;decisionNote?:string; }

@Component({standalone:true,imports:[CommonModule,FormsModule,RouterLink,ShippingLabel4UpPreviewComponent],template:`
<section class="page"><section>
  <header><div><small>Orders / Storefront</small><h1>Storefront orders</h1><p>Orders placed through your customer storefront.</p></div><div class="header-actions"><button type="button" (click)="printSelected()" [disabled]="!selectedIds().length">Print shipping label{{selectedIds().length>1?'s':''}}</button><button type="button" (click)="returnPanel.set(!returnPanel())">Return requests ({{returnRequests().length}})</button><button type="button" (click)="load()">Refresh</button></div></header>
  @if(returnPanel()){<section class="return-panel"><h2>Customer return requests</h2>@if(returnError()){<p class="error" role="alert">{{returnError()}}</p>}@for(request of returnRequests();track request.requestId){<article><strong>{{request.orderNumber}} · {{request.productName}}</strong><small>{{request.productCode||'No SKU'}} · Qty {{request.quantity}} · {{request.reason}} · {{request.status}}</small>@if(request.status==='REQUESTED'&&permissions.has('pos.return')){<button type="button" (click)="decideReturn(request,'APPROVED')">Approve</button><button type="button" (click)="decideReturn(request,'REJECTED')">Reject</button>}@else if(request.status==='APPROVED'&&permissions.has('pos.return')){<button type="button" (click)="decideReturn(request,'RECEIVED')">Mark received</button>}@else if(request.status==='RECEIVED'&&permissions.has('pos.return')){<button type="button" (click)="decideReturn(request,'COMPLETED')">Mark completed</button>}</article>}@empty{<p class="empty">No customer return requests.</p>}</section>}
  <div class="shipping-toolbar" aria-label="Shipping label actions"><button type="button" (click)="selectAll()">Select all eligible</button><button type="button" (click)="clearSelection()" [disabled]="!selectedIds().length">Clear selection</button><span>{{selectedIds().length}} selected</span></div>
  <div class="bulk-print-action"><button type="button" (click)="printSelected()" [disabled]="!selectedIds().length">Bulk Print Shipping Labels ({{selectedIds().length}})</button></div>
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
    <div class="orders">@for(order of orders();track order.id){<article><label class="select-order"><input type="checkbox" [checked]="selectedIds().includes(order.id)" [disabled]="!isLabelEligible(order).eligible" (change)="toggleOrder(order.id)"><span>Select</span></label>
      <div class="identity"><strong>{{order.orderNumber}}</strong><small>{{order.placedAt|date:'medium'}}</small></div>
      <div><b>{{order.customerName}}</b><small>{{order.customerMobile||'No mobile'}}</small></div>
      <div><b>{{order.itemCount}} items · {{order.total|currency:'INR'}}</b><small>{{order.paymentMethod}} · {{order.paymentStatus}}</small></div>
      <div><span class="status">{{order.status}}</span><small>{{order.deliveryStatus?.replaceAll('_',' ')||'Delivery not prepared'}}</small>@if(order.deliveryId){<small class="assignment" [class.unassigned]="!order.deliveryAgent">{{order.deliveryAgent?'ASSIGNED':'UNASSIGNED'}} · {{order.deliveryAgent||'Manual assignment required'}}</small>}</div>
      <div class="actions"><button type="button" class="print-one" [disabled]="!isLabelEligible(order).eligible" [title]="isLabelEligible(order).reason" (click)="printOne(order.id)">Print label</button>@if(!isLabelEligible(order).eligible){<small class="label-unavailable">{{isLabelEligible(order).reason}}</small>}@if(order.cancellationRequestStatus==='REQUESTED'){<div class="request"><strong>Cancellation requested</strong><small>{{order.cancellationReason}}</small><small>Collected refundable: {{order.refundableAmount|currency:'INR'}}</small>@if(permissions.has('pos.void')){<button type="button" [disabled]="deciding()===order.id" (click)="decide(order,true)">Approve</button><button type="button" [disabled]="deciding()===order.id" (click)="decide(order,false)">Reject</button>}</div>}@else if(order.cancellationRequestStatus==='APPROVING'){<strong>Approval needs completion</strong>@if(permissions.has('pos.void')){<button type="button" [disabled]="deciding()===order.id" (click)="decide(order,true)">Retry approval</button>}}@else if(order.cancellationRequestStatus==='REJECTED'){<small>Cancellation rejected</small>}@else if(order.cancellationRequestStatus==='APPROVED'){<strong>Cancellation approved</strong>}
        @if(order.refundStatus!=='NONE'){<small>Refund: {{order.refundStatus.replaceAll('_',' ')}} {{order.refundAmount|currency:'INR'}}</small>@if(order.refundAttemptStatus==='UNKNOWN'||order.refundAttemptStatus==='CONFIRMED'&&order.refundStatus!=='REFUNDED'){<small>Reconciliation required</small>}}
        @if(order.cancellationRequestStatus==='APPROVED'&&order.refundStatus==='REFUND_REQUIRED'&&!order.refundId&&canManageRefund()){<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'prepare')">Prepare refund</button>}
        @if(order.refundId&&order.paymentMethod==='RAZORPAY'&&canManageRefund()&&order.refundStatus!=='REFUNDED'){@if(order.refundStatus==='REFUND_REQUIRED'||order.refundStatus==='REFUND_FAILED'){<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'start')">Start Razorpay refund</button>}@else{<button type="button" [disabled]="refunding()===order.id" (click)="refundAction(order,'reconcile')">Reconcile refund</button>}}
        @if(order.refundId&&(order.paymentMethod==='DIRECT_UPI'||order.paymentMethod==='COD')&&canManageRefund()&&order.refundStatus!=='REFUNDED'){<button type="button" (click)="openManual(order)">Record external refund</button>@if(manualOrder()===order.id){<div class="manual"><select aria-label="Refund settlement method" [(ngModel)]="manualMode"><option value="BANK">Bank</option><option value="UPI">UPI</option>@if(order.paymentMethod==='COD'){<option value="CASH">Cash</option>}</select><input aria-label="External refund reference" [(ngModel)]="manualReference" placeholder="Bank, UPI or cash receipt reference"><input aria-label="Refund date and time" type="datetime-local" [(ngModel)]="manualDate"><button type="button" [disabled]="refunding()===order.id||!manualReference||!manualDate" (click)="refundAction(order,'manual')">Confirm actual refund</button></div>}}
        @if(!order.deliveryId&&permissions.has('delivery.manage')){<button type="button" [disabled]="preparing()===order.id" (click)="prepare(order)">{{preparing()===order.id?'Preparing...':'Prepare delivery'}}</button>}
        @if(order.deliveryId&&permissions.has('delivery.manage')){<a [routerLink]="['/orders/deliveries']">{{order.deliveryAgent?'Manage delivery':'Assign delivery agent'}}</a>}
      </div>
    </article>}@empty{<p class="empty">No matching storefront orders.</p>}</div>}
</section><app-shipping-label-4up-preview [open]="labelPreviewOpen()" [orders]="labelOrders()" (close)="closeLabelPreview()"></app-shipping-label-4up-preview></section>`,styles:[`:host{display:block}.page{padding:20px;max-width:1500px}.page header{display:flex;justify-content:space-between;gap:12px;align-items:start}.header-actions{display:flex;gap:8px}.page h1{margin:3px 0}.page p,.page small{color:#667085}.filters{display:flex;flex-wrap:wrap;gap:9px;margin:18px 0}.filters label{display:grid;font-size:11px;color:#667085}.filters input,.filters select,.page button{min-height:39px;border:1px solid #cbd5e1;border-radius:8px;padding:7px 10px;background:#fff}.page button{cursor:pointer}.filters>input{min-width:210px}.filters button,.actions button{background:#146c43;color:#fff;border-color:#146c43}.orders{display:grid;gap:9px}.orders article{display:grid;grid-template-columns:auto 1.2fr 1.4fr 1.2fr 1.2fr auto;align-items:center;gap:12px;padding:14px;border:1px solid #dce3df;border-radius:12px;background:#fff}.select-order{display:flex;align-items:center;gap:5px;font-size:10px}.select-order input{min-height:auto}.orders article>div{min-width:0}.orders b,.orders strong,.orders small{display:block;overflow-wrap:anywhere}.orders small{font-size:11px}.status{display:inline-block;padding:5px 8px;border-radius:99px;background:#e8f5ed;color:#145c43;font-size:11px;font-weight:700}.actions a{color:#146c43;font-weight:700}.error{color:#b42318!important}.count{font-size:12px}.empty{padding:20px;background:#fff;border-radius:12px}@media(max-width:950px){.orders article{grid-template-columns:1fr 1fr}.actions{grid-column:1/-1}}@media(max-width:560px){.page{padding:12px}.page header{align-items:start}.header-actions{flex-wrap:wrap}.orders article{grid-template-columns:1fr}.actions{grid-column:auto}.filters>*{flex:1 1 130px;min-width:0!important}.filters>input{flex-basis:100%}}`]})
export class StorefrontOrdersComponent {
  private readonly http=inject(HttpClient); readonly permissions=inject(PermissionService);
  readonly orders=signal<StorefrontOrder[]>([]); readonly loading=signal(false); readonly error=signal(''); readonly preparing=signal(''); readonly deciding=signal(''); readonly refunding=signal(''); readonly manualOrder=signal(''); readonly selectedIds=signal<string[]>([]); readonly labelOrders=signal<StorefrontShippingLabel[]>([]); readonly labelPreviewOpen=signal(false); readonly returnRequests=signal<StorefrontReturnRequest[]>([]); readonly returnError=signal(''); readonly returnPanel=signal(false);
  manualMode='BANK'; manualReference=''; manualDate='';
  search='';from='';to='';orderStatus='';paymentStatus='';deliveryStatus='';
  constructor(){void this.load();void this.loadReturns();}
  async loadReturns():Promise<void>{this.returnError.set('');try{this.returnRequests.set(await firstValueFrom(this.http.get<StorefrontReturnRequest[]>('/api/storefront-orders/return-requests')));}catch{this.returnError.set('Could not load customer return requests.');}}
  async decideReturn(request:StorefrontReturnRequest,status:string):Promise<void>{if(!confirm(`${status==='APPROVED'?'Approve':'Reject'} return request for ${request.productName}?`))return;try{await firstValueFrom(this.http.post(`/api/storefront-orders/return-requests/${request.requestId}/decision`,{status}));await this.loadReturns();}catch(error){this.returnError.set((error as {error?:{message?:string}})?.error?.message||'Return request could not be updated.');}}
  async load():Promise<void>{this.loading.set(true);this.error.set('');try{let params=new HttpParams();const values={search:this.search,from:this.from,to:this.to?new Date(this.to+'T23:59:59.999').toISOString():'',orderStatus:this.orderStatus,paymentStatus:this.paymentStatus,deliveryStatus:this.deliveryStatus};for(const [key,value] of Object.entries(values))if(value)params=params.set(key,value);this.orders.set(await firstValueFrom(this.http.get<StorefrontOrder[]>('/api/storefront-orders',{params})));}catch{this.error.set('Could not load storefront orders.');}finally{this.loading.set(false);}}
  toggleOrder(id:string):void{this.selectedIds.update(ids=>ids.includes(id)?ids.filter(x=>x!==id):[...ids,id]);}
  selectAll():void{this.selectedIds.set(this.orders().filter(order=>order.status!=='CANCELLED'&&this.isLabelEligible(order).eligible).map(order=>order.id).slice(0,50));}
  clearSelection():void{this.selectedIds.set([]);}
  async printSelected():Promise<void>{const ids=this.selectedIds();if(!ids.length){this.error.set('Select at least one eligible order to print.');return;}await this.printIds(ids.slice(0,50));}
  async printOne(id:string):Promise<void>{await this.printIds([id]);}
  private async printIds(ids:string[]):Promise<void>{this.error.set('');try{const orders=this.orders();const known=new Map(orders.map(order=>[order.id,order]));const skipped=ids.filter(id=>known.has(id)&&!this.isLabelEligible(known.get(id)!).eligible);const eligible=ids.filter(id=>!skipped.includes(id));if(skipped.length&&!window.confirm(`${skipped.length} order(s) cannot be printed because payment or fulfillment is not eligible. Continue with ${eligible.length} eligible order(s)?`))return;if(!eligible.length){this.error.set(skipped.map(id=>this.isLabelEligible(known.get(id)!).reason).filter((value,index,array)=>array.indexOf(value)===index).join(' '));return;}const results=await Promise.allSettled(eligible.map(id=>firstValueFrom(this.http.get<StorefrontShippingLabel>(`/api/storefront-orders/${id}/shipping-label`))));const labels=results.flatMap(result=>result.status==='fulfilled'?[result.value]:[]);const failed=results.flatMap((result,index)=>result.status==='rejected'?[known.get(eligible[index])?.orderNumber||eligible[index]]:[]);if(!labels.length){this.error.set(failed.length?`No eligible shipping labels could be loaded: ${failed.join(', ')}.`:'No eligible orders were selected.');return;}const unique=Array.from(new Map(labels.map(label=>[label.id,label])).values());this.labelOrders.set(unique);this.labelPreviewOpen.set(true);const messages=[...failed,...skipped.map(id=>known.get(id)?.orderNumber||id)];if(messages.length)this.error.set(`Some orders were skipped or could not be loaded: ${messages.join(', ')}.`);}catch{this.error.set('Shipping labels could not be loaded. Try again.');}}
  isLabelEligible(order:StorefrontOrder):{eligible:boolean;reason:string}{if(order.status==='CANCELLED'||order.status==='Payment Failed'||order.status==='Cancelled'||['CANCELLED','DELIVERY_FAILED'].includes(order.deliveryStatus?.toUpperCase()??''))return{eligible:false,reason:'Shipping label unavailable: this order is cancelled or no longer eligible for fulfillment.'};if(order.paymentMethod?.toUpperCase()==='COD'||['PAID','CAPTURED'].includes(order.paymentStatus?.toUpperCase()??''))return{eligible:true,reason:''};return{eligible:false,reason:['FAILED','CANCELLED'].includes(order.paymentStatus?.toUpperCase()??'')?'Shipping label unavailable: online payment failed.':'Shipping label unavailable: online payment has not been confirmed.'};}
  closeLabelPreview():void{this.labelPreviewOpen.set(false);}
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
  }
  async prepare(order:StorefrontOrder):Promise<void>{
    if(!confirm(`Prepare delivery for ${order.orderNumber}?`))return;
    this.preparing.set(order.id);this.error.set('');
    try{await firstValueFrom(this.http.post(`/api/deliveries/orders/${order.id}/ready`,{}));await this.load();}
    catch(error){this.error.set(this.apiMessage(error,'Delivery could not be prepared. Check that Delivery Management is enabled, the order is eligible, and an active/available agent or default assignment is configured.'));}
    finally{this.preparing.set('');}
  }
  private apiMessage(error:unknown,fallback:string):string{const body=(error as {error?:{message?:string;detail?:string}})?.error;return body?.message||body?.detail||fallback;}
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
