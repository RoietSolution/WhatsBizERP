import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize } from 'rxjs';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { ReportViewerComponent } from '../../shared/components/report-viewer/report-viewer.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { GstApiService, GstFilter, GstRow } from './gst-api.service';
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    ReportViewerComponent,
    StatusChipComponent,
  ],
  templateUrl: './gst-report.component.html',
  styles: [
    `
      .actions,
      .filters {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 5px;
      }
      .filters mat-form-field {
        min-width: 150px;
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
      .insight nav {
        display: flex;
        flex-direction: column;
      }
      .insight nav a {
        padding: 7px 0;
        color: var(--wb-primary);
        text-decoration: none;
        border-bottom: 1px solid var(--wb-border);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GstReportComponent {
  title = 'GST Report';
  report = 'tax-summary';
  from = new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().slice(0, 10);
  to = new Date().toISOString().slice(0, 10);
  gstRate?: number;
  readonly rows = signal<GstRow[]>([]);
  readonly loading = signal(false);
  readonly lastGenerated = signal('Not generated');
  readonly summaries = computed(() => [
    {
      label: 'Taxable Value',
      value: this.total('taxableAmount'),
      subtitle: 'Current filters',
      icon: 'currency_rupee',
      tone: 'primary' as const,
    },
    {
      label: 'CGST + SGST',
      value: this.total('cgstAmount') + this.total('sgstAmount'),
      subtitle: 'Intra-state tax',
      icon: 'account_balance',
      tone: 'info' as const,
    },
    {
      label: 'IGST',
      value: this.total('igstAmount'),
      subtitle: 'Inter-state tax',
      icon: 'swap_horiz',
      tone: 'warning' as const,
    },
    {
      label: 'GST Liability',
      value: this.total('totalTax'),
      subtitle: 'Total tax',
      icon: 'percent',
      tone: 'danger' as const,
    },
  ]);
  readonly chart = computed(() => ({
    type: 'bar' as const,
    height: 320,
    currency: true,
    categories: ['Taxable', 'CGST', 'SGST', 'IGST', 'CESS'],
    series: [
      {
        name: 'Amount',
        data: [
          this.total('taxableAmount'),
          this.total('cgstAmount'),
          this.total('sgstAmount'),
          this.total('igstAmount'),
          this.total('cessAmount'),
        ],
      },
    ],
  }));
  readonly columns = [
    { field: 'documentNumber', headerName: 'Document / Type', minWidth: 180 },
    { field: 'documentDate', headerName: 'Date' },
    { field: 'partyName', headerName: 'Party', minWidth: 180 },
    { field: 'partyGstin', headerName: 'GSTIN' },
    { field: 'hsnCode', headerName: 'HSN/SAC' },
    { field: 'gstRate', headerName: 'Rate %' },
    { field: 'quantity', headerName: 'Quantity' },
    { field: 'taxableAmount', headerName: 'Taxable' },
    { field: 'cgstAmount', headerName: 'CGST' },
    { field: 'sgstAmount', headerName: 'SGST' },
    { field: 'igstAmount', headerName: 'IGST' },
    { field: 'totalTax', headerName: 'Total Tax' },
  ];
  constructor(
    private api: GstApiService,
    route: ActivatedRoute,
    private snack: MatSnackBar,
  ) {
    this.title = route.snapshot.data['title'];
    this.report = route.snapshot.data['report'];
    this.load();
  }
  filter(): GstFilter {
    return { from: this.from, to: this.to, gstRate: this.gstRate };
  }
  load() {
    this.loading.set(true);
    this.api
      .report(this.report, this.filter())
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (x) => {
          this.rows.set(x);
          this.lastGenerated.set(new Date().toLocaleTimeString('en-IN'));
        },
        error: () => this.snack.open('Unable to load GST report', 'Close', { duration: 3500 }),
      });
  }
  reset() {
    this.from = new Date(new Date().getFullYear(), new Date().getMonth(), 1)
      .toISOString()
      .slice(0, 10);
    this.to = new Date().toISOString().slice(0, 10);
    this.gstRate = undefined;
    this.load();
  }
  total(f: keyof GstRow) {
    return this.rows().reduce((n, r) => n + (Number(r[f]) || 0), 0);
  }
  download(format: string) {
    this.api.export(this.report, format, this.filter()).subscribe({
      next: (b) => {
        const a = document.createElement('a');
        a.href = URL.createObjectURL(b);
        a.download = `${this.report}.${format}`;
        a.click();
        URL.revokeObjectURL(a.href);
      },
      error: () => this.snack.open('Export failed', 'Close', { duration: 3500 }),
    });
  }
  print() {
    window.print();
  }
  saveFilter() {
    this.snack.open('Report filters saved for this session.', undefined, { duration: 2200 });
  }
}
