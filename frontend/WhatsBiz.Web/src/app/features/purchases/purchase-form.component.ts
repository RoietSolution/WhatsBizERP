import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatNativeDateModule } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { PurchaseApiService } from './purchase-api.service';
type Line = {
  productId: string;
  productName: string;
  barcode?: string;
  batchNo?: string;
  expiryDate?: Date | null;
  quantity: number;
  freeQuantity: number;
  purchasePrice: number;
  mrp: number;
  sellingPrice: number;
  discountPercentage: number;
  discountAmount: number;
  gstPercentage: number;
};
type ProductLookup = {
  productId: string; productName: string; productCode: string; barcode?: string;
  purchasePrice?: number; mrp: number; sellingPrice: number; gstPercentage: number;
};
@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatAutocompleteModule,
    MatNativeDateModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageContainerComponent,
    PageHeaderComponent,
    StatusChipComponent,
  ],
  templateUrl: './purchase-form.component.html',
  styleUrl: './purchase-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseFormComponent {
  id = '';
  supplierId = '';
  supplierInvoiceNo = '';
  warehouseId = '';
  dueDate: Date | null = null;
  lookup: string | ProductLookup = '';
  billDiscount = 0;
  expense = 0;
  roundOff = 0;
  remarks = '';
  items = signal<Line[]>([]);
  suppliers = signal<any[]>([]);
  warehouses = signal<any[]>([]);
  productOptions = signal<ProductLookup[]>([]);
  attachments = signal<File[]>([]);
  private lookupTimer?: ReturnType<typeof setTimeout>;
  constructor(
    private api: PurchaseApiService,
    route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar,
  ) {
    this.id = route.snapshot.paramMap.get('id') ?? '';
    api.suppliers().subscribe((x) => this.suppliers.set(x));
    api.warehouses().subscribe((x) => this.warehouses.set(x));
    if (this.id)
      api.get(this.id).subscribe((x) => {
        this.supplierId = x.supplierId;
        this.supplierInvoiceNo = x.supplierInvoiceNo ?? '';
        this.warehouseId = x.warehouseId;
        this.dueDate = x.dueDate ? new Date(x.dueDate) : null;
        this.remarks = x.remarks ?? '';
        this.items.set(x.items.map((i) => ({ ...i, expiryDate: i.expiryDate ? new Date(i.expiryDate) : null })));
      });
  }
  searchProducts(value: string | ProductLookup) {
    if (typeof value !== 'string') return;
    clearTimeout(this.lookupTimer);
    if (value.trim().length < 2) { this.productOptions.set([]); return; }
    this.lookupTimer = setTimeout(() =>
      this.api.products(value.trim()).subscribe((x) => this.productOptions.set(x)), 250);
  }
  displayProduct(value: string | ProductLookup): string {
    return typeof value === 'string' ? value : `${value.productCode} · ${value.productName}`;
  }
  selectProduct(event: MatAutocompleteSelectedEvent) {
    this.addProduct(event.option.value as ProductLookup);
  }
  find() {
    if (!this.lookup) return;
    if (typeof this.lookup !== 'string') { this.addProduct(this.lookup); return; }
    // Free-text entry must remain a product/code search. Supplying the same value
    // as an exact barcode filter made normal purchase lookup return no rows.
    this.api.products(this.lookup).subscribe((x) => {
      const p = x[0] as ProductLookup | undefined;
      if (!p) {
        this.snack.open('Product not found', 'Close', { duration: 2500 });
        return;
      }
      this.addProduct(p);
    });
  }
  addProduct(p: ProductLookup) {
    if (this.items().some((x) => x.productId === p.productId)) {
      this.snack.open('Product is already added', 'Close', { duration: 2000 });
      this.lookup = '';
      return;
    }
    this.items.update((a) => [...a, { productId: p.productId, productName: p.productName, barcode: p.barcode, quantity: 1, freeQuantity: 0, purchasePrice: p.purchasePrice ?? 0, mrp: p.mrp, sellingPrice: p.sellingPrice, discountPercentage: 0, discountAmount: 0, gstPercentage: p.gstPercentage }]);
    this.lookup = '';
    this.productOptions.set([]);
  }
  selectAttachments(event: Event) {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    const invalid = files.find((x) => x.size > 10 * 1024 * 1024);
    if (invalid) { this.snack.open(`${invalid.name} exceeds 10 MB`, 'Close', { duration: 3000 }); return; }
    this.attachments.update((current) => [...current, ...files]);
    input.value = '';
  }
  removeAttachment(index: number) {
    this.attachments.update((files) => files.filter((_, i) => i !== index));
  }
  remove(i: number) {
    this.items.update((x) => x.filter((_, n) => n !== i));
  }
  subtotal() {
    return this.items().reduce((s, x) => s + x.quantity * x.purchasePrice, 0);
  }
  discount() {
    return this.items().reduce(
      (s, x) => s + (x.quantity * x.purchasePrice * x.discountPercentage) / 100,
      0,
    );
  }
  tax() {
    return this.items().reduce((s, x) => {
      const b = x.quantity * x.purchasePrice * (1 - x.discountPercentage / 100);
      return s + (b * x.gstPercentage) / 100;
    }, 0);
  }
  total() {
    return (
      this.subtotal() -
      this.discount() -
      this.billDiscount +
      this.tax() +
      this.expense +
      this.roundOff
    );
  }
  save(status: string) {
    if (!this.supplierId || !this.warehouseId || !this.items().length) {
      this.snack.open('Supplier, warehouse and items are required', 'Close', { duration: 3000 });
      return;
    }
    const body = {
      supplierId: this.supplierId,
      supplierInvoiceNo: this.supplierInvoiceNo,
      invoiceDate: new Date().toISOString(),
      dueDate: this.dueDate?.toISOString() ?? null,
      warehouseId: this.warehouseId,
      items: this.items().map((item) => ({ ...item, expiryDate: this.dateOnly(item.expiryDate) })),
      expenses: this.expense
        ? [{ expenseType: 'FREIGHT', amount: this.expense, isTaxable: false }]
        : [],
      payments: [],
      billDiscount: this.billDiscount,
      roundOff: this.roundOff,
      remarks: this.remarks,
      status,
    };
    const call = this.id ? this.api.update(this.id, body) : this.api.create(body);
    call.subscribe({
      next: (result: any) => {
        const purchaseId = result.purchaseInvoiceId ?? this.id;
        const uploads = this.attachments().map((file) => this.api.uploadAttachment(purchaseId, file));
        (uploads.length ? forkJoin(uploads) : of([])).subscribe({
          next: () => {
            this.snack.open('Purchase saved', 'Close', { duration: 2000 });
            this.router.navigateByUrl('/purchases');
          },
          error: () => this.snack.open('Purchase saved, but an attachment upload failed', 'Close', { duration: 4000 }),
        });
      },
      error: (e) =>
        this.snack.open(e.error?.detail ?? 'Unable to save', 'Close', { duration: 4000 }),
    });
  }
  private dateOnly(value?: Date | null): string | null {
    if (!value) return null;
    const year = value.getFullYear();
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const day = String(value.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
