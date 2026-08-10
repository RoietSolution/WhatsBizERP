import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { POSApiService } from './pos-api.service';
import { InvoiceList } from './pos.models';
import { PaperSize } from '../printing/paper-size';
@Component({
  imports: [MatButtonModule, MatTableModule],
  templateUrl: './invoice-history.component.html',
  styles: [
    `
      .table {
        overflow: auto;
      }
      table {
        width: 100%;
        min-width: 800px;
      }
    `,
  ],
})
export class InvoiceHistoryComponent {
  readonly cols = ['number', 'date', 'customer', 'total', 'status', 'actions'];
  readonly items = signal<InvoiceList[]>([]);
  constructor(private readonly api: POSApiService) {
    api.invoices().subscribe((x) => this.items.set(x.items));
  }
  print(x: InvoiceList, paper: PaperSize) {
    this.api.print(x.invoiceId, paper);
  }
}
