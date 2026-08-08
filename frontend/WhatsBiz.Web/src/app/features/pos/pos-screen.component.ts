import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  ViewChild,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PaymentDialogComponent } from './payment-dialog.component';
import { PosCartGridComponent } from './pos-cart-grid.component';
import { PosSummaryComponent } from './pos-summary.component';
import { POSApiService, POSWarehouse } from './pos-api.service';
import { CartItem, PaymentMethod, POSCustomer, POSProduct } from './pos.models';

@Component({
  selector: 'app-pos-screen',
  imports: [
    FormsModule,
    ScrollingModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageContainerComponent,
    PosCartGridComponent,
    PosSummaryComponent,
  ],
  templateUrl: './pos-screen.component.html',
  styleUrl: './pos-screen.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class POSScreenComponent {
  @ViewChild('barcodeInput') barcodeInput?: ElementRef<HTMLInputElement>;
  @ViewChild('productInput') productInput?: ElementRef<HTMLInputElement>;
  @ViewChild('customerInput') customerInput?: ElementRef<HTMLInputElement>;
  readonly cart = signal<CartItem[]>([]);
  readonly products = signal<POSProduct[]>([]);
  readonly customers = signal<POSCustomer[]>([]);
  readonly customer = signal<POSCustomer | null>(null);
  readonly methods = signal<PaymentMethod[]>([]);
  readonly warehouses = signal<POSWarehouse[]>([]);
  readonly subtotal = signal(0);
  readonly itemDiscount = signal(0);
  readonly tax = signal(0);
  readonly total = signal(0);
  readonly itemCount = signal(0);
  readonly shortcuts = [
    { key: 'F2', label: 'Customer', icon: 'person' },
    { key: 'F3', label: 'Product', icon: 'search' },
    { key: 'F4', label: 'Hold', icon: 'pause' },
    { key: 'F5', label: 'Payment', icon: 'payments' },
    { key: 'F6', label: 'Print', icon: 'print' },
    { key: 'F7', label: 'Return', icon: 'assignment_return' },
    { key: 'F8', label: 'Discount', icon: 'percent' },
    { key: 'Ctrl+N', label: 'New Bill', icon: 'add' },
    { key: 'Ctrl+P', label: 'Print', icon: 'print' },
    { key: 'Esc', label: 'Cancel', icon: 'close' },
  ];
  barcode = '';
  search = '';
  customerSearch = '';
  warehouseId = '';
  billDiscount = 0;
  remarks = '';
  lastInvoiceId = '';
  paymentMethod = 'CASH';
  quickAmount = 0;
  private readonly duplicateProductIds = new Map<string, string>();
  constructor(
    private api: POSApiService,
    private dialog: MatDialog,
    private snack: MatSnackBar,
    private router: Router,
  ) {
    api.methods().subscribe((x) => this.methods.set(x));
    api.warehouses().subscribe({
      next: (x) => {
        this.warehouses.set(x);
        this.warehouseId = (x.find((warehouse) => warehouse.isDefault) ?? x[0])?.warehouseId ?? '';
      },
      error: (error) => this.showOrderError(error, 'Warehouses could not be loaded.'),
    });
    setTimeout(() => this.barcodeInput?.nativeElement.focus());
  }
  @HostListener('window:keydown', ['$event']) key(e: KeyboardEvent) {
    const key = e.ctrlKey ? `Ctrl+${e.key.toUpperCase()}` : e.key;
    if (this.run(key)) e.preventDefault();
  }
  run(key: string): boolean {
    const map: Record<string, () => void> = {
      F2: () => this.customerInput?.nativeElement.focus(),
      F3: () => this.productInput?.nativeElement.focus(),
      F4: () => this.hold(),
      F5: () => this.payment(),
      F6: () => this.print(),
      F7: () => void this.router.navigate(['/pos/returns']),
      F8: () => this.focusDiscount(),
      'Ctrl+N': () => this.cancel(),
      'Ctrl+P': () => this.print(),
      Escape: () => this.cancel(),
      Esc: () => this.cancel(),
    };
    if (!map[key]) return false;
    map[key]();
    return true;
  }
  trackProduct(_: number, x: POSProduct) {
    return x.productId;
  }
  scan() {
    if (!this.barcode) return;
    this.api.products(undefined, this.barcode).subscribe((x) => {
      if (x[0]) this.add(x[0]);
      else this.snack.open('Barcode not found.', undefined, { duration: 2000 });
      this.barcode = '';
      this.barcodeInput?.nativeElement.focus();
    });
  }
  findProducts() {
    if (this.search.trim().length < 2) {
      this.products.set([]);
      return;
    }
    this.api.products(this.search).subscribe((x) => this.products.set(x));
  }
  findCustomers() {
    if (this.customerSearch.trim().length < 2) {
      this.customers.set([]);
      return;
    }
    this.api.customers(this.customerSearch).subscribe((x) => this.customers.set(x));
  }
  selectCustomer(x: POSCustomer) {
    this.customer.set(x);
    this.customers.set([]);
    this.customerSearch = x.customerName;
  }
  add(x: POSProduct) {
    const existing = this.cart().find((y) => y.productId === x.productId);
    if (existing) existing.quantity++;
    else
      this.cart.update((v) => [
        ...v,
        {
          ...x,
          quantity: 1,
          unitPrice: x.sellingPrice,
          discountPercentage: 0,
          discountAmount: 0,
          taxPercentage: x.gstPercentage,
        },
      ]);
    this.products.set([]);
    this.search = '';
    this.refresh();
    this.barcodeInput?.nativeElement.focus();
  }
  duplicate(x: CartItem) {
    const lineProductId = `${x.productId}-${Date.now()}`;
    this.duplicateProductIds.set(
      lineProductId,
      this.duplicateProductIds.get(x.productId) ?? x.productId,
    );
    this.cart.update((v) => [...v, { ...x, productId: lineProductId }]);
    this.refresh();
  }
  remove(x: CartItem) {
    this.cart.update((v) => v.filter((y) => y !== x));
    this.refresh();
  }
  refresh() {
    const subtotal = this.cart().reduce((a, x) => a + x.quantity * x.unitPrice, 0),
      discount = this.cart().reduce(
        (a, x) => a + (x.quantity * x.unitPrice * x.discountPercentage) / 100,
        0,
      ),
      tax = this.cart().reduce(
        (a, x) =>
          a + (x.quantity * x.unitPrice * (1 - x.discountPercentage / 100) * x.taxPercentage) / 100,
        0,
      );
    this.subtotal.set(+subtotal.toFixed(2));
    this.itemDiscount.set(+discount.toFixed(2));
    this.tax.set(+tax.toFixed(2));
    this.total.set(+(subtotal - discount - this.billDiscount + tax).toFixed(2));
    this.itemCount.set(this.cart().reduce((a, x) => a + x.quantity, 0));
    this.cart.set([...this.cart()]);
  }
  payload(payments: object[] = []) {
    return {
      customerId: this.customer()?.customerId,
      warehouseId: this.warehouseId,
      items: this.cart().map((x) => ({
        productId: this.duplicateProductIds.get(x.productId) ?? x.productId,
        barcode: x.barcode,
        quantity: x.quantity,
        unitPrice: x.unitPrice,
        discountPercentage: x.discountPercentage,
        discountAmount: 0,
        taxPercentage: x.taxPercentage,
      })),
      payments,
      billDiscount: this.billDiscount,
      roundOff: 0,
      remarks: this.remarks,
      interState: false,
    };
  }
  payment() {
    if (!this.cart().length || !this.warehouseId) return;
    this.dialog
      .open(PaymentDialogComponent, {
        data: {
          total: this.total(),
          methods: this.methods(),
          preferredMethod: this.paymentMethod,
          quickAmount: this.quickAmount,
        },
        width: '720px',
        maxWidth: '96vw',
      })
      .afterClosed()
      .subscribe((payments) => {
        if (payments)
          this.api.invoice(this.payload(payments)).subscribe({
            next: (x) => {
              this.lastInvoiceId = x.invoiceId;
              this.snack.open(`Invoice ${x.invoiceNumber} created.`, undefined, {
                duration: 3000,
                panelClass: 'wb-success',
              });
              this.cancel();
              this.api.print(x.invoiceId);
            },
            error: (error) => this.showOrderError(error, 'Invoice could not be created.'),
          });
      });
  }
  hold() {
    if (!this.cart().length) return;
    this.api.hold(this.payload()).subscribe({
      next: (x) => {
        this.snack.open(`Bill ${x.invoiceNumber} held.`, undefined, {
          duration: 3000,
          panelClass: 'wb-success',
        });
        this.cancel();
      },
      error: (error) => this.showOrderError(error, 'Bill could not be held.'),
    });
  }
  print() {
    if (this.lastInvoiceId) this.api.print(this.lastInvoiceId);
    else this.snack.open('Complete a bill before printing.', undefined, { duration: 2000 });
  }
  focusDiscount() {
    (document.querySelector('app-pos-summary input') as HTMLInputElement | null)?.focus();
  }
  cancel() {
    this.cart.set([]);
    this.customer.set(null);
    this.customerSearch = '';
    this.billDiscount = 0;
    this.remarks = '';
    this.quickAmount = 0;
    this.refresh();
    this.barcodeInput?.nativeElement.focus();
  }
  private showOrderError(error: HttpErrorResponse, fallback: string) {
    const message = this.errorMessage(error, fallback);
    this.snack.open(message, 'Dismiss', { duration: 12000, panelClass: 'wb-error' });
  }

  private errorMessage(error: HttpErrorResponse, fallback: string): string {
    const body = error.error;
    if (typeof body === 'string' && body.trim()) return body.trim();
    if (body && typeof body === 'object') {
      if (typeof body.detail === 'string' && body.detail.trim()) return body.detail.trim();
      if (typeof body.title === 'string' && body.title.trim()) return body.title.trim();
      if (Array.isArray(body.errors)) {
        const messages = body.errors
          .flatMap((value: unknown) => (Array.isArray(value) ? value : [value]))
          .filter(
            (value: unknown): value is string =>
              typeof value === 'string' && value.trim().length > 0,
          )
          .map((value: string) => value.trim());
        if (messages.length) return messages.join('; ');
      }
    }
    return error.message?.trim() || fallback;
  }
}
