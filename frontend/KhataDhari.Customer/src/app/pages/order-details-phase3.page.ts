import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { CustomerOrder, CustomerOrderLine } from '../models/storefront.models';
import { codDueLabel, orderStatusLabel, paymentLabel } from '../order-status-presentation';

@Component({
  standalone: true,
  imports: [CurrencyPipe, DatePipe, RouterLink, FormsModule],
  template: `
    <section class="page order-details-page">
      <a class="back" [routerLink]="['/', storeKey, 'orders']">← My orders</a>
      @if (loading()) { <p class="state" role="status">Loading order details...</p> }
      @else if (!order()) { <p class="state">This order was not found for your account.</p> }
      @else if (order(); as current) {
        @let fulfilmentGroups = current.fulfilmentGroups ?? [];
        <header class="order-heading"><div><span class="eyebrow">ORDER DETAILS</span><h1>{{ current.orderNumber }}</h1><time [attr.datetime]="current.placedAt">{{ current.placedAt | date:'medium' }}</time></div><strong>{{ current.total | currency:'INR' }}</strong></header>

        <div class="detail-grid">
          <article class="detail-card tracking-card"><div class="section-heading"><div><span class="eyebrow">DELIVERY</span><h2>Order progress</h2></div><span class="status-badge" [class]="statusClass(current.status)">{{ orderStatusLabel(current.status) }}</span></div>
            @if (fulfilmentGroups.length) { <p class="muted">{{ orderStatusLabel(current.status) }} across {{ fulfilmentGroups.length }} deliveries. See each delivery below for its latest progress.</p> } @else if (current.timeline?.length) { <ol class="timeline">@for (step of current.timeline || []; track step.code) { <li [class.complete]="step.state==='complete'" [class.current]="step.state==='current'" [class.exception]="step.state==='exception'"><span class="dot" aria-hidden="true">{{ step.state==='complete' ? '✓' : step.state==='current'||step.state==='exception' ? '•' : '○' }}</span><div><strong>{{ step.label }}</strong>@if (step.occurredAt) { <time>{{ step.occurredAt | date:'medium' }}</time> }</div></li> }</ol> } @else { <p class="muted">Delivery tracking updates will appear here when available.</p> }
            @if (current.trackingNumber) { <p class="tracking-number"><b>Tracking number</b> {{ current.trackingNumber }}</p> }
          </article>

          <article class="detail-card summary-card"><div class="section-heading"><div><span class="eyebrow">SUMMARY</span><h2>Order summary</h2></div></div><dl class="summary-list"><div><dt>Items</dt><dd>{{ current.itemCount }}</dd></div><div><dt>Payment</dt><dd>{{ paymentLabel(current.paymentStatus, current.paymentMethod) }}</dd></div>@if (current.deliveryStatus) { <div><dt>Delivery</dt><dd>{{ orderStatusLabel(current.deliveryStatus) }}</dd></div> }</dl>@if (codDueLabel(current)) { <p class="payment-note">{{ codDueLabel(current) }}</p> }</article>
        </div>

        @if (fulfilmentGroups.length) {
          <article class="detail-card fulfilments-card" aria-label="Separate deliveries">
            <div class="section-heading"><div><span class="eyebrow">DELIVERY PROGRESS</span><h2>{{ fulfilmentGroups.length }} separate deliveries</h2></div></div>
            <p class="payment-note">One order total and one payment cover all deliveries in this order.</p>
            @if (current.paymentMethod.toUpperCase() === 'COD' && isPaymentCollected(current.paymentStatus)) { <p class="payment-note collected-note">The full order amount has been collected. No further payment is due for later deliveries.</p> }
            <div class="delivery-groups">
              @for (group of fulfilmentGroups; track group.groupNumber; let index = $index) {
                <section class="delivery-group">
                  <header class="delivery-group-heading"><div><span class="eyebrow">DELIVERY {{ index + 1 }} OF {{ fulfilmentGroups.length }}</span><h3>{{ groupStatusLabel(group.status) }}</h3></div><span class="status-badge" [class]="groupStatusClass(group.status)">{{ groupStatusLabel(group.status) }}</span></header>
                  <div class="delivery-items">
                    @for (item of group.items; track item.productId) {
                      <div class="delivery-item">
                        @if (lineImages()[item.productId]) { <img [src]="lineImages()[item.productId]" [alt]="item.productName" /> } @else { <span class="delivery-image-placeholder" aria-hidden="true">□</span> }
                        <strong>{{ item.productName }}</strong><span class="delivery-quantity">Qty {{ item.quantity }}</span>
                      </div>
                    }
                  </div>
                  <ol class="delivery-timeline" [class.exception]="isGroupException(group.status)">
                    <li [class.complete]="groupStatusKey(group.status) === 'READY_FOR_DISPATCH' || group.dispatchedAt || group.deliveredAt" [class.current]="['PENDING','PREPARING'].includes(groupStatusKey(group.status))"><span class="timeline-dot"></span><div><strong>Preparing</strong></div></li>
                    <li [class.complete]="group.dispatchedAt || group.deliveredAt" [class.current]="groupStatusKey(group.status) === 'READY_FOR_DISPATCH'"><span class="timeline-dot"></span><div><strong>Ready for dispatch</strong>@if (group.readyAt) { <time>{{ group.readyAt | date:'medium' }}</time> }</div></li>
                    <li [class.complete]="group.dispatchedAt || group.deliveredAt" [class.current]="groupStatusKey(group.status) === 'OUT_FOR_DELIVERY'"><span class="timeline-dot"></span><div><strong>Out for delivery</strong>@if (group.dispatchedAt) { <time>{{ group.dispatchedAt | date:'medium' }}</time> }</div></li>
                    <li [class.complete]="group.deliveredAt" [class.current]="groupStatusKey(group.status) === 'DELIVERED'"><span class="timeline-dot"></span><div><strong>{{ finalDeliveryStepLabel(group.status) }}</strong>@if (group.deliveredAt) { <time>{{ group.deliveredAt | date:'medium' }}</time> }</div></li>
                  </ol>
                  @if (group.trackingReference) { <p class="tracking-number"><b>Tracking reference</b> <span>{{ group.trackingReference }}</span></p> }
                </section>
              }
            </div>
            @if (current.refundStatus && current.refundStatus !== 'No refund due') { <p class="refund-note" [class.review]="current.refundStatus === 'Refund review required'"><b>{{ refundStatusLabel(current.refundStatus) }}</b>@if (current.refundStatus === 'Refund review required') { <span>We are reviewing the amount before confirming any refund.</span> } @else if (current.refundStatus === 'Refund required') { <span>A refund is due and is awaiting processing.</span> } @else if (current.refundStatus === 'Refund in progress') { <span>Refund processing is in progress. It is not yet complete.</span> }</p> }
          </article>
        }

        <article class="detail-card products-card"><div class="section-heading"><div><span class="eyebrow">ITEMS</span><h2>Ordered products</h2></div></div><div class="line-list">@for (line of current.lines || []; track line.productId) { <div class="product-line">@if (lineImages()[line.productId]) { <img [src]="lineImages()[line.productId]" [alt]="line.productName" /> } @else { <span class="product-placeholder" aria-hidden="true">□</span> }<div class="product-copy"><strong>{{ line.productName }}</strong>@if (line.packSize) { <small>{{ line.packSize }}</small> }<small>Quantity {{ line.quantity }}</small></div><strong class="line-total">{{ line.lineTotal | currency:'INR' }}</strong></div> }</div><div class="totals"><div><span>Merchandise subtotal</span><b>{{ (current.merchandiseSubtotal ?? ((current.merchandiseAmount ?? current.total) - (current.merchandiseTaxAmount ?? 0))) | currency:'INR' }}</b></div>@if (current.promotionDiscount) { <div><span>{{ current.promotionName || 'Storefront offer' }}</span><b class="discount">-{{ current.promotionDiscount | currency:'INR' }}</b></div> }<div><span>GST</span><b>{{ (current.merchandiseTaxAmount ?? 0) | currency:'INR' }}</b></div><div><span>Delivery charges</span><b>{{ current.deliveryCharge ? (current.deliveryCharge | currency:'INR') : 'FREE' }}</b></div><div class="grand-total"><span>Total payable</span><b>{{ current.total | currency:'INR' }}</b></div></div></article>

        @if (current.cancellation; as cancellation) { <article class="detail-card request-card"><div class="section-heading"><div><span class="eyebrow">CANCELLATION</span><h2>Cancellation request</h2></div><span class="status-badge" [class]="requestClass(cancellation.requestStatus)">{{ cancellationStatus(cancellation.requestStatus, current.status) }}</span></div><p>{{ cancellation.reason || 'Your cancellation request is being reviewed.' }}</p>@if (!cancellation.canRequest && cancellation.unavailableReason) { <p class="muted">{{ cancellation.unavailableReason }}</p> }@if (cancellation.decisionNote) { <p class="decision-note">{{ cancellation.decisionNote }}</p> }@if (isCancelled(current.status)) { <p class="muted">{{ refundMessage(cancellation.refundStatus, cancellation.refundAmount, cancellation.refundedAt) }}</p> }</article> }

        @if (current.cancellation?.canRequest) { <article class="detail-card action-card"><span class="eyebrow">NEED HELP?</span><h2>Cancel this order</h2><p class="muted">The retailer must approve a cancellation. A request does not itself confirm a refund.</p><label for="cancellation-reason">Reason</label><select id="cancellation-reason" [value]="reason" (change)="reason=selectValue($event)"><option value="">Select a reason</option><option>Ordered by mistake</option><option>Changed mind</option><option>Delivery taking too long</option><option>Other</option></select><button type="button" [disabled]="saving()||!reason" (click)="requestCancellation(current.id)">{{ saving() ? 'Sending request...' : 'Request cancellation' }}</button>@if (cancelError()) { <p class="error" role="alert">{{ cancelError() }}</p> }</article> }

        @if (current.returnRequests?.length) { <article class="detail-card request-card"><div class="section-heading"><div><span class="eyebrow">RETURNS</span><h2>Return requests</h2></div><span class="request-count">{{ current.returnRequests?.length }}</span></div>@for (request of current.returnRequests || []; track request.requestId) { <div class="request-detail">@if (lineImages()[request.invoiceItemId]) { <img [src]="lineImages()[request.invoiceItemId]" [alt]="request.productName" /> }<div><strong>{{ request.productName }}</strong><p><span class="status-badge" [class]="requestClass(request.status)">{{ returnStatus(request.status) }}</span> · Quantity {{ request.quantity }}</p><p><b>Reason:</b> {{ request.reason }}</p>@if (request.explanation) { <p><b>Note:</b> {{ request.explanation }}</p> }@if (request.decisionNote) { <p class="decision-note">{{ request.decisionNote }}</p> }@if (request.decidedAtUtc) { <time>{{ request.decidedAtUtc | date:'mediumDate' }}</time> }</div></div> }</article> }

        <section class="return-items" aria-label="Return eligibility by product"><div class="section-heading"><div><span class="eyebrow">RETURNS</span><h2>Return an item</h2></div></div>@for (line of current.lines || []; track line.productId) { <article class="return-line"><div><strong>{{ line.productName }}</strong><p class="muted">@if (line.returnStatus==='NOT_ELIGIBLE'||line.isReturnable===false) { Non-returnable product } @else { {{ returnWindowLabel(line) }} }@if (line.returnDeadline) { · Return until {{ line.returnDeadline | date:'mediumDate' }} }</p></div>@if (line.returnStatus==='ELIGIBLE' && line.invoiceItemId && line.returnableQuantity > 0 && !line.returnRequestStatus) { <button type="button" class="secondary-button" (click)="openReturn(line)">Request return</button> } @if (returningLine()===line.invoiceItemId) { <div class="return-form"><label>Quantity<input type="number" min="0.01" [max]="line.returnableQuantity" [(ngModel)]="returnQuantity" /></label><label>Reason<select [(ngModel)]="returnReason"><option value="">Choose a reason</option><option>Damaged or defective</option><option>Wrong product</option><option>Not as described</option><option>Changed my mind</option><option>Other</option></select></label><label>Additional explanation<textarea [(ngModel)]="returnExplanation" maxlength="1000"></textarea></label><button type="button" [disabled]="returnSaving()||!returnReason" (click)="submitReturn(line)">{{ returnSaving() ? 'Submitting...' : 'Submit return request' }}</button><button type="button" class="secondary-button" (click)="closeReturn()">Cancel</button></div> }</article> }</section>
      }
    </section>
  `,
  styleUrl: './order-details.page.css',
})
export class OrderDetailsPhase3Page implements OnInit {
  storeKey=''; readonly order=signal<CustomerOrder|null>(null); readonly lineImages=signal<Record<string,string>>({}); readonly loading=signal(true); readonly saving=signal(false); readonly cancelError=signal(''); readonly returningLine=signal(''); readonly returnSaving=signal(false); reason=''; returnQuantity=1; returnReason=''; returnExplanation='';
  constructor(private readonly route:ActivatedRoute, private readonly data:StorefrontDataService, private readonly session:CustomerSessionService){}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';void this.load();}
  orderStatusLabel=(status:string|null|undefined)=>orderStatusLabel(status); paymentLabel=(status:string|null|undefined,method?:string|null)=>paymentLabel(status,method); codDueLabel=codDueLabel;
  groupStatusKey(status:string|null|undefined):string { const value=(status??'').trim().toUpperCase().replaceAll(' ','_'); return ({CONFIRMED:'PENDING',PACKED:'READY_FOR_DISPATCH','OUT_FOR_DELIVERY':'OUT_FOR_DELIVERY','DELIVERY_ATTENTION_REQUIRED':'DELIVERY_FAILED'} as Record<string,string>)[value]??value; }
  groupStatusLabel(status:string|null|undefined):string { const value=this.groupStatusKey(status); return ({PENDING:'Confirmed',PREPARING:'Preparing',READY_FOR_DISPATCH:'Packed',OUT_FOR_DELIVERY:'Out for delivery',DELIVERED:'Delivered',DELIVERY_FAILED:'Delivery attempt needs attention',CANCELLED:'Cancelled'} as Record<string,string>)[value]??status??'Delivery status unavailable'; }
  groupStatusClass(status:string|null|undefined):string { const value=this.groupStatusKey(status); return ['DELIVERED'].includes(value)?'success':['DELIVERY_FAILED','CANCELLED'].includes(value)?'danger':['OUT_FOR_DELIVERY','READY_FOR_DISPATCH'].includes(value)?'':'warning'; }
  isGroupException(status:string|null|undefined):boolean { return ['DELIVERY_FAILED','CANCELLED'].includes(this.groupStatusKey(status)); }
  finalDeliveryStepLabel(status:string|null|undefined):string { const value=this.groupStatusKey(status); return value==='CANCELLED'?'Delivery cancelled':value==='DELIVERY_FAILED'?'Delivery needs attention':'Delivered'; }
  isPaymentCollected(status:string|null|undefined):boolean { return ['PAYMENT SUCCESSFUL','PAID','CAPTURED'].includes((status??'').toUpperCase()); }
  refundStatusLabel(status:string):string { return ({'Refund review required':'Refund under review','Refund required':'Refund due','Refund in progress':'Refund in progress','Refunded':'Refund completed'} as Record<string,string>)[status]??status; }
  statusClass(status:string|null|undefined):string{const value=(status??'').toUpperCase();return value.includes('FAIL')||value.includes('CANCEL')?'danger':value.includes('PENDING')||value.includes('HELD')?'warning':'success';} isCancelled(status:string|null|undefined):boolean{return (status??'').toUpperCase()==='CANCELLED'||(status??'').toUpperCase()==='CANCELED';}
  requestClass(status:string|null|undefined):string{const value=(status??'').toUpperCase();return value.includes('REJECT')||value.includes('FAIL')?'danger':value.includes('PENDING')||value==='REQUESTED'?'warning':'success';}
  returnStatus(status:string):string{return ({REQUESTED:'Return requested',APPROVED:'Approved',RECEIVED:'Return received',COMPLETED:'Return completed',REJECTED:'Rejected'} as Record<string,string>)[status.toUpperCase()]??status;}
  cancellationStatus(status:string|undefined,orderStatus:string):string{if(orderStatus.toUpperCase()==='CANCELLED')return'Cancellation approved';return ({REQUESTED:'Pending review',APPROVING:'Approval in progress',REJECTED:'Request declined'} as Record<string,string>)[(status??'').toUpperCase()]??'Request submitted';}
  refundMessage(status:string,amount:number,at?:string):string{const value=status.toUpperCase();if(value==='REFUNDED')return`Refund completed: ${new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(amount)}.`;if(value.includes('PENDING')||value.includes('REQUIRED'))return`Refund of ${new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(amount)} is pending.`;return'No payment refund was required.';}
  selectValue(event:Event):string{return(event.target as HTMLSelectElement).value;} returnWindowLabel(line:CustomerOrderLine):string{return line.returnWindowDays===0?'Return window: same day after delivery':`Return window: ${line.returnWindowDays??0} days after delivery`;}
  openReturn(line:CustomerOrderLine):void{this.returningLine.set(line.invoiceItemId??'');this.returnQuantity=Math.min(1,line.returnableQuantity??1);this.returnReason='';this.returnExplanation='';} closeReturn():void{this.returningLine.set('');}
  async submitReturn(line:CustomerOrderLine):Promise<void>{if(!line.invoiceItemId||!this.returnReason||this.returnQuantity<=0)return;this.returnSaving.set(true);this.cancelError.set('');try{await this.data.requestReturn(this.storeKey,this.route.snapshot.paramMap.get('orderId')??'',line.invoiceItemId,this.returnQuantity,this.returnReason,this.returnExplanation);this.closeReturn();await this.load();}catch(error){const body=error instanceof HttpErrorResponse?error.error as{message?:string}|null:null;this.cancelError.set(body?.message??'Could not submit the return request. Please retry.');}finally{this.returnSaving.set(false);}}
  async requestCancellation(orderId:string):Promise<void>{if(!this.reason||!confirm('Request cancellation of this entire order?'))return;this.saving.set(true);this.cancelError.set('');try{await this.data.requestCancellation(this.storeKey,orderId,this.reason);await this.load();}catch(error){const body=error instanceof HttpErrorResponse?error.error as{message?:string}|null:null;this.cancelError.set(body?.message??'Could not request cancellation. Please retry.');}finally{this.saving.set(false);}}
  private async load():Promise<void>{try{await this.session.refresh(this.storeKey);if(!this.session.active())return;const current=await this.data.getOrder(this.storeKey,this.route.snapshot.paramMap.get('orderId')??'');this.order.set(current);if(current?.lines?.length){const products=await Promise.all(current.lines.map(line=>this.data.getProduct(this.storeKey,line.productId).catch(()=>null)));const images:Record<string,string>={};current.lines.forEach((line,index)=>{const product=products[index];const primary=product?.images?.find(image=>image.isPrimary)??product?.images?.[0];const url=primary?.thumbnailUrl??primary?.url??product?.imageUrl;if(url){images[line.productId]=url;if(line.invoiceItemId)images[line.invoiceItemId]=url;}});this.lineImages.set(images);}}finally{this.loading.set(false);}}
}
