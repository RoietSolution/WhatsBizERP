import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { FinanceApiService, LedgerRow } from './finance-api.service';
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
  ],
  templateUrl: './party-ledger.component.html',
  styles: [
    `
      .actions,
      .filters {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 6px;
      }
      .filters mat-form-field {
        min-width: 280px;
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
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartyLedgerComponent {
  party = 'customer';
  id = '';
  readonly parties = signal<any[]>([]);
  readonly rows = signal<LedgerRow[]>([]);
  readonly selected = signal<LedgerRow | null>(null);
  readonly opening = computed(() => this.rows()[0]?.balance ?? 0);
  readonly balance = computed(() => this.rows().at(-1)?.balance ?? 0);
  readonly debit = computed(() => this.rows().reduce((a, x) => a + x.debit, 0));
  readonly credit = computed(() => this.rows().reduce((a, x) => a + x.credit, 0));
  readonly summaries = computed(() => [
    {
      label: 'Opening balance',
      value: this.opening(),
      subtitle: 'Period opening',
      icon: 'first_page',
      tone: 'info' as const,
    },
    {
      label: 'Total debit',
      value: this.debit(),
      subtitle: 'Ledger debit',
      icon: 'south_west',
      tone: 'success' as const,
    },
    {
      label: 'Total credit',
      value: this.credit(),
      subtitle: 'Ledger credit',
      icon: 'north_east',
      tone: 'warning' as const,
    },
    {
      label: 'Closing balance',
      value: this.balance(),
      subtitle: 'Running balance',
      icon: 'account_balance_wallet',
      tone: 'primary' as const,
    },
  ]);
  readonly columns = [
    {
      field: 'entryDate',
      headerName: 'Date',
      valueFormatter: (p: any) => String(p.value ?? '').slice(0, 10),
    },
    { field: 'entryType', headerName: 'Voucher type' },
    { field: 'referenceNumber', headerName: 'Voucher / Reference', minWidth: 200 },
    { field: 'debit', headerName: 'Debit' },
    { field: 'credit', headerName: 'Credit' },
    { field: 'balance', headerName: 'Running balance' },
  ];
  constructor(
    private api: FinanceApiService,
    route: ActivatedRoute,
  ) {
    this.party = route.snapshot.data['party'];
    (this.party === 'customer' ? api.customers() : api.suppliers()).subscribe((x) =>
      this.parties.set(x),
    );
  }
  load() {
    if (!this.id) return;
    (this.party === 'customer' ? this.api.customer(this.id) : this.api.supplier(this.id)).subscribe(
      (x) => this.rows.set(x),
    );
  }
  print() {
    window.print();
  }
  exportCsv() {
    const data = this.rows();
    if (!data.length) return;
    const keys = Object.keys(data[0]),
      csv = [
        keys.join(','),
        ...data.map((x: any) =>
          keys.map((k) => `"${String(x[k] ?? '').replaceAll('"', '""')}"`).join(','),
        ),
      ].join('\n'),
      a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    a.download = `${this.party}-ledger.csv`;
    a.click();
  }
}
