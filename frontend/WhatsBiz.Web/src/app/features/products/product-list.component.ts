import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogActions, MatDialogClose, MatDialogContent, MatDialogTitle } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, finalize, forkJoin, map, of } from 'rxjs';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import {
  MasterActionEvent,
  MasterPageComponent,
  MasterPageConfig,
  MasterPageEvent,
  MasterSortEvent,
} from '../../shared/master/public-api';
import { ProductApiService } from './product-api.service';
import { ProductListItem } from './product.models';
import { ProductHistoryDialogComponent } from './product-history-dialog.component';

@Component({
  selector: 'app-product-list',
  imports: [MasterPageComponent],
  templateUrl: './product-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductListComponent implements OnDestroy {
  readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private imageRequest = 0;
  private readonly masterPage = viewChild(MasterPageComponent);
  readonly items = signal<ProductListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly statusBusy = signal(new Set<string>());
  readonly cardImages = signal<Record<string, string>>({});
  readonly summaries = computed(() =>
    this.cards(
      this.items().filter((x) => x.isActive).length,
      this.items().filter((x) => !x.isActive).length,
    ),
  );
  search = '';
  status: 'all' | 'active' | 'inactive' = 'all';
  page = 1;
  size = 20;
  sortBy = 'createdOn';
  descending = true;
  readonly config: MasterPageConfig<ProductListItem> = {
    title: 'Products',
    singular: 'Product',
    description: 'Manage product catalog, pricing, inventory attributes, and media.',
    icon: 'inventory_2',
    newRoute: '/products/new',
    rowId: 'productId',
    rowName: 'productName',
    templateEnabled: true,
    templateLabel: 'Product Import Template',
    importEnabled: true,
    exportEnabled: true,
    recentEnabled: true,
    cardViewEnabled: true,
    cardImageEnabled: true,
    cardSubtitleField: 'productCode',
    cardPriceField: 'sellingPrice',
    compactCardActions: true,
    statusToggle: {
      label: (row) => `Mark ${row.productName} ${row.isActive ? 'inactive' : 'active'}`,
      active: (row) => row.isActive,
      busy: (row) => this.statusBusy().has(row.productId),
      toggle: (row) => this.toggleStatus(row),
    },
    viewRoute: '/products',
    cardFields: [
      { label: 'Category', key: 'categoryName' },
      { label: 'Brand', key: 'brandName' },
      { label: 'Unit', key: 'unitName' },
    ],
    columns: [
      { field: 'productCode', headerName: 'Code' },
      { field: 'productName', headerName: 'Product name', minWidth: 220 },
      { field: 'categoryName', headerName: 'Category' },
      { field: 'brandName', headerName: 'Brand' },
      { field: 'unitName', headerName: 'Unit' },
      {
        field: 'sellingPrice',
        headerName: 'Selling price',
        valueFormatter: (p) =>
          Number(p.value ?? 0).toLocaleString('en-IN', { style: 'currency', currency: 'INR' }),
      },
      {
        field: 'isActive',
        headerName: 'Status',
        sortable: false,
        cellRenderer: (p: { data?: ProductListItem }) => {
          const row = p.data;
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'product-status-toggle';
          if (!row) return button;
          button.style.cssText = `display:inline-flex;align-items:center;gap:6px;min-height:28px;border:0;border-radius:999px;padding:4px 10px;background:${row.isActive ? 'var(--wb-primary-soft)' : 'var(--wb-surface-muted)'};color:${row.isActive ? 'var(--wb-primary)' : 'var(--wb-text-secondary)'};font-size:11px;font-weight:700;cursor:pointer`;
          button.textContent = row.isActive ? 'Active' : 'Inactive';
          button.setAttribute('role', 'switch');
          button.setAttribute('aria-checked', String(row.isActive));
          button.setAttribute('aria-label', `Mark ${row.productName} ${row.isActive ? 'inactive' : 'active'}`);
          button.disabled = this.statusBusy().has(row.productId);
          button.addEventListener('click', () => this.toggleStatus(row));
          return button;
        },
      },
    ],
    detailFields: [
      { label: 'Product code', key: 'productCode' },
      { label: 'Product name', key: 'productName' },
      { label: 'Category', key: 'categoryName' },
      { label: 'Brand', key: 'brandName' },
      { label: 'Unit', key: 'unitName' },
      { label: 'Purchase price', key: 'purchasePrice' },
      { label: 'Selling price', key: 'sellingPrice' },
      { label: 'GST', key: 'gstPercentage' },
      { label: 'Active', key: 'isActive' },
    ],
  };
  constructor(
    private api: ProductApiService,
    private snack: MatSnackBar,
    private dialog: MatDialog,
    private router: Router,
  ) {
    this.load();
  }
  load() {
    this.masterPage()?.clearSelection();
    this.loading.set(true);
    this.api
      .search({
        search: this.search || undefined,
        isActive: this.status === 'all' ? undefined : this.status === 'active',
        sortBy: this.sortBy,
        descending: this.descending,
        pageNumber: this.page,
        pageSize: this.size,
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (x) => {
          this.items.set(x.items);
          this.total.set(x.totalCount);
          this.loadCardImages(x.items);
        },
        error: () => this.snack.open('Unable to load products.', 'Dismiss', { duration: 4000 }),
      });
  }
  paged(e: MasterPageEvent) {
    this.page = e.pageIndex + 1;
    this.size = e.pageSize;
    this.load();
  }
  sorted(e: MasterSortEvent) {
    this.sortBy = e.field || 'productName';
    this.descending = e.direction === 'desc';
    this.page = 1;
    this.load();
  }
  handle(e: MasterActionEvent<ProductListItem>) {
    if (e.action === 'refresh') this.load();
    else if (e.action === 'recent') {
      this.search = '';
      this.status = 'all';
      this.page = 1;
      this.size = 5;
      this.sortBy = 'createdOn';
      this.descending = true;
      this.load();
      this.snack.open('Showing the 5 most recently added products.', undefined, {
        duration: 2500,
      });
    }
    else if (e.action === 'template')
      this.api
        .template()
        .subscribe({
          next: (file) => this.download(file, 'product-import-template.xlsx'),
          error: () =>
            this.snack.open('Unable to download the product import template.', 'Dismiss', {
              duration: 4000,
            }),
        });
    else if (e.action === 'import') this.fileInput()?.nativeElement.click();
    else if (e.action === 'export')
      this.api
        .export(
          this.search || undefined,
          this.status === 'all' ? undefined : this.status === 'active',
        )
        .subscribe((b) => this.download(b, 'products.xlsx'));
    else if (e.action === 'print') window.print();
    else if (e.action === 'edit' && e.row)
      this.router.navigate(['/products', e.row.productId, 'edit']);
    else if (e.action === 'view' && e.row)
      this.router.navigate(['/products', e.row.productId]);
    else if (e.action === 'duplicate' && e.row)
      this.router.navigate(['/products/new'], { queryParams: { copyFrom: e.row.productId } });
    else if (e.action === 'history' && e.row)
      this.dialog.open(ProductHistoryDialogComponent, { data: e.row, width: '680px' });
    else if (e.action === 'delete' && e.rows?.length) this.remove(e.rows);
    else if (e.action === 'bulk' && e.rows?.length) this.bulkStatus(e.rows);
    else
      this.snack.open(`${e.action} is ready for the selected product(s).`, undefined, {
        duration: 2500,
      });
  }
  toggleStatus(row: ProductListItem) {
    if (this.statusBusy().has(row.productId)) return;
    const previous = row.isActive;
    row.isActive = !previous;
    this.items.update((items) => [...items]);
    this.statusBusy.update((busy) => new Set(busy).add(row.productId));
    this.api.setStatus(row.productId, row.isActive).pipe(finalize(() => this.statusBusy.update((busy) => { const next = new Set(busy); next.delete(row.productId); return next; }))).subscribe({
      next: () => this.snack.open(`Product marked ${row.isActive ? 'active' : 'inactive'}.`, undefined, { duration: 2500 }),
      error: () => { row.isActive = previous; this.items.update((items) => [...items]); this.snack.open('Unable to update product status.', 'Dismiss', { duration: 4000 }); },
    });
  }
  bulkStatus(rows: ProductListItem[]) {
    this.dialog.open(ProductBulkStatusDialogComponent, { data: { count: rows.length }, width: '420px' }).afterClosed().subscribe((isActive: boolean | undefined) => {
      if (isActive === undefined) return;
      const ids = rows.map((row) => row.productId);
      this.loading.set(true);
      this.api.setStatuses(ids, isActive).pipe(finalize(() => this.loading.set(false))).subscribe({
        next: (result) => {
          const missing = new Set(result.notFoundProductIds);
          this.items.update((items) => items.map((item) => ids.includes(item.productId) && !missing.has(item.productId) ? { ...item, isActive } : item));
          this.masterPage()?.clearSelection();
          const detail = missing.size ? ` ${missing.size} product(s) were not found for this tenant.` : '';
          this.snack.open(`${result.updatedCount} product(s) marked ${isActive ? 'active' : 'inactive'}.${detail}`, 'Dismiss', { duration: 4500 });
        },
        error: () => this.snack.open('Unable to update selected products.', 'Dismiss', { duration: 4000 }),
      });
    });
  }
  remove(rows: ProductListItem[]) {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete products', message: `Delete ${rows.length} selected product(s)?` },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (ok)
          rows.forEach((x, i) =>
            this.api.delete(x.productId).subscribe({
              complete: () => {
                if (i === rows.length - 1) this.load();
              },
            }),
          );
      });
  }
  upload(e: Event) {
    const input = e.target as HTMLInputElement,
      file = input.files?.[0];
    if (!file) return;
    this.loading.set(true);
    this.api
      .import(file)
      .pipe(
        finalize(() => {
          this.loading.set(false);
          input.value = '';
        }),
      )
      .subscribe({
        next: (x) => {
          this.snack.open(`Imported ${x.importedCount} product(s).`, undefined, { duration: 3500 });
          this.load();
        },
        error: () => this.snack.open('Product import failed.', 'Dismiss', { duration: 4000 }),
      });
  }
  private cards(active: number, inactive: number) {
    return [
      {
        label: 'Total records',
        value: this.total(),
        subtitle: 'All products',
        icon: 'inventory_2',
        tone: 'primary' as const,
      },
      {
        label: 'Active',
        value: active,
        subtitle: 'Visible page',
        icon: 'check_circle',
        tone: 'success' as const,
      },
      {
        label: 'Inactive',
        value: inactive,
        subtitle: 'Visible page',
        icon: 'pause_circle',
        tone: 'warning' as const,
      },
      {
        label: 'Recently loaded',
        value: this.items().length,
        subtitle: 'Current page',
        icon: 'schedule',
        tone: 'info' as const,
      },
    ];
  }
  ngOnDestroy(): void {
    this.releaseCardImages();
  }
  private loadCardImages(rows: ProductListItem[]): void {
    this.releaseCardImages();
    const request = ++this.imageRequest;
    const requests = rows
      .filter((row) => !!row.imageUrl)
      .map((row) =>
        this.api.imageByUrl(row.imageUrl!, true).pipe(
          map((blob) => ({ id: row.productId, url: URL.createObjectURL(blob) })),
          catchError(() => of(null)),
        ),
      );
    if (!requests.length) return;
    forkJoin(requests).subscribe((images) => {
      if (request !== this.imageRequest) { for (const image of images) if (image) URL.revokeObjectURL(image.url); return; }
      const next: Record<string, string> = {};
      for (const image of images) if (image) next[image.id] = image.url;
      this.cardImages.set(next);
    });
  }
  private releaseCardImages(): void {
    this.imageRequest++;
    for (const url of Object.values(this.cardImages())) URL.revokeObjectURL(url);
    this.cardImages.set({});
  }
  private download(b: Blob, n: string) {
    const u = URL.createObjectURL(b),
      a = document.createElement('a');
    a.href = u;
    a.download = n;
    a.click();
    URL.revokeObjectURL(u);
  }
}

@Component({
  selector: 'app-product-bulk-status-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatDialogClose],
  template: `<h2 mat-dialog-title>Update product status</h2><mat-dialog-content><p>{{ data.count }} selected product(s)</p><p>Choose the status to apply. Products outside your tenant are never changed.</p></mat-dialog-content><mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-stroked-button [mat-dialog-close]="false">Mark Inactive</button><button mat-flat-button color="primary" [mat-dialog-close]="true">Mark Active</button></mat-dialog-actions>`,
})
class ProductBulkStatusDialogComponent { readonly data = inject<{ count: number }>(MAT_DIALOG_DATA); }