import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { InventoryApiService } from './inventory-api.service';
import { InventoryTransaction, TransactionList, WarehouseOption } from './inventory.models';

@Component({
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './inventory-transactions.component.html',
  styles: [
    `
      .tools {
        display: flex;
        gap: 1rem;
        flex-wrap: wrap;
      }
      .table {
        overflow: auto;
      }
      table {
        width: 100%;
        min-width: 800px;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InventoryTransactionsComponent {
  readonly cols = ['number', 'date', 'type', 'warehouse', 'quantity', 'cost'];
  readonly transactionTypes = [
    'ADJUSTMENT_IN',
    'ADJUSTMENT_OUT',
    'TRANSFER_OUT',
    'TRANSFER_IN',
    'RESERVATION',
    'RELEASE',
    'PURCHASE',
    'SALE',
    'RETURN',
    'MANUFACTURING',
  ];
  readonly items = signal<TransactionList[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly total = signal(0);
  readonly filters;
  page = 1;
  size = 20;
  constructor(
    private readonly api: InventoryApiService,
    fb: FormBuilder,
    private readonly dialog: MatDialog,
  ) {
    this.filters = fb.group({
      search: [''],
      warehouseId: [''],
      transactionType: [''],
      from: [''],
      to: [''],
    });
    api.warehouses().subscribe((x) => this.warehouses.set(x));
    this.load();
  }
  load() {
    const f = this.filters.getRawValue();
    this.api
      .transactions({
        search: f.search || undefined,
        warehouseId: f.warehouseId || undefined,
        transactionType: f.transactionType || undefined,
        from: f.from || undefined,
        to: f.to || undefined,
        pageNumber: this.page,
        pageSize: this.size,
      })
      .subscribe((x) => {
        this.items.set(x.items);
        this.total.set(x.totalCount);
      });
  }
  paged(e: PageEvent) {
    this.page = e.pageIndex + 1;
    this.size = e.pageSize;
    this.load();
  }
  view(x: TransactionList) {
    this.api
      .transaction(x.transactionId)
      .subscribe((t) => this.dialog.open(TransactionDialogComponent, { data: t, width: '800px' }));
  }
}
@Component({
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './transaction-dialog.component.html',
})
export class TransactionDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) readonly data: InventoryTransaction) {}
}
