import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
@Component({ selector: 'app-collection-products', imports: [CurrencyPipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, PageContainerComponent, PageHeaderComponent], templateUrl: './collection-products.component.html', styles: [`.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:16px 0}.toolbar mat-form-field{min-width:280px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:12px}.product{display:flex;gap:12px;align-items:center;padding:12px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}.product img,.placeholder{width:58px;height:58px;object-fit:cover;border-radius:8px;background:#f1f5f9;display:grid;place-items:center}.product-info{min-width:0;flex:1}.product-info strong,.product-info small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.product-info small{color:var(--wb-text-secondary)}.selected{outline:2px solid var(--wb-primary)}.existing{color:#15803d;font-size:.75rem}.empty{color:var(--wb-text-secondary)}@media(max-width:700px){.product{min-width:0}}`], changeDetection: ChangeDetectionStrategy.OnPush })
export class CollectionProductsComponent {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!; readonly collection = signal<CollectionDetail|null>(null); readonly members = signal<CollectionProduct[]>([]); readonly products = signal<ProductListItem[]>([]); readonly selected = signal(new Set<string>()); search = ''; loading = signal(false);
  constructor(private readonly api: CollectionApiService, private readonly productApi: ProductApiService, private readonly snack: MatSnackBar) { this.load(); }
  load() { this.loading.set(true); this.api.get(this.id).subscribe({ next: x => { this.collection.set(x); this.members.set(x.products); this.searchProducts(); }, error: () => this.snack.open('Collection could not be loaded.', 'Dismiss', { duration: 3500 }) }); }
  searchProducts() { this.productApi.search({ search: this.search || undefined, isActive: true, sortBy: 'productName', descending: false, pageNumber: 1, pageSize: 50 }).subscribe(x => this.products.set(x.items)); }
  has(id: string) { return this.members().some(x => x.productId === id); }
  toggle(id: string) { this.selected.update(current => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; }); }
  add() { const ids = [...this.selected()]; if (!ids.length) return; this.api.addProducts(this.id, ids).subscribe({ next: x => { this.members.set(x); this.selected.set(new Set()); this.snack.open('Products added to collection.', undefined, { duration: 2500 }); }, error: () => this.snack.open('Products could not be added.', 'Dismiss', { duration: 3500 }) }); }
  remove(item: CollectionProduct) { this.api.removeProduct(this.id, item.productId).subscribe({ next: () => { this.members.update(x => x.filter(y => y.productId !== item.productId)); this.snack.open('Product removed from collection.', undefined, { duration: 2500 }); }, error: () => this.snack.open('Product could not be removed.', 'Dismiss', { duration: 3500 }) }); }
}
