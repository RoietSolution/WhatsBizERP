import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { StorefrontBlacklistEntry, StorefrontCustomerApiService, StorefrontCustomerDetail, StorefrontCustomerRow } from './storefront-customer-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FeatureService } from '../../core/services/feature.service';

@Component({
  selector: 'app-storefront-customer-list-integrated',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <header class="header"><div><p class="eyebrow">STOREFRONT</p><h1>Storefront Customers</h1><p class="muted">Registered customers only; anonymous sessions are not listed.</p></div><button type="button" (click)="load()">Refresh</button></header>
      <form class="toolbar" (submit)="searchPage($event)"><label>Search by name or mobile<input [(ngModel)]="search" name="search" placeholder="Search customers" /></label><button type="submit">Search</button></form>
      @if (error()) { <div class="error" role="alert">{{ error() }} <button type="button" (click)="load()">Retry</button></div> }
      @if (loading()) { <p class="state" role="status">Loading storefront customers...</p> }
      @else if (!items().length) { <p class="state">No registered storefront customers found.</p> }
      @else {
        <div class="table-wrap"><table><thead><tr><th>Customer</th><th>Mobile</th><th>Registered</th><th>Last login</th><th>Last visit</th><th>Orders</th><th>Total spent</th><th>Status</th><th></th></tr></thead><tbody>
          @for (item of items(); track item.customerId) { <tr><td>{{ item.name }}</td><td>{{ item.mobile || '—' }}</td><td>{{ item.registeredOn | date:'mediumDate' }}</td><td>{{ item.lastSuccessfulLogin ? (item.lastSuccessfulLogin | date:'medium') : '—' }}</td><td>{{ item.lastVisit ? (item.lastVisit | date:'medium') : '—' }}</td><td>{{ item.totalOrders }}</td><td>{{ item.totalSpent | number:'1.2-2' }}</td><td>{{ item.isActive ? 'Active' : 'Inactive' }}</td><td><button type="button" (click)="open(item.customerId)">View</button></td></tr> }
        </tbody></table></div>
        <nav class="pager" aria-label="Customer pages"><button type="button" [disabled]="page <= 1" (click)="move(-1)">Previous</button><span>Page {{ page }} of {{ pageCount() }}</span><button type="button" [disabled]="page >= pageCount()" (click)="move(1)">Next</button></nav>
      }
      @if (detail(); as d) {
        <aside class="detail" aria-label="Customer details"><button class="close" type="button" (click)="detail.set(null)">Close</button><h2>{{ d.customer.name }}</h2><p>{{ d.customer.mobile || 'No mobile recorded' }} · {{ d.customer.isActive ? 'Active' : 'Inactive' }}</p><p>{{ d.customer.totalOrders }} storefront orders · {{ d.customer.totalSpent | number:'1.2-2' }} total spent</p>
          <h3>Order history</h3>@if (!d.orders.length) { <p class="muted">No storefront orders.</p> } @else { <ul>@for (order of d.orders; track order.orderNumber) { <li>{{ order.orderNumber }} · {{ order.placedOn | date:'mediumDate' }} · {{ order.status }} · {{ order.total | number:'1.2-2' }}</li> }</ul> }
          <h3>Visits</h3><ng-container *ngTemplateOutlet="activityList; context: { $implicit: activityOf(d, ['STORE_VISIT', 'CUSTOMER_SIGN_IN']) }"></ng-container>
          <h3>Viewed products</h3><ng-container *ngTemplateOutlet="activityList; context: { $implicit: activityOf(d, ['PRODUCT_VIEW']) }"></ng-container>
          <h3>Cart activity</h3><ng-container *ngTemplateOutlet="activityList; context: { $implicit: activityOf(d, ['ADD_TO_CART', 'REMOVE_FROM_CART']) }"></ng-container>
          <h3>Wishlist</h3>@if (!d.wishlist?.length) { <p class="muted">No wishlist items.</p> } @else { <ul>@for (item of d.wishlist; track item.productId) { <li>{{ item.productName }} · {{ item.sellingPrice | number:'1.2-2' }} · {{ item.savedAt | date:'mediumDate' }}</li> }</ul> }
          <h3>Search history</h3>@if (!d.searchHistory?.length) { <p class="muted">No search history.</p> } @else { <ul>@for (search of d.searchHistory; track $index) { <li>{{ search.keyword }} · {{ search.outcome }} · {{ search.createdAtUtc | date:'medium' }}</li> }</ul> }
          <h3>Return requests</h3>@if (!d.returnRequests.length) { <p class="muted">No return requests.</p> } @else { <ul>@for (request of d.returnRequests; track $index) { <li>{{ request.productName }} · {{ request.quantity }} · {{ request.status }}</li> }</ul> }
          <ng-template #activityList let-rows><p class="muted" *ngIf="!rows.length">No activity recorded.</p><ul *ngIf="rows.length"><li *ngFor="let activity of rows">{{ activity.eventType }}{{ activity.productName ? ' · ' + activity.productName : '' }} · {{ activity.occurredOn | date:'medium' }}</li></ul></ng-template>
        </aside>
      }
    </section>
  `,
  styles: [`:host{display:block}.page{padding:clamp(16px,3vw,32px);position:relative}.header,.toolbar,.pager{display:flex;align-items:center;gap:12px;flex-wrap:wrap}.header{justify-content:space-between}h1{margin:.2rem 0}.eyebrow{font-size:.75rem;letter-spacing:.12em}.muted{color:#64748b}.toolbar{margin:20px 0}label{display:flex;gap:8px;align-items:center}input{min-width:260px;padding:9px;border:1px solid #cbd5e1;border-radius:6px}button{padding:8px 12px;border:1px solid #94a3b8;border-radius:6px;background:white;cursor:pointer}button:disabled{opacity:.5;cursor:not-allowed}.error{padding:12px;background:#fee2e2;color:#991b1b;border-radius:8px}.state{padding:32px;text-align:center}.table-wrap{overflow:auto}table{width:100%;border-collapse:collapse;min-width:960px}th,td{text-align:left;padding:11px 10px;border-bottom:1px solid #e2e8f0;white-space:nowrap}.pager{justify-content:center;margin:18px 0}.detail{position:fixed;right:0;top:0;bottom:0;width:min(520px,100vw);padding:24px;background:white;box-shadow:-8px 0 30px #0f172a33;overflow:auto;z-index:10}.close{float:right}li{margin:8px 0}@media(max-width:600px){label{width:100%;align-items:stretch;flex-direction:column}input{min-width:0;width:100%}.detail{padding:18px}}`],
})
export class StorefrontCustomerListIntegratedComponent {
  readonly items = signal<StorefrontCustomerRow[]>([]);
  readonly detail = signal<StorefrontCustomerDetail | null>(null);
  readonly blacklist = signal<StorefrontBlacklistEntry[]>([]);
  readonly blacklistLoading = signal(false);
  blacklistReason = '';
  readonly loading = signal(false);
  readonly error = signal('');
  search = ''; page = 1; readonly size = 20; readonly sortBy = 'name'; readonly descending = false; total = 0;
  get canManageBlacklist(): boolean { return this.permissions.has('customer.blacklist.manage') && this.features.hasFeature('STOREFRONT_CUSTOMER_BLACKLIST'); }
  constructor(private readonly api: StorefrontCustomerApiService, private readonly permissions: PermissionService, private readonly features: FeatureService) { this.load(); }
  pageCount(): number { return Math.max(1, Math.ceil(this.total / this.size)); }
  searchPage(event: Event): void { event.preventDefault(); this.page = 1; this.load(); }
  move(delta: number): void { this.page += delta; this.load(); }
  load(): void { this.loading.set(true); this.error.set(''); this.api.search(this.search.trim(), this.sortBy, this.descending, this.page, this.size).pipe(finalize(() => this.loading.set(false))).subscribe({ next: value => { this.items.set(value.items); this.total = value.totalCount; }, error: () => this.error.set('Unable to load storefront customers. Try again.') }); }
  open(id: string): void { this.api.get(id).subscribe({ next: value => { this.detail.set(value); this.blacklistReason = ''; this.loadBlacklist(value.customer.mobile); }, error: () => this.error.set('Unable to load this customer.') }); }
  activeBlacklist(): StorefrontBlacklistEntry | undefined { return this.blacklist().find(x => x.isActive); }
  loadBlacklist(phone?: string): void { if (!this.canManageBlacklist || !phone) { this.blacklist.set([]); return; } this.blacklistLoading.set(true); this.api.blacklistForPhone(phone).pipe(finalize(() => this.blacklistLoading.set(false))).subscribe({ next: value => this.blacklist.set(value.items), error: () => this.error.set('Unable to load blacklist status.') }); }
  blockCustomer(phone: string): void { const reason = this.blacklistReason.trim(); if (!reason || !confirm('Block new storefront orders for this customer?')) return; this.api.block(phone, reason).subscribe({ next: () => { this.blacklistReason = ''; this.loadBlacklist(phone); }, error: (error) => this.error.set(error?.error?.message ?? 'Unable to blacklist this customer.') }); }
  unblock(entry: StorefrontBlacklistEntry): void { if (!confirm('Allow new storefront orders for this customer?')) return; this.api.unblock(entry.blacklistId).subscribe({ next: () => this.loadBlacklist(entry.normalizedPhone), error: () => this.error.set('Unable to unblacklist this customer.') }); }
  activityOf(detail: StorefrontCustomerDetail, types: string[]) { return detail.recentActivity.filter(activity => types.includes(activity.eventType)); }
}
