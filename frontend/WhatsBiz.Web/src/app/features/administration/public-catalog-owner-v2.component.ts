import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { FeatureService, FeatureTenantSummary } from '../../core/services/feature.service';
import { ProductPublicationPanelComponent } from '../products/product-publication-panel.component';
import { BusinessTypesComponent } from './business-types.component';

interface QueueRow {
  productId:string; tenantId:string; storeName:string; productName:string; productCode:string;
  categoryName:string; sellingPrice:number; mrp:number; status:string; submittedAt:string|null; primaryImageId:string|null; contentVersion:string; reviewVersion:string|null;
}
interface QueueResponse { items:QueueRow[]; total:number; pendingTotal:number; }
interface BulkReviewResult {productId:string;status:string;message?:string;productName?:string;}
type DirectoryStatus='PENDING'|'PUBLISHED'|'REJECTED'|'UNPUBLISHED';
interface DirectoryRow {
  tenantId:string; name:string; tenantKey:string; businessType:string; shortDescription:string; optedIn:boolean;
  ownerApproved:boolean; isActive:boolean; version:string; status:DirectoryStatus; logoUrl:string|null;
}

@Component({
  standalone:true,
  imports:[FormsModule,DatePipe,CurrencyPipe,NgTemplateOutlet,ProductPublicationPanelComponent,BusinessTypesComponent],
  templateUrl:'./public-catalog-owner-v2.component.html',
  styleUrl:'./public-catalog-owner-v2.component.css',
})
export class PublicCatalogOwnerV2Component implements OnDestroy {
  private readonly http=inject(HttpClient);
  private readonly features=inject(FeatureService);
  private productRequest?:Subscription;
  private directoryRequest?:Subscription;
  private imageRequests=new Subscription();

  readonly activeTab=signal<'products'|'directory'>('products');
  readonly tenants=signal<FeatureTenantSummary[]>([]);
  readonly tenantError=signal('');
  readonly rows=signal<QueueRow[]>([]);
  readonly total=signal(0);
  readonly pendingTotal=signal(0);
  readonly productBusy=signal(false);
  readonly productError=signal('');
  readonly selected=signal<QueueRow|null>(null);
  readonly selectedForBulk=signal<QueueRow[]>([]);
  readonly bulkAction=signal<'APPROVE'|'REJECT'|''>('');
  readonly bulkReason=signal('');
  readonly bulkBusy=signal(false);
  readonly bulkResult=signal('');
  readonly bulkItemResults=signal<BulkReviewResult[]>([]);
  readonly legacyMessage=signal('');
  readonly legacyBusy=signal(false);
  readonly productImages=signal<Record<string,string>>({});
  readonly directory=signal<DirectoryRow[]>([]);
  readonly directoryBusy=signal(false);
  readonly directoryError=signal('');
  readonly directoryActionTenant=signal('');
  readonly failedLogos=signal<Set<string>>(new Set());

  tenant=''; status='PENDING_APPROVAL'; since=''; until=''; category=''; page=1;
  directorySearch=''; directoryStatus:DirectoryStatus|'ALL'='ALL'; directoryReason:Record<string,string>={};
  readonly statuses=['DRAFT','PENDING_APPROVAL','APPROVED','REJECTED','CHANGES_PENDING','SUSPENDED','LEGACY'];
  directoryVisible(){const search=this.directorySearch.trim().toLocaleLowerCase();return this.directory().filter(row=>(this.directoryStatus==='ALL'||row.status===this.directoryStatus)&&(!search||`${row.name} ${row.businessType} ${row.shortDescription}`.toLocaleLowerCase().includes(search)));}
  directoryPending(){return this.directoryVisible().filter(x=>x.status==='PENDING');}
  directoryPublished(){return this.directoryVisible().filter(x=>x.status==='PUBLISHED');}
  directoryInactive(){return this.directoryVisible().filter(x=>x.status==='REJECTED'||x.status==='UNPUBLISHED');}

