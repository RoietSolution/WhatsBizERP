/* Replaced by the focused tabbed implementation below; retained as a compatibility module during V72 rollout.
import { Component, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { DatePipe, CurrencyPipe } from '@angular/common';
import { FeatureService, FeatureTenantSummary } from '../../core/services/feature.service';
import { ProductPublicationPanelComponent } from '../products/product-publication-panel.component';
import { finalize } from 'rxjs';
import { BusinessTypesComponent } from './business-types.component';

interface QueueRow{productId:string;tenantId:string;storeName:string;productName:string;productCode:string;categoryName:string;sellingPrice:number;mrp:number;status:string;submittedAt:string|null;}
interface DirectoryRow{tenantId:string;name:string;businessType:string;shortDescription:string;optedIn:boolean;ownerApproved:boolean;isActive:boolean;version:string;}
@Component({standalone:true,imports:[FormsModule,DatePipe,CurrencyPipe,ProductPublicationPanelComponent,BusinessTypesComponent],
 template:`<section class="catalog-panel"><header><div><p>Application Owner · Mandatory public catalog governance</p><h1>Product review & store directory</h1></div><button (click)="load()" [disabled]="busy()">Refresh</button></header>
 <app-business-types/><p>Approval cannot be bypassed by a plan or entitlement. Existing catalog products remain explicitly grandfathered until individually submitted or materially changed.</p>
 @if(error()){<p class="error" role="alert">{{error()}}</p>}
 <section class="filters"><label>Retailer<select [(ngModel)]="tenant"><option value="">All retailers</option>@for(t of tenants();track t.tenantId){<option [value]="t.tenantId">{{t.displayName||t.tenantName}}</option>}</select></label>
 <label>Approval status<select [(ngModel)]="status"><option value="">All statuses</option>@for(s of statuses;track s){<option [value]="s">{{s.replaceAll('_',' ')}}</option>}</select></label>
 <label>Submitted from<input type="date" [(ngModel)]="since"></label><label>Submitted through<input type="date" [(ngModel)]="until"></label></section>
 <div class="actions"><button class="primary" (click)="page=1;load()" [disabled]="busy()">Apply filters</button><button (click)="legacy()" [disabled]="!tenant||busy()">Dry-run legacy adoption counts</button></div>
 @if(legacyMessage()){<p class="notice" role="status">{{legacyMessage()}}</p>}
 <h2>Products · {{total()}}</h2><div class="queue">@for(p of rows();track p.productId){<article><div><h3>{{p.productName}}</h3><p>{{p.storeName}} · {{p.productCode}} · {{p.categoryName}}</p><p>{{p.sellingPrice|currency:'INR'}} · {{p.status.replaceAll('_',' ')}} · {{p.submittedAt?(p.submittedAt|date:'medium'):'Not submitted'}}</p></div><button (click)="selected.set(p)">Review</button></article>}@empty{<p>No products match this review filter.</p>}</div>
 <div class="actions"><button (click)="page=page-1;load()" [disabled]="page<=1||busy()">Previous</button><span>Page {{page}}</span><button (click)="page=page+1;load()" [disabled]="page*30>=total()||busy()">Next</button></div>
 @if(selected();as p){<button (click)="selected.set(null);load()">Close review & refresh queue</button><app-product-publication-panel [productId]="p.productId" [ownerTenant]="p.tenantId"/>}
 <h2>Directory publication</h2><p>Only active, opted-in and owner-published stores are listed. Editing a listing requires a new owner decision.</p><div class="queue">@for(e of directory();track e.tenantId){<article><div><h3>{{e.name}}</h3><p>{{e.businessType}} · {{e.shortDescription}}</p><p>{{e.optedIn?'Opted in':'Not opted in'}} · {{e.ownerApproved?'Published':'Unpublished'}} · {{e.isActive?'Active':'Inactive'}}</p></div><button (click)="publish(e)" [disabled]="busy()||(!e.ownerApproved&&(!e.optedIn||!e.isActive))">{{e.ownerApproved?'Unpublish':'Publish approved listing'}}</button></article>}@empty{<p>No retailers have configured directory entries.</p>}</div></section>`,
 styleUrl:'../products/public-catalog.css'})
export class PublicCatalogOwnerComponent{
 private http=inject(HttpClient);private features=inject(FeatureService);readonly tenants=signal<FeatureTenantSummary[]>([]);readonly rows=signal<QueueRow[]>([]);readonly directory=signal<DirectoryRow[]>([]);readonly total=signal(0);readonly busy=signal(false);readonly error=signal('');readonly selected=signal<QueueRow|null>(null);readonly legacyMessage=signal('');
 tenant='';status='PENDING_APPROVAL';since='';until='';page=1;readonly statuses=['DRAFT','PENDING_APPROVAL','APPROVED','REJECTED','CHANGES_PENDING','SUSPENDED','LEGACY'];
 constructor(){this.features.tenants().subscribe({next:r=>this.tenants.set(r),error:()=>this.error.set('Retailers could not be loaded.')});this.load();}
 load(){this.busy.set(true);this.error.set('');let params=new HttpParams().set('page',this.page);if(this.tenant)params=params.set('tenant',this.tenant);if(this.status)params=params.set('status',this.status);if(this.since)params=params.set('since',`${this.since}T00:00:00Z`);if(this.until)params=params.set('until',`${this.until}T23:59:59Z`);
 this.http.get<{items:QueueRow[];total:number}>('/api/public-catalog/owner/products',{params}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:r=>{this.rows.set(r.items);this.total.set(r.total);},error:()=>this.error.set('Review queue could not be loaded. Please retry.')});this.http.get<DirectoryRow[]>('/api/public-catalog/owner/directory').subscribe({next:r=>this.directory.set(r),error:()=>this.error.set('Directory publication settings could not be loaded.')});}
 legacy(){if(!this.tenant)return;this.http.get<{legacyCount:number;message:string}>(`/api/public-catalog/owner/tenants/${this.tenant}/legacy-plan`).subscribe({next:r=>this.legacyMessage.set(`${r.legacyCount} legacy products. ${r.message}`),error:()=>this.error.set('Legacy adoption counts could not be loaded.')});}
 publish(e:DirectoryRow){if(this.busy())return;this.busy.set(true);this.http.post(`/api/public-catalog/owner/directory/${e.tenantId}`,{publish:!e.ownerApproved,version:e.version}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:()=>this.load(),error:err=>this.error.set(err.error?.message||'Publication changed or could not be saved. Refresh before retrying.')});}
}
*/
export { PublicCatalogOwnerV2Component as PublicCatalogOwnerComponent } from './public-catalog-owner-v2.component';
