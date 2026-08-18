import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { FinanceApiService } from './finance-api.service';
@Component({
  imports: [
    RouterLink,
    MatButtonModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    DataTableComponent,
  ],
  templateUrl: './book.component.html',
  styles: [
    `
      .actions,
      .filter-placeholders {
        display: flex;
        flex-wrap: wrap;
        gap: 4px;
      }
      .insight {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .insight h3 {
        margin-top: 0;
      }
      .insight p {
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
      nav {
        display: flex;
        flex-direction: column;
      }
      nav a {
        padding: 8px;
        color: var(--wb-primary);
        text-decoration: none;
        border-bottom: 1px solid var(--wb-border);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinanceBookComponent {
  kind = 'cash';
  title = 'Cash Book';
  readonly rows = signal<any[]>([]);
  readonly selected = signal<any | null>(null);
  readonly totalIn = computed(() =>
    this.rows().reduce((s, x) => s + (x.amountIn ?? x.debitTotal ?? 0), 0),
  );
  readonly totalOut = computed(() =>
    this.rows().reduce((s, x) => s + (x.amountOut ?? x.creditTotal ?? 0), 0),
  );
  readonly balance = computed(() => this.totalIn() - this.totalOut());
  readonly summaries = computed(() => [
    {
      label: "Today's collection",
      value: this.money(this.totalIn()),
      subtitle: 'Cash inflow',
      icon: 'south_west',
      tone: 'success' as const,
    },
    {
      label: "Today's payments",
      value: this.money(this.totalOut()),
      subtitle: 'Cash outflow',
      icon: 'north_east',
      tone: 'danger' as const,
    },
    {
      label: this.kind === 'bank' ? 'Bank balance' : 'Cash in hand',
      value: this.money(this.balance()),
      subtitle: 'Running balance',
      icon: this.kind === 'bank' ? 'account_balance' : 'payments',
      tone: 'primary' as const,
    },
    {
      label: 'Net cash flow',
      value: this.money(this.balance()),
      subtitle: 'Inflow less outflow',
      icon: 'account_balance_wallet',
      tone: 'info' as const,
    },
  ]);
  readonly columns = [
    {
      field: 'entryDate',
      headerName: 'Date',
      valueFormatter: (p: any) => String(p.value ?? '').slice(0, 10),
    },
    { field: 'entryType', headerName: 'Voucher type' },
    { field: 'transactionType', headerName: 'Transaction' },
    { field: 'referenceNumber', headerName: 'Reference', minWidth: 180 },
    { field: 'mode', headerName: 'Payment mode' },
    {
      field: 'amountIn',
      headerName: 'In / Debit',
      valueGetter: (p: any) => p.data?.amountIn ?? p.data?.debitTotal ?? 0,
      valueFormatter: (p: any) => this.money(p.value),
    },
    {
      field: 'amountOut',
      headerName: 'Out / Credit',
      valueGetter: (p: any) => p.data?.amountOut ?? p.data?.creditTotal ?? 0,
      valueFormatter: (p: any) => this.money(p.value),
    },
    {
      field: 'balance',
      headerName: 'Running balance',
      valueGetter: (p: any) => p.data?.balance ?? ((p.data?.debitTotal ?? 0) - (p.data?.creditTotal ?? 0)),
      valueFormatter: (p: any) => this.money(p.value),
    },
  ];
  constructor(
    private api: FinanceApiService,
    route: ActivatedRoute,
  ) {
    this.kind = route.snapshot.data['kind'];
    this.title =
      this.kind === 'cash' ? 'Cash Book' : this.kind === 'bank' ? 'Bank Book' : 'Day Book';
    this.refresh();
  }
  refresh() {
    if (this.kind === 'cash') this.api.cash().subscribe((x) => this.rows.set(x));
    else if (this.kind === 'bank') this.api.bank().subscribe((x) => this.rows.set(x));
    else this.api.day().subscribe((x) => this.rows.set(x));
  }
  print() {
    window.print();
  }
  money(value: unknown): string {
    const amount = Number(value ?? 0);
    return Number.isFinite(amount) ? amount.toFixed(2) : '0.00';
  }
  exportCsv() {
    const data = this.rows();
    if (!data.length) return;
    const keys = Object.keys(data[0]),
      csv = [
        keys.join(','),
        ...data.map((x) =>
          keys.map((k) => {
            const value = ['amountIn', 'amountOut', 'balance', 'debitTotal', 'creditTotal'].includes(k)
              ? this.money(x[k])
              : x[k] ?? '';
            return `"${String(value).replaceAll('"', '""')}"`;
          }).join(','),
        ),
      ].join('\n'),
      a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    a.download = `${this.kind}-book.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
