import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService } from './admin-api.service';
@Component({
  imports: [
    RouterLink,
    MatButtonModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    DataTableComponent,
    StatusChipComponent,
  ],
  templateUrl: './audit-history.component.html',
  styles: [
    `
      .filters {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
      }
      .context {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .context h3 {
        margin-top: 0;
      }
      .context p {
        color: var(--wb-text-secondary);
      }
      dl {
        display: grid;
        grid-template-columns: 1fr auto;
        gap: 10px;
      }
      dd {
        max-width: 160px;
        margin: 0;
        font-weight: 700;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .context nav {
        display: flex;
        flex-direction: column;
      }
      .context nav a {
        padding: 7px 0;
        color: var(--wb-primary);
        text-decoration: none;
        border-bottom: 1px solid var(--wb-border);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditHistoryComponent {
  readonly rows = signal<any[]>([]);
  readonly selected = signal<any | null>(null);
  readonly failures = computed(() => this.rows().filter((x) => x.succeeded === false).length);
  readonly summaries = computed(() => [
    {
      label: 'Total Events',
      value: this.rows().length,
      subtitle: 'Current view',
      icon: 'history',
      tone: 'primary' as const,
    },
    {
      label: 'Users',
      value: new Set(this.rows().map((x) => x.userName)).size,
      subtitle: 'Active identities',
      icon: 'groups',
      tone: 'info' as const,
    },
    {
      label: 'Successful',
      value: this.rows().filter((x) => x.succeeded !== false).length,
      subtitle: 'Completed events',
      icon: 'check_circle',
      tone: 'success' as const,
    },
    {
      label: 'Security Events',
      value: this.failures(),
      subtitle: 'Needs review',
      icon: 'gpp_maybe',
      tone: 'danger' as const,
    },
  ]);
  readonly columns = [
    { field: 'occurredOn', headerName: 'Date / Time' },
    { field: 'loginOn', headerName: 'Login Time' },
    { field: 'userName', headerName: 'User' },
    { field: 'action', headerName: 'Action' },
    { field: 'requestPath', headerName: 'Resource' },
    { field: 'ipAddress', headerName: 'IP Address' },
    {
      field: 'succeeded',
      headerName: 'Status',
      valueFormatter: (p: any) => (p.value === false ? 'Failed' : 'Success'),
    },
  ];
  login = false;
  constructor(
    private api: AdminApiService,
    route: ActivatedRoute,
  ) {
    this.login = !!route.snapshot.data['login'];
    this.load();
  }
  load() {
    this.api.audit(this.login).subscribe((x) => this.rows.set(x));
  }
  print() {
    window.print();
  }
}
