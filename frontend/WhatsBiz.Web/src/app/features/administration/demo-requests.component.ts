import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { DataTableComponent, GridRowAction } from '../../shared/components/data-table/data-table.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService, DemoRequestDetail, DemoRequestSummary } from './admin-api.service';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, OperationsWorkspaceComponent, DataTableComponent, StatusChipComponent],
  templateUrl: './demo-requests.component.html',
  styles: [`
    .filters { display:grid; grid-template-columns:2fr 1fr 1fr 1fr auto; gap:10px; align-items:start; }
    .filters mat-form-field { width:100%; }
    .detail { padding:18px; background:var(--wb-surface); border:1px solid var(--wb-border); border-radius:var(--wb-radius-md); }
    .detail h3 { margin:0 0 12px; }
    .detail dl { display:grid; grid-template-columns:minmax(110px,auto) 1fr; gap:9px 14px; margin:0; }
    .detail dt { color:var(--wb-text-secondary); }
    .detail dd { margin:0; overflow-wrap:anywhere; }
    .message { white-space:pre-wrap; }
    .status-editor { display:flex; margin-top:16px; align-items:center; gap:10px; }
    .empty-detail { color:var(--wb-text-secondary); }
    @media(max-width:900px) { .filters { grid-template-columns:1fr 1fr; } .filters .search { grid-column:1/-1; } }
    @media(max-width:600px) { .filters { grid-template-columns:1fr; } .filters .search { grid-column:auto; } .detail dl { grid-template-columns:1fr; gap:3px; } .detail dd { margin-bottom:8px; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DemoRequestsComponent {
  readonly statuses = ['NEW', 'CONTACTED', 'FOLLOW_UP', 'DEMO_SCHEDULED', 'DEMO_COMPLETED', 'TRIAL_STARTED', 'CONVERTED', 'NOT_INTERESTED', 'LOST'];
  readonly rows = signal<DemoRequestSummary[]>([]);
  readonly selected = signal<DemoRequestDetail | null>(null);
  readonly loading = signal(false);
  readonly total = signal(0);
  search = '';
  status = '';
  from = '';
  to = '';
  selectedStatus = '';
  readonly columns = [
    { field: 'referenceNo', headerName: 'Reference' }, { field: 'name', headerName: 'Customer Name' },
    { field: 'mobile', headerName: 'Mobile' }, { field: 'businessName', headerName: 'Business' },
    { field: 'businessType', headerName: 'Business Type' }, { field: 'city', headerName: 'City' },
    { field: 'source', headerName: 'Source' },
    { field: 'createdOn', headerName: 'Created On', valueFormatter: (p: any) => p.value ? new Date(p.value).toLocaleString() : '' },
    { field: 'status', headerName: 'Status' },
  ];
  readonly summaries = computed(() => [
    { label: 'Requests', value: this.total(), subtitle: 'Matching filters', icon: 'campaign', tone: 'primary' as const },
    { label: 'New', value: this.rows().filter(x => x.status === 'NEW').length, subtitle: 'Awaiting contact', icon: 'fiber_new', tone: 'warning' as const },
    { label: 'Scheduled', value: this.rows().filter(x => x.status === 'DEMO_SCHEDULED').length, subtitle: 'Demo pipeline', icon: 'event', tone: 'info' as const },
    { label: 'Converted', value: this.rows().filter(x => x.status === 'CONVERTED').length, subtitle: 'Current view', icon: 'verified', tone: 'success' as const },
  ]);
  constructor(private readonly api: AdminApiService) { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.demoRequests(this.search, this.status, this.from, this.to).subscribe({
      next: result => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
  clear(): void { this.search = ''; this.status = ''; this.from = ''; this.to = ''; this.load(); }
  open(row: DemoRequestSummary): void {
    this.api.demoRequest(row.id).subscribe(value => { this.selected.set(value); this.selectedStatus = value.status; });
  }
  action(event: GridRowAction<DemoRequestSummary>): void { if (event.action === 'view') this.open(event.row); }
  saveStatus(): void {
    const lead = this.selected();
    if (!lead || !this.selectedStatus || this.selectedStatus === lead.status) return;
    this.api.updateDemoRequestStatus(lead.id, this.selectedStatus).subscribe(value => {
      this.selected.set(value);
      this.rows.update(rows => rows.map(row => row.id === value.id ? { ...row, status: value.status } : row));
    });
  }
}
