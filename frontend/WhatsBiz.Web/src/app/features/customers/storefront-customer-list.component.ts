import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { StorefrontCustomerApiService, StorefrontCustomerDetail, StorefrontCustomerRow } from './storefront-customer-api.service';

@Component({
  selector: 'app-storefront-customer-list', standalone: true, imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <header><div><p class="eyebrow">STOREFRONT</p><h1>Storefront Customers</h1><p class="muted">Registered customers only. Anonymous visitor sessions are not listed.</p></div><button type="button" (click)="load()">Refresh</button></header>
      <div class="toolbar"><label>Search by name or mobile <input [(ngModel)]="search" (keyup.enter)="page=1;load()" placeholder="Search customers" /></label><button type="button" (click)="page=1;load()">Search</button></div>
      @if (error()) { <div class="error">{{error()}} <button type="button" (click)="load()">Retry</button></div> }
      @if (loading()) { <p class="state">Loading storefront customers…</p> } @else if (!items().length) { <p class="state">No registered storefront customers found.</p> } @else {
        <div class="table-wrap"><table><thead><tr><th>Customer</th><th>Mobile</th><th>Registered</th><th>Last login</th><th>Last visit</th><th>Orders</th><th>Total spent</th><th>Status</th><th></th></tr></thead><tbody>
          @for (item of items(); track item.customerId) { <tr><td>{{item.name}}</td><td>{{item.mobile || '—'}}</td><td>{{item.registeredOn | date:'mediumDate'}}</td><td>{{item.lastSuccessfulLogin ? (item.lastSuccessfulLogin | date:'medium') : '—'}}</td><td>{{item.lastVisit ? (item.lastVisit | date:'medium') : '—'}}</td><td>{{item.totalOrders}}</td><td>{{item.totalSpent | number:'1.2-2'}}</td><td>{{item.isActive ? 'Active' : 'Inactive'}}</td><td><button type="button" (click)="open(item.customerId)">View</button></td></tr> }
        </tbody></table></div>
        <nav class="pager"><button type="button" [disabled]="page<=1" (click)="page=page-1;load()">Previous</button><span>Page {{page}} of {{pageCount()}}</span><button type="button" [disabled]="page>=pageCount()" (click)="page=page+1;load()">Next</button></nav>
      }
      @if (detail(); as d) { <aside class="detail"><button class="close" type="button" (click)="detail.set(null)">Close</button><h2>{{d.customer.name}}</h2><p>{{d.customer.mobile || 'No mobile recorded'}} · {{d.customer.isActive ? 'Active' : 'Inactive'}}</p><p>{{d.customer.totalOrders}} storefront orders · {{d.customer.totalSpent | number:'1.2-2'}} total spent</p><h3>Order history</h3>@if (!d.orders.length){<p class="muted">No storefront orders.</p>}@else{<ul>@for(o of d.orders;track o.orderNumber){<li>{{o.orderNumber}} · {{o.placedOn|date:'mediumDate'}} · {{o.status}} · {{o.total|number:'1.2-2'}}</li>}</ul>}<h3>Recent activity</h3>@if (!d.recentActivity.length){<p class="muted">No signed-in activity.</p>}@else{<ul>@for(a of d.recentActivity;track $index){<li>{{a.eventType}} · {{a.occurredOn|date:'medium'}}</li>}</ul>}<h3>Return requests</h3>@if (!d.returnRequests.length){<p class="muted">No return requests.</p>}@else{<ul>@for(r of d.returnRequests;track $index){<li>{{r.productName}} · {{r.quantity}} · {{r.status}}</li>}</ul>}</aside> }
    </section>`,
  styles: [`:host{display:block}.page{padding:clamp(16px,3vw,32px);position:relative}header,.toolbar,.pager{display:flex;align-items:center;gap:12px;flex-wrap:wrap}header{justify-content:space-between}h1{margin:.2rem 0}.eyebrow{font-size:.75rem;letter-spacing:.12em}.muted{color:#64748b}.toolbar{margin:20px 0}label{display:flex;gap:8px;align-items:center}input{min-width:260px;padding:9px;border:1px solid #cbd5e1;border-radius:6px}button{padding:8px 12px;border:1px solid #94a3b8;border-radius:6px;background:white;cursor:pointer}button:disabled{opacity:.5;cursor:not-allowed}.error{padding:12px;background:#fee2e2;color:#991b1b;border-radius:8px}.state{padding:32px;text-align:center}.table-wrap{overflow:auto}table{width:100%;border-collapse:collapse;min-width:960px}th,td{text-align:left;padding:11px 10px;border-bottom:1px solid #e2e8f0;white-space:nowrap}.pager{justify-content:center;margin:18px 0}.detail{position:fixed;right:0;top:0;bottom:0;width:min(520px,100vw);padding:24px;background:white;box-shadow:-8px 0 30px #0f172a33;overflow:auto;z-index:10}.close{float:right}li{margin:8px 0}@media(max-width:600px){label{width:100%;align-items:stretch;flex-direction:column}input{min-width:0;width:100%}.detail{padding:18px}}`],
})
export class StorefrontCustomerListComponent {
  readonly items = signal<StorefrontCustomerRow[]>([]); readonly detail = signal<StorefrontCustomerDetail | null>(null); readonly loading = signal(false); readonly error = signal('');
  search = ''; page = 1; readonly size = 20; sortBy = 'name'; descending = false; total = 0;
  constructor(private readonly api: StorefrontCustomerApiService) { this.load(); }
  pageCount() { return Math.max(1, Math.ceil(this.total / this.size)); }
  load() { this.loading.set(true); this.error.set(''); this.api.search(this.search.trim(), this.sortBy, this.descending, this.page, this.size).pipe(finalize(() => this.loading.set(false))).subscribe({ next: x => { this.items.set(x.items); this.total = x.totalCount; }, error: () => this.error.set('Unable to load storefront customers. Try again.') }); }
  open(id: string) { this.api.get(id).subscribe({ next: x => this.detail.set(x), error: () => this.error.set('Unable to load this customer.') }); }
}
