import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import {
  DataTableComponent,
  GridRowAction,
} from '../../shared/components/data-table/data-table.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService, Backup } from './admin-api.service';
@Component({
  imports: [
    RouterLink,
    MatButtonModule,
    OperationsWorkspaceComponent,
    DataTableComponent,
    StatusChipComponent,
  ],
  templateUrl: './backup-restore.component.html',
  styles: [
    `
      .actions {
        display: flex;
        flex-wrap: wrap;
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
        margin: 0;
        font-weight: 700;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BackupRestoreComponent {
  readonly items = signal<Backup[]>([]);
  readonly verified = computed(() => this.items().filter((x) => x.isVerified).length);
  readonly storageMb = computed(
    () => +(this.items().reduce((a, x) => a + (x.fileSizeBytes || 0), 0) / 1048576).toFixed(1),
  );
  readonly summaries = computed(() => [
    {
      label: 'Backup History',
      value: this.items().length,
      subtitle: 'Available backups',
      icon: 'history',
      tone: 'primary' as const,
    },
    {
      label: 'Verified',
      value: this.verified(),
      subtitle: 'Restore ready',
      icon: 'verified',
      tone: 'success' as const,
    },
    {
      label: 'Storage Usage',
      value: `${this.storageMb()} MB`,
      subtitle: 'Backup storage',
      icon: 'storage',
      tone: 'info' as const,
    },
    {
      label: 'Scheduled Backup',
      value: 'Planned',
      subtitle: 'Future automation',
      icon: 'schedule',
      tone: 'warning' as const,
    },
  ]);
  readonly columns = [
    { field: 'fileName', headerName: 'Backup File', minWidth: 240 },
    { field: 'startedOn', headerName: 'Created' },
    { field: 'fileSizeBytes', headerName: 'Size (bytes)' },
    { field: 'status', headerName: 'Status' },
    {
      field: 'isVerified',
      headerName: 'Verified',
      valueFormatter: (p: any) => (p.value ? 'Yes' : 'No'),
    },
  ];
  restoreMode = false;
  constructor(
    private api: AdminApiService,
    route: ActivatedRoute,
    private snack: MatSnackBar,
  ) {
    this.restoreMode = !!route.snapshot.data['restore'];
    this.load();
  }
  load() {
    this.api.backups().subscribe((x) => this.items.set(x));
  }
  create() {
    this.api.backup().subscribe(() => this.load());
  }
  validate(x: Backup) {
    this.api
      .restore(x.id)
      .subscribe((r) => this.snack.open(r.message, 'Close', { duration: 4000 }));
  }
  action(e: GridRowAction<Backup>) {
    if (this.restoreMode && e.row.isVerified) this.validate(e.row);
  }
}
