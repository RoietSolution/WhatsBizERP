import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { PrintApiService } from './print-api.service';
@Component({
  imports: [FormsModule, MatButtonModule],
  templateUrl: './barcode-generator.component.html',
  styles: [
    `
      section {
        display: flex;
        gap: 1rem;
        align-items: end;
        flex-wrap: wrap;
      }
      label {
        display: grid;
      }
      input,
      select {
        padding: 0.65rem;
      }
      article {
        margin-top: 2rem;
        background: #fff;
        padding: 2rem;
        text-align: center;
      }
      img {
        display: block;
        max-width: 100%;
        margin: auto;
      }
    `,
  ],
})
export class BarcodeGeneratorComponent {
  value = '8901234567897';
  format = 'CODE128';
  url = signal<SafeResourceUrl | null>(null);
  blob?: Blob;
  constructor(
    private api: PrintApiService,
    private safe: DomSanitizer,
  ) {}
  generate() {
    const call =
      this.format === 'QR'
        ? this.api.qrcode({ value: this.value, pixelsPerModule: 8 })
        : this.api.barcode({
            value: this.value,
            format: this.format,
            width: 400,
            height: 120,
            showText: true,
          });
    call.subscribe((b) => {
      this.blob = b;
      this.url.set(this.safe.bypassSecurityTrustResourceUrl(URL.createObjectURL(b)));
    });
  }
  download() {
    if (!this.blob) return;
    const a = document.createElement('a');
    a.href = URL.createObjectURL(this.blob);
    a.download = `${this.format.toLowerCase()}.svg`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
