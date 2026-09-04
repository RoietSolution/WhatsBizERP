import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { ProductApiService } from './product-api.service';
import { ProductListItem } from './product.models';
import { CollectionApiService } from './collection-api.service';
import { CollectionDetail, CollectionProduct } from './collection.models';
@Component({
  selector: 'app-collection-products',
  imports: [CurrencyPipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, PageContainerComponent, PageHeaderComponent],
  templateUrl: './collection-products.component.html',
  styles: [`
    :host { display: block; }
    .section-heading { display:flex;align-items:flex-end;justify-content:space-between;gap:12px;margin:24px 0 12px; }
    .section-heading h2 { margin:0;font-size:1.2rem; }
    .section-heading p { margin:4px 0 0;color:var(--wb-text-secondary);font-size:.875rem; }
    .toolbar { display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:0 0 16px; }
    .toolbar mat-form-field { min-width:280px;flex:1 1 320px; }
    .grid { display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:12px; }
    .product { position:relative;display:flex;min-width:0;gap:12px;align-items:center;padding:14px 48px 14px 14px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md);cursor:pointer;transition:border-color 150ms ease,box-shadow 150ms ease,transform 150ms ease; }
    .product:hover,.product:focus-visible { border-color:var(--wb-primary);box-shadow:var(--wb-shadow-sm);outline:none;transform:translateY(-1px); }
    .product img,.placeholder { width:64px;height:64px;flex:0 0 64px;object-fit:cover;border-radius:8px;background:#f1f5f9;display:grid;place-items:center; }
    .product-info { min-width:0;flex:1; }
    .product-info strong,.product-info small { display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap; }
    .product-info small { margin-top:3px;color:var(--wb-text-secondary); }
    .membership-action { position:absolute;top:6px;right:6px;z-index:1; }
    .membership-action .material-symbols-rounded { font-size:21px; }
    .remove-action { color:var(--wb-danger); }
    .add-action.active { color:var(--wb-primary);background:var(--wb-primary-soft); }
    .selected { border-color:var(--wb-primary);box-shadow:0 0 0 2px var(--wb-primary-soft); }
    .empty { grid-column:1/-1;margin:0;padding:24px;border:1px dashed var(--wb-border);border-radius:var(--wb-radius-md);color:var(--wb-text-secondary);text-align:center; }
    @media(max-width:700px) {
      .section-heading { align-items:flex-start;flex-direction:column; }
      .toolbar { align-items:stretch;flex-direction:column; }
      .toolbar mat-form-field { width:100%;min-width:0;flex:none; }
      .toolbar button { width:100%;min-width:0; }
      .grid { grid-template-columns:1fr; }
      .product { padding:12px 44px 12px 12px; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionProductsComponent {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!; readonly collection = signal<CollectionDetail|null>(null); readonly members = signal<CollectionProduct[]>([]); readonly products = signal<ProductListItem[]>([]); readonly selected = signal(new Set<string>()); search = ''; loading = signal(false);
  readonly availableProducts = computed(() => this.products().filter(product => !this.has(product.productId)));
  constructor(private readonly api: CollectionApiService, private readonly productApi: ProductApiService, private readonly snack: MatSnackBar, private readonly router: Router) { this.load(); }
  load() { this.loading.set(true); this.api.get(this.id).subscribe({ next: x => { this.collection.set(x); this.members.set(x.products); this.searchProducts(); }, error: () => this.snack.open('Collection could not be loaded.', 'Dismiss', { duration: 3500 }) }); }
  searchProducts() { this.productApi.search({ search: this.search || undefined, isActive: true, sortBy: 'productName', descending: false, pageNumber: 1, pageSize: 50 }).subscribe(x => this.products.set(x.items.filter(product => product.isWhatsAppVisible))); }
  has(id: string) { return this.members().some(x => x.productId === id); }
  toggle(id: string) { this.selected.update(current => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; }); }
  add() { const ids = [...this.selected()]; if (!ids.length) return; this.api.addProducts(this.id, ids).subscribe({ next: x => { this.members.set(x); this.selected.set(new Set()); this.snack.open('Products added to collection.', undefined, { duration: 2500 }); }, error: () => this.snack.open('Products could not be added.', 'Dismiss', { duration: 3500 }) }); }
  remove(item: CollectionProduct) { this.api.removeProduct(this.id, item.productId).subscribe({ next: () => { this.members.update(x => x.filter(y => y.productId !== item.productId)); this.snack.open('Product removed from collection.', undefined, { duration: 2500 }); }, error: () => this.snack.open('Product could not be removed.', 'Dismiss', { duration: 3500 }) }); }
  openProduct(productId: string) { void this.router.navigate(['/products', productId]); }
}
