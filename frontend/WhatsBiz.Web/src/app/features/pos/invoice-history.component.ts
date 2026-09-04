import { DatePipe } from '@angular/common';
import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { POSApiService } from './pos-api.service';
import { InvoiceList } from './pos.models';
import { PaperSize } from '../printing/paper-size';
@Component({
  imports: [DatePipe, MatButtonModule, MatTableModule],
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
      .print-actions {
        display: flex;
        align-items: center;
        gap: 6px;
        min-height: 52px;
      }
      .print-size {
        --mat-button-outlined-container-height: 30px;
        min-width: 44px;
        padding-inline: 8px;
        font-size: 12px;
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
