import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import {
  DataTableComponent,
  GridRowAction,
} from '../../shared/components/data-table/data-table.component';
import { LoadingOverlayComponent } from '../../shared/components/loading-overlay/loading-overlay.component';
import { PurchaseApiService } from './purchase-api.service';
import { PurchaseList } from './purchase.models';
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    DataTableComponent,
    LoadingOverlayComponent,
  ],
  templateUrl: './purchase-list.component.html',
  styles: [
    `
      .actions {
        display: flex;
        flex-wrap: wrap;
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
      dl {
        display: grid;
        grid-template-columns: 1fr auto;
        gap: 10px;
      }
      dd {
        margin: 0;
        font-weight: 700;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseListComponent {
  readonly rows = signal<PurchaseList[]>([]);
  readonly loading = signal(false);
  readonly pending = computed(() => this.rows().filter((x) => x.status === 'DRAFT').length);
  readonly received = computed(() => this.rows().filter((x) => x.status === 'POSTED').length);
  readonly outstanding = computed(() => this.rows().reduce((a, x) => a + x.balanceAmount, 0));
  readonly summaries = computed(() => [
    {
      label: 'Purchase records',
      value: this.rows().length,
      subtitle: 'Current view',
      icon: 'receipt_long',
      tone: 'primary' as const,
    },
    {
      label: 'Pending',
      value: this.pending(),
      subtitle: 'Awaiting receipt',
      icon: 'pending_actions',
      tone: 'warning' as const,
    },
    {
      label: 'Received',
      value: this.received(),
      subtitle: 'Posted purchases',
      icon: 'inventory',
      tone: 'success' as const,
    },
    {
      label: 'Outstanding',
      value: this.outstanding(),
      subtitle: 'Supplier payable',
      icon: 'account_balance_wallet',
      tone: 'danger' as const,
    },
  ]);
  readonly columns = [
    { field: 'invoiceNumber', headerName: 'Purchase number', minWidth: 170 },
    {
      field: 'invoiceDate',
      headerName: 'Date',
      valueFormatter: (p: any) => String(p.value ?? '').slice(0, 10),
    },
    { field: 'supplierName', headerName: 'Supplier', minWidth: 200 },
    { field: 'supplierInvoiceNo', headerName: 'Supplier invoice' },
    { field: 'warehouseName', headerName: 'Warehouse' },
    {
      field: 'grandTotal',
      headerName: 'Total',
      valueFormatter: (p: any) =>
        Number(p.value ?? 0).toLocaleString('en-IN', { style: 'currency', currency: 'INR' }),
    },
    {
      field: 'balanceAmount',
      headerName: 'Outstanding',
      valueFormatter: (p: any) =>
        Number(p.value ?? 0).toLocaleString('en-IN', { style: 'currency', currency: 'INR' }),
    },
    { field: 'status', headerName: 'Status' },
  ];
  search = '';
  status = '';
  constructor(
    private api: PurchaseApiService,
    private router: Router,
  ) {
    this.load();
  }
  load() {
    this.loading.set(true);
    this.api.list(this.search, this.status).subscribe({
      next: (x) => {
        this.rows.set(x.items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
  view(x: PurchaseList) {
    this.router.navigate(['/purchases', x.purchaseInvoiceId]);
  }
  action(e: GridRowAction<PurchaseList>) {
    if (e.action === 'view') this.view(e.row);
    else if (e.action === 'edit')
      this.router.navigate(['/purchases', e.row.purchaseInvoiceId, 'edit']);
    else if (e.action === 'print') window.print();
  }
  download(b: Blob, n: string) {
    const u = URL.createObjectURL(b),
      a = document.createElement('a');
    a.href = u;
    a.download = n;
    a.click();
    URL.revokeObjectURL(u);
  }
  export() {
    this.api.export().subscribe((x) => this.download(x, 'purchases.xlsx'));
  }
  template() {
    this.api.template().subscribe((x) => this.download(x, 'purchase-template.xlsx'));
  }
  print() {
    window.print();
  }
}
