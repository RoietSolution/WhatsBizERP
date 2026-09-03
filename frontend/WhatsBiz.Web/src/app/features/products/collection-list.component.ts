import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { MatDialog } from '@angular/material/dialog';
import { CollectionApiService } from './collection-api.service';
import { CollectionListItem } from './collection.models';
@Component({ selector: 'app-collection-list', imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, PageContainerComponent, PageHeaderComponent], templateUrl: './collection-list.component.html', styles: [`:host{display:block}.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:16px 0}.toolbar mat-form-field{min-width:220px}.card{overflow:auto;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}table{width:100%;border-collapse:collapse}th,td{padding:13px 16px;border-bottom:1px solid var(--wb-border);text-align:left;white-space:nowrap}th{color:var(--wb-text-secondary);font-size:.75rem;text-transform:uppercase}td small{display:block;color:var(--wb-text-secondary)}.status{padding:4px 9px;border-radius:14px;font-size:.75rem}.active{color:#166534;background:#dcfce7}.inactive{color:#92400e;background:#fef3c7}.actions{display:flex;gap:6px;flex-wrap:wrap}@media(max-width:700px){.toolbar mat-form-field{width:100%;min-width:0}.card{overflow:visible;border:0;background:transparent}table,tbody,tr,td{display:block;width:100%}thead{display:none}tbody{display:grid;gap:12px}tr{box-sizing:border-box;padding:14px;border:1px solid var(--wb-border);border-radius:var(--wb-radius-md);background:var(--wb-surface)}td{box-sizing:border-box;padding:4px 0;border:0;white-space:normal}td:nth-child(4),td:nth-child(5){display:none}td:nth-child(2)::before{content:'Products: ';color:var(--wb-text-secondary)}td:nth-child(6){padding-top:10px}.actions{gap:2px;margin-inline:-8px}.actions a,.actions button{min-width:0;padding-inline:8px}}`], changeDetection: ChangeDetectionStrategy.OnPush })
export class CollectionListComponent {
  readonly items = signal<CollectionListItem[]>([]); readonly total = signal(0); readonly loading = signal(false); search = ''; status: 'all'|'active'|'inactive' = 'all'; page = 1; size = 20;
  constructor(private readonly api: CollectionApiService, private readonly snack: MatSnackBar, private readonly dialog: MatDialog) { this.load(); }
  load() { this.loading.set(true); this.api.list(this.search, this.status === 'all' ? undefined : this.status === 'active', this.page, this.size).subscribe({ next: x => { this.items.set(x.items); this.total.set(x.totalCount); this.loading.set(false); }, error: () => { this.snack.open('Unable to load collections.', 'Dismiss', { duration: 3500 }); this.loading.set(false); } }); }
  remove(item: CollectionListItem) { this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete collection', message: `Delete “${item.name}”? Products will not be deleted.` } }).afterClosed().subscribe(ok => { if (ok) this.api.delete(item.collectionId).subscribe({ next: () => { this.snack.open('Collection deleted.', undefined, { duration: 2500 }); this.load(); }, error: () => this.snack.open('Collection could not be deleted.', 'Dismiss', { duration: 3500 }) }); }); }
}
