import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { ValueFormatterParams } from 'ag-grid-community';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { InventoryApiService } from './inventory-api.service';
import { Balance, ProductOption, WarehouseOption } from './inventory.models';
@Component({
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatPaginatorModule,
    MatSelectModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    DataTableComponent,
  ],
  templateUrl: './stock-balance.component.html',
  styles: [
    `
      .actions {
        display: flex;
        flex-wrap: wrap;
      }
      .filters {
        display: grid;
        grid-template-columns: repeat(4, minmax(160px, 1fr));
        gap: 12px;
        width: 100%;
      }
      .context-card {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .context-card h3 {
        margin-top: 0;
      }
      .context-card p {
        color: var(--wb-text-secondary);
      }
      .inventory-cards { display:grid; grid-template-columns:repeat(auto-fill,minmax(240px,1fr)); gap:12px; }
      .inventory-card { display:grid; grid-template-columns:auto 1fr auto; gap:10px; padding:16px; background:var(--wb-surface); border:1px solid var(--wb-border); border-radius:var(--wb-radius-md); }
      .inventory-card .product-icon { display:grid; width:40px; height:40px; color:var(--wb-primary); background:var(--wb-primary-soft); border-radius:10px; place-items:center; }
      .product-copy { min-width:0; display:flex; flex-direction:column; gap:3px; } .product-copy strong { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .product-copy small { color:var(--wb-text-secondary); }
      .stock-status { color:var(--wb-success); font-size:12px; font-weight:600; } .stock-status.low { color:var(--wb-warning); } .stock-status.out { color:var(--wb-danger); }
      .inventory-card dl { grid-column:1/-1; display:grid; grid-template-columns:1fr auto; gap:6px; margin:4px 0 0; padding-top:10px; border-top:1px solid var(--wb-border); font-size:13px; } .inventory-card dt { color:var(--wb-text-secondary); } .inventory-card dd { margin:0; font-weight:600; }
      .hidden { display:none !important; }
      dl {
        display: grid;
        grid-template-columns: 1fr auto;
        gap: 10px;
      }
      dd {
        margin: 0;
        font-weight: 700;
      }
      @media (max-width: 767px) {
        .filters {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockBalanceComponent {
  readonly view = signal<'card'|'table'>('card');
  readonly items = signal<Balance[]>([]);
  readonly products = signal<ProductOption[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly search = new FormControl('', { nonNullable: true });
  readonly warehouse = new FormControl('', { nonNullable: true });
  readonly product = new FormControl('', { nonNullable: true });
  readonly available = computed(() => this.items().reduce((a, x) => a + x.quantityAvailable, 0));
  readonly reserved = computed(() => this.items().reduce((a, x) => a + x.quantityReserved, 0));
  readonly value = computed(() => this.items().reduce((a, x) => a + x.stockValue, 0));
  readonly summaries = computed(() => [
    {
      label: 'Stock records',
      value: this.total(),
      subtitle: 'Current filters',
      icon: 'inventory_2',
      tone: 'primary' as const,
    },
    {
      label: 'Available',
      value: this.available(),
      subtitle: 'Visible stock',
      icon: 'check_circle',
      tone: 'success' as const,
    },
    {
      label: 'Reserved',
      value: this.reserved(),
      subtitle: 'Committed stock',
      icon: 'lock',
      tone: 'info' as const,
    },
    {
      label: 'Stock value',
      value: this.value().toFixed(2),
      subtitle: 'Visible value',
      icon: 'currency_rupee',
      tone: 'warning' as const,
    },
  ]);
  readonly columns = [
    { field: 'productCode', headerName: 'Code' },
    { field: 'productName', headerName: 'Product', minWidth: 220 },
    { field: 'warehouseName', headerName: 'Warehouse' },
    { field: 'zoneCode', headerName: 'Zone' },
    { field: 'binCode', headerName: 'Bin' },
    { field: 'quantityOnHand', headerName: 'On hand' },
    { field: 'quantityReserved', headerName: 'Reserved' },
    { field: 'quantityAvailable', headerName: 'Available' },
    {
      field: 'stockValue',
      headerName: 'Stock value',
      valueFormatter: (params: ValueFormatterParams<Balance>) => Number(params.value ?? 0).toFixed(2),
    },
  ];
  page = 1;
  size = 20;
  constructor(private api: InventoryApiService) {
    api.products().subscribe((x) => this.products.set(x.items));
    api.warehouses().subscribe((x) => this.warehouses.set(x));
    this.load();
  }
  load() {
    this.loading.set(true);
    this.api
      .balances({
        search: this.search.value || undefined,
        warehouseId: this.warehouse.value || undefined,
        productId: this.product.value || undefined,
        pageNumber: this.page,
        pageSize: this.size,
      })
      .subscribe({
        next: (x) => {
          this.items.set(x.items);
          this.total.set(x.totalCount);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }
  reset() {
    this.search.setValue('');
    this.warehouse.setValue('');
    this.product.setValue('');
    this.page = 1;
    this.load();
  }
  paged(e: PageEvent) {
    this.page = e.pageIndex + 1;
    this.size = e.pageSize;
    this.load();
  }
  export() {
    this.api
      .export(
        this.search.value || undefined,
        this.warehouse.value || undefined,
        this.product.value || undefined,
      )
      .subscribe((blob) => {
        const url = URL.createObjectURL(blob),
          anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'inventory-balances.xlsx';
        anchor.click();
        URL.revokeObjectURL(url);
      });
  }
}
