import { Component, Input, OnChanges, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription, finalize, switchMap } from 'rxjs';
import { CurrentUserService } from '../../core/services/current-user.service';

export interface ReviewProduct {
  productId:string;tenantId:string;productName:string;productCode:string;categoryName:string;unitName:string;
  status:string;contentVersion:string;reviewVersion:string|null;sellingPrice:number;mrp:number;gstPercentage:number;gstPricingType:string;
  shortDescription:string|null;longDescription:string|null;isActive:boolean;isWhatsAppVisible:boolean;
  specifications:{name:string;value:string}[];images:{productImageId:string;fileName:string;isPrimary:boolean}[];
  stock:{warehouseId:string;warehouseName:string;quantityOnHand:number;quantityReserved:number}[];
  createdOn:string;createdBy:string;modifiedOn:string|null;modifiedBy:string|null;
  submittedAt:string|null;decidedAt:string|null;reviewer:string|null;reason:string|null;
  directScheduleAssignments:number;scheduleNote:string;
  history:{action:string;actor:string;reason:string|null;occurredAt:string}[];
  modifications:{userName:string|null;action:string;httpMethod:string|null;succeeded:boolean;occurredOn:string}[];
}
@Component({
  selector:'app-product-publication-panel',standalone:true,imports:[FormsModule,CurrencyPipe,DatePipe],
  template:`<section class="catalog-panel"><header><div><p>Storefront publication</p><h2>Approval & public product details</h2></div><button type="button" (click)="load()" [disabled]="busy()">Refresh</button></header>
    @if(error()){<p role="alert" class="error">{{error()}}</p>}@if(message()){<p role="status">{{message()}}</p>}
    @if(item();as p){<div class="status"><strong>{{p.status.replaceAll('_',' ')}}</strong><span>{{p.isActive?'Active':'Inactive'}} · {{p.isWhatsAppVisible?'Storefront visibility on':'Storefront visibility off'}}</span></div>
    @if(p.status==='LEGACY'){<p class="notice">Existing catalog baseline — not owner approved. Submit this product to adopt approval review. Submission temporarily removes it from public sale until approved.</p>}
    @if(p.status==='CHANGES_PENDING'){<p class="notice">Public content changed. The product is temporarily unpublished until its current content is submitted and approved.</p>}
    <div class="detail-grid"><section><h3>Pricing / GST</h3><dl><dt>Selling price</dt><dd>{{p.sellingPrice|currency:'INR'}}</dd><dt>MRP</dt><dd>{{p.mrp|currency:'INR'}}</dd><dt>GST</dt><dd>{{p.gstPercentage}}% · {{p.gstPricingType}}</dd><dt>Category / unit</dt><dd>{{p.categoryName}} / {{p.unitName}}</dd></dl></section>
    <section><h3>Approval record</h3><dl><dt>Submitted</dt><dd>{{p.submittedAt?(p.submittedAt|date:'medium'):'Not submitted'}}</dd><dt>Decision</dt><dd>{{p.decidedAt?(p.decidedAt|date:'medium'):'No decision'}}</dd><dt>Reviewer</dt><dd>{{p.reviewer||'No reviewer'}}</dd></dl>@if(p.reason){<p class="notice">{{p.reason}}</p>}</section></div>
    <section><h3>Images</h3><div class="gallery">@for(image of imageUrls();track image.url){<img [src]="image.url" [alt]="image.name">}@empty{<p>No active product images.</p>}</div></section>
    <section><h3>Description & specifications</h3><div [innerHTML]="p.shortDescription"></div><div [innerHTML]="p.longDescription"></div><dl>@for(s of p.specifications;track s.name){<dt>{{s.name}}</dt><dd>{{s.value}}</dd>}</dl></section>
    <section><h3>Stock balances</h3><div class="stock">@for(s of p.stock;track $index){<div><strong>{{s.warehouseName}}</strong><span>On hand: {{s.quantityOnHand}} · Reserved: {{s.quantityReserved}}</span></div>}@empty{<p>No inventory balances recorded.</p>}</div></section>
    <section><h3>Scheduling</h3><p>{{p.directScheduleAssignments}} direct schedule assignments. {{p.scheduleNote}}</p></section>
    <div class="actions">@if(!ownerTenant&&canEdit()&&['DRAFT','REJECTED','CHANGES_PENDING','LEGACY'].includes(p.status)){<button class="primary" (click)="act('SUBMIT')" [disabled]="busy()">Submit for approval</button>}
    @if(ownerTenant){<label>Decision reason<textarea [(ngModel)]="reason" maxlength="1000" rows="3"></textarea></label>
    @if(['SUSPENDED','LEGACY'].includes(p.status)){<button (click)="act('SUBMIT')" [disabled]="busy()">Submit current content for review</button>}
    @if(p.status==='PENDING_APPROVAL'){<button class="primary" (click)="act('APPROVE')" [disabled]="busy()">Approve current revision</button><button (click)="act('REJECT')" [disabled]="busy()||!reason.trim()">Reject</button>}
    @if(['APPROVED','LEGACY','CHANGES_PENDING'].includes(p.status)){<button (click)="act('SUSPEND')" [disabled]="busy()||!reason.trim()">Suspend / Revoke</button>}}</div>
    <section><h3>Submission & decision history</h3><ol>@for(h of p.history;track $index){<li><strong>{{h.action}}</strong> · {{h.occurredAt|date:'medium'}} · {{h.actor}} @if(h.reason){<p>{{h.reason}}</p>}</li>}@empty{<li>No review actions recorded.</li>}</ol></section>
    <section><h3>Product modification history</h3><ol>@for(h of p.modifications;track $index){<li>{{h.action}} · {{h.occurredOn|date:'medium'}} · {{h.userName||'system'}} · {{h.succeeded?'Succeeded':'Failed'}}</li>}@empty{<li>No audited product modifications recorded.</li>}</ol></section>
    <footer>Created {{p.createdOn|date:'medium'}} by {{p.createdBy||'system'}}@if(p.modifiedOn){ · Modified {{p.modifiedOn|date:'medium'}} by {{p.modifiedBy||'system'}}}. Stock changes do not require approval.</footer>
    }@else if(busy()){<p role="status">Loading publication details…</p>}</section>`,
  styleUrl:'./public-catalog.css'
})
export class ProductPublicationPanelComponent implements OnChanges,OnDestroy {
  @Input({required:true}) productId='';@Input() ownerTenant='';
  private http=inject(HttpClient);private current=inject(CurrentUserService);private request?:Subscription;private actionRequest?:Subscription;private downloads=new Subscription();private generation=0;
  readonly item=signal<ReviewProduct|null>(null);readonly busy=signal(false);readonly error=signal('');readonly message=signal('');readonly imageUrls=signal<{url:string;name:string}[]>([]);reason='';
  canEdit(){return this.current.user()?.permissions.includes('product.edit')??false;}
  private endpoint(){return this.ownerTenant?`/api/public-catalog/owner/tenants/${this.ownerTenant}/products/${this.productId}`:`/api/public-catalog/products/${this.productId}`;}
  ngOnChanges(){this.actionRequest?.unsubscribe();this.load();}
  load(){if(!this.productId)return;this.request?.unsubscribe();this.clearImages();const generation=++this.generation;this.item.set(null);this.busy.set(true);this.error.set('');this.request=this.http.get<ReviewProduct>(this.endpoint()).pipe(finalize(()=>this.busy.set(false))).subscribe({next:p=>{if(generation!==this.generation)return;this.item.set(p);this.loadImages(p,generation);},error:e=>this.error.set(e.error?.message||'Publication data could not be loaded. Refresh to retry.')});}
  act(action:string){const p=this.item();if(!p||this.busy())return;const endpoint=this.endpoint();const generation=this.generation;this.busy.set(true);this.error.set('');this.message.set('');this.actionRequest=this.http.post<ReviewProduct>(endpoint+(this.ownerTenant?'':'/submit'),{action,contentVersion:p.contentVersion,reviewVersion:p.reviewVersion,reason:this.reason}).pipe(switchMap(()=>this.http.get<ReviewProduct>(endpoint)),finalize(()=>this.busy.set(false))).subscribe({next:saved=>{if(generation!==this.generation)return;this.reason='';this.item.set(saved);this.clearImages();this.loadImages(saved,generation);this.message.set('Saved. Showing persisted approval status.');},error:e=>this.error.set(e.error?.message||'The review could not be saved or reloaded. Reload before retrying.')});}
  private loadImages(p:ReviewProduct,generation:number){for(const i of p.images){const url=this.ownerTenant?`${this.endpoint()}/images/${i.productImageId}`:`/api/products/${p.productId}/images/${i.productImageId}`;this.downloads.add(this.http.get(url,{responseType:'blob'}).subscribe({next:b=>{if(generation===this.generation)this.imageUrls.update(a=>[...a,{url:URL.createObjectURL(b),name:i.fileName}]);},error:()=>this.error.set('One or more product images could not be loaded.')}));}}
  private clearImages(){this.downloads.unsubscribe();this.downloads=new Subscription();this.imageUrls().forEach(i=>URL.revokeObjectURL(i.url));this.imageUrls.set([]);}
  ngOnDestroy(){this.generation++;this.request?.unsubscribe();this.actionRequest?.unsubscribe();this.clearImages();}
}
