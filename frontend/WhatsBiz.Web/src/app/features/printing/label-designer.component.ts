import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { PrintApiService } from './print-api.service';
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
  name = 'Sample Product';
  code = 'PRD-001';
  barcode = '8901234567897';
  size = '50,25';
  quantity = 1;
  url = signal<SafeResourceUrl | null>(null);
  constructor(
    private api: PrintApiService,
    private safe: DomSanitizer,
  ) {}
  preview() {
    const [w, h] = this.size.split(',').map(Number);
    this.api
      .label({
        labelType: this.labelType,
        productName: this.name,
        productCode: this.code,
        barcode: this.barcode,
        price: 99,
        mrp: 110,
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
