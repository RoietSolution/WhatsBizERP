import { DecimalPipe } from '@angular/common';
import { Component, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { PrintApiService, PrintTemplate, Printer } from './print-api.service';
import { DEFAULT_PAPER_SIZE, PAPER_SIZES, PaperSize, previewDimensions } from './paper-size';

@Component({
  imports: [DecimalPipe, FormsModule, MatButtonModule],
  templateUrl: './print-preview.component.html',
  styles: [
    `
      header,
      .controls {
        display: flex;
        justify-content: space-between;
        gap: 1rem;
        flex-wrap: wrap;
      }
      .controls {
        padding: 1rem;
        background: #fff;
      }
      .controls label {
        display: grid;
      }
      .stage {
        margin-top: 1rem;
        min-height: 600px;
        overflow: auto;
        background: #9da3aa;
        padding: 2rem;
        text-align: center;
      }
      iframe {
        width: 210mm;
        height: 297mm;
        border: 0;
        background: #fff;
        transform-origin: top center;
      }
      @media (max-width: 800px) {
        iframe {
          width: 100%;
        }
      }
    `,
  ],
})
export class PrintPreviewComponent implements OnDestroy {
  readonly paperSizes = PAPER_SIZES;
  readonly templates = signal<PrintTemplate[]>([]);
  readonly printers = signal<Printer[]>([]);
  readonly url = signal<SafeResourceUrl | null>(null);
  raw = '';
  zoom = 1;
  type = 'SALES_INVOICE';
  paper: PaperSize = DEFAULT_PAPER_SIZE;
  template = '';
  printer = '';
  constructor(
    private api: PrintApiService,
    private safe: DomSanitizer,
  ) {
    api.templates().subscribe((x) => this.templates.set(x));
    api.printers().subscribe((x) => this.printers.set(x));
    api.settings().subscribe((x) => (this.paper = x.paperSize));
  }
  dimensions() { return previewDimensions(this.paper); }
  zoomOut() {
    this.zoom = Math.max(0.5, this.zoom - 0.1);
  }
  zoomIn() {
    this.zoom = Math.min(2, this.zoom + 0.1);
  }
  preview() {
    this.api
      .document({
        documentType: this.type,
        documentNumber: 'PREVIEW-001',
        title: this.type.replaceAll('_', ' '),
        bodyHtml:
          '<h2>Document preview</h2><table><tr><th>Description</th><th>Amount</th></tr><tr><td>Sample item</td><td>100.00</td></tr></table>',
        paperType: this.paper,
        output: 'html',
        templateCode: this.template || null,
      })
      .subscribe((b) => {
        if (this.raw) URL.revokeObjectURL(this.raw);
        this.raw = URL.createObjectURL(b);
        this.url.set(this.safe.bypassSecurityTrustResourceUrl(this.raw));
      });
  }
  print() {
    const frame = document.querySelector('iframe') as HTMLIFrameElement;
    frame?.contentWindow?.print();
  }
  ngOnDestroy() {
    if (this.raw) URL.revokeObjectURL(this.raw);
  }
}