  constructor(){
    this.features.tenants().subscribe({next:r=>this.tenants.set(r),error:()=>this.tenantError.set('Retailer filters could not be loaded. Retry the page after your session refreshes.')});
    this.loadProducts();
  }
  selectTab(tab:'products'|'directory'){this.activeTab.set(tab);if(tab==='directory'&&!this.directory().length&&!this.directoryBusy())this.loadDirectory();}
  loadProducts(){
    this.productRequest?.unsubscribe();this.selectedForBulk.set([]);this.bulkAction.set('');this.productBusy.set(true);this.productError.set('');
    let params=new HttpParams().set('page',this.page);if(this.tenant)params=params.set('tenant',this.tenant);if(this.status)params=params.set('status',this.status);if(this.since)params=params.set('since',`${this.since}T00:00:00Z`);if(this.until)params=params.set('until',`${this.until}T23:59:59.999Z`);if(this.category.trim())params=params.set('category',this.category.trim());
    this.productRequest=this.http.get<QueueResponse>('/api/public-catalog/owner/products',{params}).pipe(finalize(()=>this.productBusy.set(false))).subscribe({next:r=>{this.rows.set(r.items);this.total.set(r.total);this.pendingTotal.set(r.pendingTotal);this.loadProductImages(r.items);},error:()=>this.productError.set('Product approvals could not be loaded. Retry this tab.')});
  }
  applyProductFilters(){this.page=1;this.selected.set(null);this.loadProducts();}
  clearProductFilters(){this.tenant='';this.status='';this.since='';this.until='';this.category='';this.page=1;this.selected.set(null);this.selectedForBulk.set([]);this.bulkAction.set('');this.legacyMessage.set('');this.loadProducts();}
  productEmptyMessage(){return this.status==='PENDING_APPROVAL'&&!this.tenant&&!this.since&&!this.until?'No pending product submissions.':'No products match the active filters.';}
  movePage(delta:number){this.page+=delta;this.selected.set(null);this.loadProducts();}
  canSelectForBulk(product:QueueRow){return !!this.tenant&&product.tenantId===this.tenant&&this.status==='PENDING_APPROVAL'&&product.status==='PENDING_APPROVAL'&&(!this.selectedForBulk().length||this.selectedForBulk()[0].tenantId===product.tenantId);}
  isSelectedForBulk(product:QueueRow){return this.selectedForBulk().some(item=>item.productId===product.productId);}
  toggleBulkProduct(product:QueueRow,checked:boolean){this.selectedForBulk.update(rows=>checked?[...rows.filter(x=>x.productId!==product.productId),product]:rows.filter(x=>x.productId!==product.productId));this.bulkResult.set('');}
  selectVisiblePending(){if(!this.tenant){this.productError.set('Choose one retailer before selecting products for bulk review.');return;}this.selectedForBulk.set(this.rows().filter(row=>row.tenantId===this.tenant&&row.status==='PENDING_APPROVAL'));this.bulkResult.set('');}
  visiblePendingSelected(){const eligible=this.rows().filter(row=>this.canSelectForBulk(row));return eligible.length>0&&eligible.every(row=>this.isSelectedForBulk(row));}
  toggleVisiblePending(checked:boolean){const eligible=this.rows().filter(row=>this.canSelectForBulk(row));const ids=new Set(eligible.map(row=>row.productId));this.selectedForBulk.update(current=>checked?[...current.filter(row=>!ids.has(row.productId)),...eligible]:current.filter(row=>!ids.has(row.productId)));this.bulkResult.set('');}
  beginBulkReview(action:'APPROVE'|'REJECT'){if(!this.selectedForBulk().length)return;this.bulkAction.set(action);this.bulkReason.set('');this.bulkResult.set('');}
  confirmBulkReview(){const items=this.selectedForBulk();const action=this.bulkAction();const reason=this.bulkReason().trim();if(!items.length||!action||this.bulkBusy()||(action==='REJECT'&&!reason))return;this.bulkBusy.set(true);this.http.post<BulkReviewResult[]>(`/api/public-catalog/owner/tenants/${items[0].tenantId}/products/bulk`,{items:items.map(x=>({productId:x.productId,contentVersion:x.contentVersion,reviewVersion:x.reviewVersion})),action,reason:reason||null}).pipe(finalize(()=>this.bulkBusy.set(false))).subscribe({next:results=>{const passed=results.filter(x=>x.status===(action==='APPROVE'?'APPROVED':'REJECTED')).length;const conflicts=results.filter(x=>x.status==='CONFLICT').length;const failures=results.length-passed-conflicts;this.bulkResult.set(`${passed} ${action==='APPROVE'?'approved':'rejected'}; ${conflicts} conflict(s); ${failures} failed.`);this.bulkItemResults.set(results.map(result=>({...result,productName:items.find(item=>item.productId===result.productId)?.productName??result.productId})));this.selectedForBulk.set([]);this.bulkAction.set('');this.loadProducts();},error:()=>this.productError.set('Bulk review failed. Reload the queue and confirm each product revision before retrying.')});}
  loadLegacy(){if(!this.tenant||this.legacyBusy())return;this.legacyBusy.set(true);this.legacyMessage.set('');this.http.get<{legacyCount:number;message:string}>(`/api/public-catalog/owner/tenants/${this.tenant}/legacy-plan`).pipe(finalize(()=>this.legacyBusy.set(false))).subscribe({next:r=>this.legacyMessage.set(`${r.legacyCount} grandfathered products. ${r.message}`),error:()=>this.legacyMessage.set('Legacy product counts could not be loaded. Retry without changing any products.')});}
  loadDirectory(){this.directoryRequest?.unsubscribe();this.directoryBusy.set(true);this.directoryError.set('');this.directoryRequest=this.http.get<DirectoryRow[]>('/api/public-catalog/owner/directory').pipe(finalize(()=>this.directoryBusy.set(false))).subscribe({next:r=>{this.directory.set(r);this.failedLogos.set(new Set());},error:()=>this.directoryError.set('Store directory requests could not be loaded. Retry this tab.')});}
  clearDirectoryFilters(){this.directorySearch='';this.directoryStatus='ALL';}
  directoryHasFilters(){return!!this.directorySearch.trim()||this.directoryStatus!=='ALL';}
  directoryEmptyMessage(){return this.directory().length===0?'No retailer has opted in. Retailers must complete business information and request publication first.':'No directory requests match the active filters.';}
  logoFailed(tenantId:string){this.failedLogos.update(current=>new Set(current).add(tenantId));}
  initials(name:string){return name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join('').toUpperCase()||'KS';}
  decideDirectory(row:DirectoryRow,action:'APPROVE'|'REJECT'|'UNPUBLISH'){
    if(this.directoryActionTenant())return;const reason=(this.directoryReason[row.tenantId]??'').trim();if(action==='REJECT'&&!reason){this.directoryError.set('Enter a reason before rejecting a directory request.');return;}
    this.directoryActionTenant.set(row.tenantId);this.directoryError.set('');this.http.post(`/api/public-catalog/owner/directory/${row.tenantId}`,{publish:action==='APPROVE',version:row.version,action,reason:reason||null}).pipe(finalize(()=>this.directoryActionTenant.set(''))).subscribe({next:()=>{delete this.directoryReason[row.tenantId];this.loadDirectory();},error:err=>this.directoryError.set(err.error?.message||'The directory decision could not be saved. Reload before retrying.')});
  }
  private loadProductImages(rows:QueueRow[]){this.imageRequests.unsubscribe();this.imageRequests=new Subscription();for(const url of Object.values(this.productImages()))URL.revokeObjectURL(url);this.productImages.set({});for(const row of rows.filter(x=>x.primaryImageId)){const url=`/api/public-catalog/owner/tenants/${row.tenantId}/products/${row.productId}/images/${row.primaryImageId}`;this.imageRequests.add(this.http.get(url,{responseType:'blob'}).subscribe({next:blob=>this.productImages.update(current=>({...current,[row.productId]:URL.createObjectURL(blob)}))}));}}
  ngOnDestroy(){this.productRequest?.unsubscribe();this.directoryRequest?.unsubscribe();this.imageRequests.unsubscribe();for(const url of Object.values(this.productImages()))URL.revokeObjectURL(url);}
}
