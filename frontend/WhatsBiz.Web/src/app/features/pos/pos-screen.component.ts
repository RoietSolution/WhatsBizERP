import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  ViewChild,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PaymentDialogComponent, PaymentResult } from './payment-dialog.component';
import { BarcodeScannerComponent } from './barcode-scanner.component';
import { PosCartGridComponent } from './pos-cart-grid.component';
import { PosSummaryComponent } from './pos-summary.component';
import { POSApiService, POSWarehouse } from './pos-api.service';
import { CartItem, PaymentMethod, POSBrand, POSCategory, POSCustomer, POSProduct } from './pos.models';
import { ProductAddedSoundService } from './product-added-sound.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-pos-screen',
  imports: [
    FormsModule,
    ScrollingModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageContainerComponent,
    BarcodeScannerComponent,
    PosCartGridComponent,
    PosSummaryComponent,
  ],
  templateUrl: './pos-screen.component.html',
  styleUrl: './pos-screen.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class POSScreenComponent implements OnDestroy {
  @ViewChild('barcodeInput') barcodeInput?: ElementRef<HTMLInputElement>;
  @ViewChild('productInput') productInput?: ElementRef<HTMLInputElement>;
  @ViewChild('customerInput') customerInput?: ElementRef<HTMLInputElement>;
  readonly cart = signal<CartItem[]>([]);
  readonly products = signal<POSProduct[]>([]);
  readonly customers = signal<POSCustomer[]>([]);
  readonly categories = signal<POSCategory[]>([]);
  readonly brands = signal<POSBrand[]>([]);
  readonly customer = signal<POSCustomer | null>(null);
  readonly methods = signal<PaymentMethod[]>([]);
  readonly warehouses = signal<POSWarehouse[]>([]);
  readonly productImages = signal<Record<string, string>>({});
  readonly subtotal = signal(0);
  readonly itemDiscount = signal(0);
  readonly tax = signal(0);
  readonly total = signal(0);
  readonly itemCount = signal(0);
  readonly scannerOpen = signal(false);
  readonly scannerFeedback = signal('');
  readonly shortcuts = [
    { key: 'Ctrl+N', label: 'New Bill', icon: 'add' },
    { key: 'F2', label: 'Customer', icon: 'person' },
    { key: 'F5', label: 'Payment', icon: 'payments' },
    { key: 'F4', label: 'Hold', icon: 'pause' },
    { key: 'F6', label: 'Print', icon: 'print' },
    { key: 'F3', label: 'Product', icon: 'search' },
    { key: 'F7', label: 'Return', icon: 'assignment_return' },
    { key: 'F8', label: 'Discount', icon: 'percent' },
    { key: 'Esc', label: 'Cancel', icon: 'close' },
  ];
  barcode = '';
  search = '';
  customerSearch = '';
  categoryId = '';
  brandId = '';
  warehouseId = '';
  billDiscount = 0;
  remarks = '';
  lastInvoiceId = '';
  paymentMethod = 'CASH';
  quickAmount = 0;
  private readonly requestedProductImages = new Set<string>();
  private readonly pendingBarcodes = new Set<string>();
  constructor(
    private api: POSApiService,
    private dialog: MatDialog,
    private snack: MatSnackBar,
    private router: Router,
    private addSound: ProductAddedSoundService,
  ) {
    api.methods().subscribe({
      next: (x) => this.methods.set(x.filter((method) => ['CASH', 'UPI'].includes(method.methodCode))),
      error: () => this.snack.open('Payment methods could not be loaded. Refresh the page and retry.', 'Dismiss', { duration: 5000 }),
    });
    api.warehouses().subscribe({
      next: (x) => {
        this.warehouses.set(x);
        this.warehouseId = (x.find((warehouse) => warehouse.isDefault) ?? x[0])?.warehouseId ?? '';
      },
      error: (error) => this.showOrderError(error, 'Warehouses could not be loaded.'),
    });
    api.categories().subscribe({
      next: (items) => this.categories.set(this.flattenCategories(items)),
      error: () => this.snack.open('Product categories could not be loaded.', 'Dismiss', { duration: 4000 }),
    });
    api.brands().subscribe({
      next: (items) => this.brands.set(items),
      error: () => this.snack.open('Product brands could not be loaded.', 'Dismiss', { duration: 4000 }),
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
    this.addSound.unlock();
    const barcode = this.barcode.trim();
    if (!barcode) return;
    this.lookupBarcode(barcode, false);
  }
  openCameraScanner() {
    if (!this.warehouseId) {
      this.snack.open('Select a warehouse before scanning.', undefined, { duration: 3000 });
      return;
    }
    this.addSound.unlock();
    this.scannerFeedback.set('');
    this.scannerOpen.set(true);
  }
  closeCameraScanner() {
    this.scannerOpen.set(false);
    this.barcodeInput?.nativeElement.focus();
  }
  cameraBarcode(barcode: string) {
    this.lookupBarcode(barcode, true);
  }
  findProducts() {
    if (this.search.trim().length < 2 && !this.categoryId && !this.brandId) {
      this.products.set([]);
      return;
    }
    this.api.products(this.search || undefined, undefined, this.warehouseId, undefined, this.categoryId || undefined, this.brandId || undefined).subscribe({
      next: (x) => this.products.set(x),
      error: () => this.snack.open('Product search failed. Please retry.', 'Dismiss', { duration: 5000 }),
    });
  }
  private flattenCategories(items: POSCategory[]): POSCategory[] {
    return items.flatMap((item) => [item, ...this.flattenCategories(item.children ?? [])]);
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
  quickCustomer() {
    this.dialog.open(QuickCustomerDialogComponent, { width: '520px', maxWidth: '94vw' }).afterClosed().subscribe((x: POSCustomer | undefined) => {
      if (x) this.selectCustomer(x);
    });
  }
  add(x: POSProduct): boolean {
    const existing = this.cart().find((y) => y.productId === x.productId);
    const requestedQuantity = (existing?.quantity ?? 0) + 1;
    if (
      !x.negativeStockAllowed &&
      x.availableQuantity != null &&
      x.availableQuantity < requestedQuantity
    ) {
      this.snack.open(`Stock unavailable for ${x.productName}.`, undefined, { duration: 3500 });
      return false;
    }
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
    this.loadProductImage(x.productId);
    this.addSound.play();
    this.barcodeInput?.nativeElement.focus();
    return true;
  }
  remove(x: CartItem) {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: 'Remove bill item',
          message: `Remove ${x.productName} from this bill?`,
          confirmLabel: 'Remove',
          tone: 'danger',
        },
      })
      .afterClosed()
      .subscribe((confirmed: boolean | undefined) => {
        if (!confirmed) return;
        this.cart.update((v) => v.filter((y) => y !== x));
        this.refresh();
      });
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
  payload(payments: object[] = [], isCreditSale = false) {
    return {
      customerId: this.customer()?.customerId,
      warehouseId: this.warehouseId,
      items: this.cart().map((x) => ({
        productId: x.productId,
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
      isCreditSale,
    };
  }
  payment() {
    if (!this.cart().length) {
      this.snack.open('Add at least one product before taking payment.', undefined, { duration: 3000 });
      return;
    }
    if (!this.warehouseId) {
      this.snack.open('Select a warehouse before taking payment.', undefined, { duration: 3000 });
      return;
    }
    if (!this.methods().length) {
      this.snack.open('No active payment methods are available.', 'Dismiss', { duration: 4000 });
      return;
    }
    this.dialog
      .open(PaymentDialogComponent, {
        data: {
          total: this.total(),
          methods: this.methods(),
          preferredMethod: this.paymentMethod,
          quickAmount: this.quickAmount,
          hasCustomer: !!this.customer(),
        },
        width: '720px',
        maxWidth: '96vw',
      })
      .afterClosed()
      .subscribe((result: PaymentResult | undefined) => {
        if (result)
          this.api.invoice(this.payload(result.payments, result.isCreditSale)).subscribe({
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
    if (!this.cart().length) {
      this.snack.open('Add at least one product before holding the bill.', undefined, { duration: 3000 });
      return;
    }
    if (!this.warehouseId) {
      this.snack.open('Select a warehouse before holding the bill.', undefined, { duration: 3000 });
      return;
    }
    this.api.hold(this.payload()).subscribe({
      next: (x) => {
        const notice = this.snack.open(`Bill ${x.invoiceNumber} held safely. No payment or stock was posted.`, 'View Held Bills', {
          duration: 7000,
          panelClass: 'wb-success',
        });
        notice.onAction().subscribe(() => void this.router.navigate(['/pos/holds']));
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

  ngOnDestroy(): void {
    Object.values(this.productImages()).forEach((url) => URL.revokeObjectURL(url));
  }

  private loadProductImage(productId: string): void {
    if (this.requestedProductImages.has(productId)) return;
    this.requestedProductImages.add(productId);
    this.api.productImage(productId).subscribe({
      next: (image) => {
        const url = URL.createObjectURL(image);
        this.productImages.update((images) => ({ ...images, [productId]: url }));
      },
      error: () => undefined,
    });
  }

  private lookupBarcode(value: string, fromCamera: boolean) {
    const barcode = fromCamera ? value : value.trim();
    if (!barcode?.trim() || this.pendingBarcodes.has(barcode)) return;
    if (!this.warehouseId) {
      this.snack.open('Select a warehouse before scanning.', undefined, { duration: 3000 });
      return;
    }
    this.pendingBarcodes.add(barcode);
    if (fromCamera) this.scannerFeedback.set(`Looking up ${barcode}...`);
    this.api.products(undefined, barcode, this.warehouseId, 1).subscribe({
      next: (products) => {
        const product = products[0];
        if (!product) {
          const message = `No active product found for barcode ${barcode}.`;
          if (fromCamera) this.scannerFeedback.set(message);
          this.snack.open(message, undefined, { duration: 3000 });
        } else if (this.add(product)) {
          const message = `${product.productName} added to cart.`;
          if (fromCamera) this.scannerFeedback.set(message);
        } else if (fromCamera) {
          this.scannerFeedback.set(`Stock unavailable for ${product.productName}.`);
        }
        this.barcode = '';
        this.pendingBarcodes.delete(barcode);
      },
      error: () => {
        const message = 'Barcode lookup failed. Check the connection and scan again.';
        if (fromCamera) this.scannerFeedback.set(message);
        this.snack.open(message, 'Dismiss', { duration: 5000 });
        this.pendingBarcodes.delete(barcode);
      },
    });
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

@Component({
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatDialogModule],
  template: `<h2 mat-dialog-title>Add Customer</h2><mat-dialog-content><mat-form-field appearance="outline"><mat-label>Customer name</mat-label><input matInput [(ngModel)]="name" required /></mat-form-field><mat-form-field appearance="outline"><mat-label>Mobile (optional)</mat-label><input matInput [(ngModel)]="mobile" /></mat-form-field><mat-form-field appearance="outline"><mat-label>GSTIN (optional)</mat-label><input matInput [(ngModel)]="gstin" /></mat-form-field></mat-dialog-content><mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" [disabled]="!name.trim()" (click)="save()">Create customer</button></mat-dialog-actions>`,
  styles: [`mat-dialog-content{display:grid;gap:8px;padding-top:8px}mat-form-field{width:100%}`],
})
class QuickCustomerDialogComponent {
  name = ''; mobile = ''; gstin = '';
  constructor(private readonly api: POSApiService, private readonly ref: MatDialogRef<QuickCustomerDialogComponent>, private readonly snack: MatSnackBar) {}
  save() { this.api.quickCustomer({ customerName: this.name.trim(), mobile: this.mobile.trim() || null, gstin: this.gstin.trim() || null }).subscribe({ next: x => this.ref.close(x), error: () => this.snack.open('Customer could not be created.', 'Dismiss', { duration: 4000 }) }); }
}
