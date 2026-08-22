import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CustomerApiService } from '../customers/customer-api.service';
import { CustomerList } from '../customers/customer.models';
import { CustomerGroup } from '../customers/customer-group.models';
import { CustomerGroupApiService } from '../customers/customer-group-api.service';
import { CollectionApiService } from './collection-api.service';
import { CollectionDetail } from './collection.models';

@Component({
  selector: 'app-collection-send',
  imports: [CurrencyPipe, FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, PageContainerComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-container>
      <app-page-header [title]="'Send ' + (collection()?.name || 'Collection') + ' on WhatsApp'" description="Choose one or more active customers and preview the current sellable products.">
        <a header-actions mat-button [routerLink]="['/products/collections', id, 'products']">Back to products</a>
      </app-page-header>

      <div class="toolbar">
        <mat-form-field appearance="outline">
          <mat-label>Search customer</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="find()" />
        </mat-form-field>
        <label class="segment-label">Customer group
          <select [ngModel]="segment()" (ngModelChange)="segment.set($event); find()">
            <option value="">All active customers</option>
            @for (group of groups(); track group.customerGroupId) { <option [value]="group.customerGroupId">{{ group.groupName }}</option> }
          </select>
        </label>
        <button mat-flat-button color="primary" type="button" (click)="find()">Search</button>
        <button mat-stroked-button type="button" (click)="selectVisible()">{{ allVisibleSelected() ? 'Clear visible' : 'Select visible' }}</button>
      </div>

      <p class="selection-summary">{{ selectedCustomers().length }} customer(s) selected. Only customers with a WhatsApp/mobile number can be sent.</p>
      <div class="customer-list">
        @for (customer of visibleCustomers(); track customer.customerId) {
          <label class="customer" [class.selected]="isSelected(customer.customerId)">
            <mat-checkbox [checked]="isSelected(customer.customerId)" (change)="toggle(customer)"></mat-checkbox>
            <span class="customer-details">
              <strong>{{ customer.customerName }}</strong>
              <span>{{ customer.mobile || 'No mobile number' }} · {{ customer.customerCode }} · {{ customer.customerType || 'Customer' }}</span>
            </span>
          </label>
        }
        @empty { <p class="muted">No matching active customers found. Check the search or customer group.</p> }
      </div>

      @if (collection(); as item) {
        <section class="preview">
          <h3>{{ item.name }}</h3>
          <p class="muted">{{ item.products.length }} product(s) currently in this collection.</p>
          @for (product of item.products; track product.productId) {
            <div class="line"><span>{{ product.productName }}</span><strong>{{ product.sellingPrice | currency:'INR' }}</strong></div>
          }
          <div class="actions">
            <a mat-button routerLink="/products/collections">Cancel</a>
            <button mat-flat-button color="primary" type="button" [disabled]="!selectedCustomers().length || sending" (click)="send()">
              {{ sending ? 'Sending…' : 'Send to ' + selectedCustomers().length + ' customer(s)' }}
            </button>
          </div>
        </section>
      }
    </app-page-container>
  `,
  styles: [`
    .toolbar,.actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:16px 0}.toolbar mat-form-field{min-width:280px}.segment-label{display:flex;align-items:center;gap:8px;color:var(--wb-text-secondary);font-size:.85rem}.segment-label select{min-height:40px;padding:0 10px;color:var(--wb-text-primary);background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:6px;font:inherit}.selection-summary,.muted{color:var(--wb-text-secondary);font-size:.85rem}.customer-list{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:10px}.customer{display:flex;align-items:flex-start;gap:8px;padding:12px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md);cursor:pointer}.customer.selected{outline:2px solid var(--wb-primary);background:var(--wb-primary-soft)}.customer-details{display:flex;min-width:0;flex-direction:column;gap:5px}.customer-details span{overflow:hidden;color:var(--wb-text-secondary);font-size:.85rem;text-overflow:ellipsis;white-space:nowrap}.preview{max-width:640px;margin-top:24px;padding:20px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}.line{display:flex;justify-content:space-between;padding:10px 0;border-bottom:1px solid var(--wb-border)}.actions{justify-content:flex-end}@media(max-width:700px){.toolbar mat-form-field,.segment-label,.segment-label select,.toolbar button{width:100%;min-width:0}.segment-label{align-items:stretch;flex-direction:column}.actions button,.actions a{width:100%}}
  `],
})
export class CollectionSendComponent {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly collection = signal<CollectionDetail | null>(null);
  readonly customers = signal<CustomerList[]>([]);
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly groups = signal<CustomerGroup[]>([]);
  readonly segment = signal('');
  search = '';
  sending = false;

  readonly visibleCustomers = computed(() => {
    return this.customers();
  });
  readonly selectedCustomers = computed(() => this.customers().filter(x => this.selectedIds().has(x.customerId)));
  readonly allVisibleSelected = computed(() => this.visibleCustomers().length > 0 && this.visibleCustomers().every(x => this.selectedIds().has(x.customerId)));

  constructor(private readonly api: CollectionApiService, private readonly customerApi: CustomerApiService, private readonly groupApi: CustomerGroupApiService, private readonly snack: MatSnackBar) {
    this.api.get(this.id).subscribe({ next: x => this.collection.set(x), error: () => this.snack.open('Collection could not be loaded.', 'Dismiss', { duration: 3500 }) });
    this.groupApi.list().subscribe({ next: x => this.groups.set(x), error: () => this.snack.open('Customer groups could not be loaded.', 'Dismiss', { duration: 3500 }) });
    this.find();
  }

  find() {
    const request = this.segment() ? this.groupApi.customers(this.segment()) : this.customerApi.search({ search: this.search || undefined, isActive: true, sortBy: 'customerName', descending: false, pageNumber: 1, pageSize: 200 });
    request.subscribe({
      next: x => this.customers.set(this.search && this.segment() ? x.items.filter(c => `${c.customerName} ${c.customerCode} ${c.mobile || ''}`.toLowerCase().includes(this.search.toLowerCase())) : x.items),
      error: () => this.snack.open('Customers could not be loaded.', 'Dismiss', { duration: 3500 }),
    });
  }

  isSelected(id: string) { return this.selectedIds().has(id); }

  toggle(customer: CustomerList) {
    const next = new Set(this.selectedIds());
    next.has(customer.customerId) ? next.delete(customer.customerId) : next.add(customer.customerId);
    this.selectedIds.set(next);
  }

  selectVisible() {
    const next = new Set(this.selectedIds());
    if (this.allVisibleSelected()) this.visibleCustomers().forEach(x => next.delete(x.customerId));
    else this.visibleCustomers().forEach(x => next.add(x.customerId));
    this.selectedIds.set(next);
  }

  send() {
    const recipients = this.selectedCustomers().filter(x => !!x.mobile);
    if (!recipients.length) { this.snack.open('Select customers with a WhatsApp/mobile number.', 'Dismiss', { duration: 3500 }); return; }
    this.sending = true;
    forkJoin(recipients.map(customer => this.api.send(this.id, customer.customerId).pipe(catchError(() => of(null))))).subscribe(results => {
      this.sending = false;
      const succeeded = results.filter(x => !!x?.succeeded).length;
      const failed = recipients.length - succeeded;
      this.snack.open(`${this.collection()?.name} sent to ${succeeded} customer(s)${failed ? `; ${failed} failed.` : '.'}`, 'Dismiss', { duration: 5000 });
    });
  }
}
