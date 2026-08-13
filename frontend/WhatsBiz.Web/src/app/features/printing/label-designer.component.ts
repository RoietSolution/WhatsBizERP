import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { PrintApiService } from './print-api.service';
import { ProductApiService } from '../products/product-api.service';
import { ProductListItem } from '../products/product.models';
@Component({
  imports: [FormsModule, MatButtonModule],
  templateUrl: './label-designer.component.html',
  styles: [
    `
      section {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        background: #fff;
        padding: 1rem;
      }
      label {
        display: grid;
      }
      input,
      select {
        padding: 0.6rem;
      }
      iframe {
        margin-top: 1rem;
        width: 100%;
        height: 500px;
        background: #ddd;
        border: 0;
      }
      @media (max-width: 700px) {
        section {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class LabelDesignerComponent {
  labelType = 'PRODUCT';
  name = '';
  code = '';
  barcode = '';
  price?: number;
  mrp?: number;
  size = '50,25';
  quantity = 1;
  selectedProductId = '';
  readonly products = signal<ProductListItem[]>([]);
  url = signal<SafeResourceUrl | null>(null);
  constructor(
    private api: PrintApiService,
    private productsApi: ProductApiService,
    private safe: DomSanitizer,
  ) {
    productsApi.search({ sortBy: 'productName', descending: false, pageNumber: 1, pageSize: 200, isActive: true })
      .subscribe((result) => this.products.set(result.items));
  }
  selectProduct(): void {
    if (!this.selectedProductId) return;
    this.productsApi.get(this.selectedProductId).subscribe((product) => {
      this.name = product.productName;
      this.code = product.productCode;
      this.barcode = product.barcode ?? '';
      this.price = product.sellingPrice;
      this.mrp = product.mrp;
    });
  }
  preview() {
    const [w, h] = this.size.split(',').map(Number);
    this.api
      .label({
        labelType: this.labelType,
        productName: this.name,
        productCode: this.code,
        barcode: this.barcode,
        price: this.price,
        mrp: this.mrp,
        widthMm: w,
        heightMm: h,
        quantity: this.quantity,
        output: 'html',
      })
      .subscribe((b) =>
        this.url.set(this.safe.bypassSecurityTrustResourceUrl(URL.createObjectURL(b))),
      );
  }
}
