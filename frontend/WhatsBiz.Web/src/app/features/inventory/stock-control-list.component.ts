import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { StockControlApiService, MovementRow, StockControlRow } from './stock-control-api.service';
import { WarehouseOption } from './inventory.models';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './stock-control-list.component.html',
  styles: [
    `
      .filters {
        display: flex;
        gap: 1rem;
        align-items: center;
        flex-wrap: wrap;
      }
      .table {
        overflow: auto;
      }
      table {
        border-collapse: collapse;
        width: 100%;
        min-width: 720px;
      }
      th,
      td {
        padding: 0.75rem;
        text-align: left;
        border-bottom: 1px solid #ddd;
      }
      span {
        padding: 0.25rem 0.55rem;
        border-radius: 1rem;
        background: #e8eef8;
      }
      @media (max-width: 700px) {
        .filters mat-form-field {
          width: 100%;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockControlListComponent implements OnInit {
  readonly rows = signal<StockControlRow[]>([]);
  readonly movements = signal<MovementRow[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly loading = signal(false);
  readonly alertTypes = [
    'LOW_STOCK',
    'OUT_OF_STOCK',
    'NEGATIVE_STOCK',
    'OVER_STOCK',
    'EXPIRING_SOON',
    'EXPIRED_STOCK',
  ];
  mode = 'reorder';
  title = 'Reorder Suggestions';
  search = '';
  warehouseId = '';
  status = '';
  constructor(
    private readonly api: StockControlApiService,
    route: ActivatedRoute,
  ) {
    this.mode = route.snapshot.data['mode'] ?? 'reorder';
    this.title = route.snapshot.data['title'] ?? 'Inventory Stock Control';
  }
  ngOnInit() {
    this.api.warehouses().subscribe((x) => this.warehouses.set(x));
    this.load();
  }
  load() {
    this.loading.set(true);
    const q = {
      search: this.search,
      warehouseId: this.warehouseId,
      status: this.status,
      pageNumber: 1,
      pageSize: 200,
    };
    if (this.mode === 'movement')
      this.api.movements(q).subscribe((x) => {
        this.movements.set(x.items);
        this.loading.set(false);
      });
    else if (this.mode === 'alerts')
      this.api.alerts(q).subscribe((x) => {
        this.rows.set(x);
        this.loading.set(false);
      });
    else
      this.api.reorder(q).subscribe((x) => {
        this.rows.set(x);
        this.loading.set(false);
      });
  }
  exportCsv() {
    const values: any[] = this.mode === 'movement' ? this.movements() : this.rows();
    if (!values.length) return;
    const keys = Object.keys(values[0]);
    const csv = [
      keys.join(','),
      ...values.map((x) =>
        keys.map((k) => `"${String(x[k] ?? '').replaceAll('"', '""')}"`).join(','),
      ),
    ].join('\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    a.download = `${this.mode}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
