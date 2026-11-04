import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogActions, MatDialogContent, MatDialogRef, MatDialogTitle } from '@angular/material/dialog';
import { finalize } from 'rxjs';
import { ProductApiService } from './product-api.service';

@Component({
  selector: 'app-product-import-templates-dialog',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatDialogTitle, MatDialogContent, MatDialogActions],
  template: `
    <h2 mat-dialog-title>Product Import Templates</h2>
    <mat-dialog-content>
      <section class="template-options">
        <article class="template-option">
          <div><h3>Standard ERP Product Template</h3><p>Existing 13-column ERP product import format.</p></div>
          <button mat-stroked-button type="button" (click)="downloadStandard()" [disabled]="busy()">
            {{ busy() ? 'Downloading…' : 'Download ERP template' }}
          </button>
        </article>
        <article class="template-option shopify-option">
          <div><h3>Shopify Product Template</h3><p>Shopify’s 49 source columns plus ERP Selling Price and ERP MRP.</p></div>
          <label>Supplier
            <select [(ngModel)]="supplierId" [disabled]="busy()">
              <option value="">Select a supplier</option>
              @for (supplier of suppliers(); track supplier.supplierId) {
                <option [value]="supplier.supplierId">{{ supplier.supplierName }} ({{ supplier.supplierCode }})</option>
              }
            </select>
          </label>
          <button mat-stroked-button type="button" (click)="downloadShopify()" [disabled]="busy() || !supplierId">
            {{ busy() ? 'Downloading…' : 'Download Shopify template' }}
          </button>
        </article>
      </section>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button type="button" (click)="ref.close()">Close</button></mat-dialog-actions>
  `,
  styles: [`
    :host{display:block;color:var(--wb-text)}mat-dialog-content{max-height:75vh}
    .template-options{display:grid;gap:1rem}.template-option{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:.8rem;align-items:center;padding:1rem;border:1px solid var(--wb-border);border-radius:12px;background:var(--wb-surface)}
    .template-option h3{margin:0 0 .35rem;font-size:1rem}.template-option p{margin:0;color:var(--wb-text-secondary);font-size:.88rem}
    .shopify-option{grid-template-columns:minmax(0,1fr) auto}.shopify-option label{grid-column:1/-1;display:grid;gap:.35rem;font-size:.88rem;font-weight:600}
    select{width:100%;min-height:42px;padding:.55rem .7rem;border:1px solid var(--wb-border);border-radius:8px;background:var(--wb-surface);color:var(--wb-text);font:inherit;box-sizing:border-box}
    .error{color:var(--wb-danger,#b42318)}@media(max-width:560px){.template-option,.shopify-option{grid-template-columns:1fr}.template-option button{justify-self:start}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductImportTemplatesDialogComponent {
  readonly ref = inject(MatDialogRef<ProductImportTemplatesDialogComponent>);
  private readonly api = inject(ProductApiService);
  readonly suppliers = signal<{ supplierId: string; supplierCode: string; supplierName: string }[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  supplierId = '';

  constructor() {
    this.api.supplierOptions().subscribe({
      next: suppliers => this.suppliers.set(suppliers),
      error: () => this.error.set('Unable to load tenant suppliers for the Shopify template.'),
    });
  }

  downloadStandard(): void {
    this.download(this.api.template(), 'product-import-template.xlsx', 'Unable to download the ERP product template.');
  }

  downloadShopify(): void {
    if (!this.supplierId) return;
    this.download(this.api.shopifySupplierCatalogTemplate(this.supplierId), 'shopify-supplier-catalog-template.xlsx', 'Unable to download the Shopify template. Check your product permission and selected supplier.');
  }

  private download(request: import('rxjs').Observable<Blob>, filename: string, errorMessage: string): void {
    this.busy.set(true);
    this.error.set('');
    request.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: blob => {
        if (!(blob instanceof Blob) || blob.size === 0) {
          this.error.set('The server returned an empty template file.');
          return;
        }
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.click();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      },
      error: (error: { status?: number }) => this.error.set(error.status ? `${errorMessage} (HTTP ${error.status}).` : errorMessage),
    });
  }
}
