import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { Outstanding, ReceivablesApiService } from './receivables-api.service';
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
  templateUrl: './outstanding-ageing.component.html',
  styles: [
    `
      .actions,
      .filters {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 6px;
      }
      .ageing-strip {
        display: grid;
        grid-template-columns: repeat(4, 1fr);
        gap: 8px;
        margin-bottom: 12px;
      }
      .ageing-strip button {
        display: flex;
        padding: 14px;
        color: var(--wb-text-secondary);
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: 8px;
        align-items: center;
        justify-content: space-between;
        font: inherit;
        cursor: pointer;
      }
      .ageing-strip button:hover {
        color: var(--wb-primary);
        border-color: var(--wb-primary);
      }
      .ageing-strip strong {
        font-size: 17px;
      }
      .ageing-strip button:nth-child(2) {
        border-bottom-color: var(--wb-info);
      }
      .ageing-strip button:nth-child(3) {
        border-bottom-color: var(--wb-warning);
      }
      .ageing-strip button:nth-child(4) {
        border-bottom-color: var(--wb-danger);
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
      .insight > strong {
        color: var(--wb-primary);
        font-size: 28px;
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
      @media (max-width: 600px) {
        .ageing-strip {
          grid-template-columns: repeat(2, 1fr);
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OutstandingAgeingComponent implements OnInit {
  party = 'customer';
  ageing = false;
  bucket = '';
  readonly rows = signal<Outstanding[]>([]);
  readonly selected = signal<Outstanding | null>(null);
  readonly total = computed(() => this.rows().reduce((n, x) => n + x.outstandingAmount, 0));
  readonly summaries = computed(() => [
    {
      label: this.party === 'customer' ? 'Receivables' : 'Payables',
      value: this.total(),
      subtitle: 'Total outstanding',
      icon: 'account_balance_wallet',
      tone: 'primary' as const,
    },
    {
      label: '0–30 days',
      value: this.bucketTotal('0-30'),
      subtitle: 'Current',
      icon: 'check_circle',
      tone: 'success' as const,
    },
    {
      label: '31–90 days',
      value: this.bucketTotal('31-60') + this.bucketTotal('61-90'),
      subtitle: 'Attention',
      icon: 'warning',
      tone: 'warning' as const,
    },
    {
      label: '90+ days',
      value: this.bucketTotal('ABOVE_90'),
      subtitle: 'Critical ageing',
      icon: 'error',
      tone: 'danger' as const,
    },
  ]);
  readonly columns = [
    { field: 'partyCode', headerName: 'Code' },
    { field: 'partyName', headerName: 'Party', minWidth: 190 },
    { field: 'invoiceNumber', headerName: 'Invoice' },
    { field: 'invoiceDate', headerName: 'Invoice date' },
    { field: 'dueDate', headerName: 'Due date' },
    { field: 'invoiceAmount', headerName: 'Invoice amount' },
    { field: 'paidAmount', headerName: 'Paid' },
    { field: 'outstandingAmount', headerName: 'Outstanding' },
    { field: 'ageDays', headerName: 'Age (days)' },
    { field: 'ageBucket', headerName: 'Bucket' },
  ];
  constructor(
    private api: ReceivablesApiService,
    route: ActivatedRoute,
  ) {
    this.party = route.snapshot.data['party'];
    this.ageing = route.snapshot.data['ageing'] ?? false;
  }
  ngOnInit() {
    this.load();
  }
  load() {
    this.api
      .outstanding(this.party, undefined, this.ageing ? this.bucket : undefined)
      .subscribe((x) => this.rows.set(x));
  }
  bucketTotal(bucket: string) {
    return this.rows()
      .filter((x) => x.ageBucket === bucket)
      .reduce((n, x) => n + x.outstandingAmount, 0);
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
    a.download = `${this.party}-${this.ageing ? 'ageing' : 'outstanding'}.csv`;
    a.click();
  }
  print() {
    window.print();
  }
}
