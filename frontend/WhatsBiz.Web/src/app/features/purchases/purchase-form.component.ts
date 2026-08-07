import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { PurchaseApiService } from './purchase-api.service';
type Line = {
  productId: string;
  productName: string;
  barcode?: string;
  batchNo?: string;
  expiryDate?: string;
  quantity: number;
  freeQuantity: number;
  purchasePrice: number;
  mrp: number;
  sellingPrice: number;
  discountPercentage: number;
  discountAmount: number;
  gstPercentage: number;
};
@Component({
  imports: [
    FormsModule,
    MatButtonModule,
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
  dueDate = '';
  lookup = '';
  billDiscount = 0;
  expense = 0;
  roundOff = 0;
  remarks = '';
  items = signal<Line[]>([]);
  suppliers = signal<any[]>([]);
  warehouses = signal<any[]>([]);
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
        this.dueDate = x.dueDate?.slice(0, 10) ?? '';
        this.remarks = x.remarks ?? '';
        this.items.set(x.items.map((i) => ({ ...i })));
      });
  }
  find() {
    if (!this.lookup) return;
    this.api.products(this.lookup, this.lookup).subscribe((x) => {
      const p = x[0];
      if (!p) {
        this.snack.open('Product not found', 'Close', { duration: 2500 });
        return;
      }
      this.items.update((a) => [
        ...a,
        {
          productId: p.productId,
          productName: p.productName,
          barcode: p.barcode,
          quantity: 1,
          freeQuantity: 0,
          purchasePrice: p.purchasePrice ?? 0,
          mrp: p.mrp,
          sellingPrice: p.sellingPrice,
          discountPercentage: 0,
          discountAmount: 0,
          gstPercentage: p.gstPercentage,
        },
      ]);
      this.lookup = '';
    });
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
      dueDate: this.dueDate ? new Date(this.dueDate).toISOString() : null,
      warehouseId: this.warehouseId,
      items: this.items(),
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
      next: () => {
        this.snack.open('Purchase saved', 'Close', { duration: 2000 });
        this.router.navigateByUrl('/purchases');
      },
      error: (e) =>
        this.snack.open(e.error?.detail ?? 'Unable to save', 'Close', { duration: 4000 }),
    });
  }
}
