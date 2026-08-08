import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { ActionToolbarComponent } from '../../components/action-toolbar/action-toolbar.component';
import {
  DataTableComponent,
  GridRowAction,
  GridSort,
} from '../../components/data-table/data-table.component';
import { FilterPanelComponent } from '../../components/filter-panel/filter-panel.component';
import { PageContainerComponent } from '../../components/page-container/page-container.component';
import { PageHeaderComponent } from '../../components/page-header/page-header.component';
import { SummaryCardComponent } from '../../components/summary-card/summary-card.component';
import { MasterDetailDrawerComponent } from '../master-detail-drawer/master-detail-drawer.component';
import {
  MasterAction,
  MasterActionEvent,
  MasterPageConfig,
  MasterPageEvent,
  MasterSortEvent,
  MasterSummary,
} from '../master.models';

@Component({
  selector: 'app-master-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatPaginatorModule,
    MatSelectModule,
    ActionToolbarComponent,
    DataTableComponent,
    FilterPanelComponent,
    PageContainerComponent,
    PageHeaderComponent,
    SummaryCardComponent,
    MasterDetailDrawerComponent,
  ],
  templateUrl: './master-page.component.html',
  styleUrl: './master-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterPageComponent<T extends object> {
  readonly config = input.required<MasterPageConfig<T>>();
  readonly rows = input<T[]>([]);
  readonly total = input(0);
  readonly loading = input(false);
  readonly pageIndex = input(0);
  readonly pageSize = input(20);
  readonly summaries = input<MasterSummary[]>([]);
  readonly action = output<MasterActionEvent<T>>();
  readonly searchChange = output<string>();
  readonly statusChange = output<'all' | 'active' | 'inactive'>();
  readonly pageChange = output<MasterPageEvent>();
  readonly sortChange = output<MasterSortEvent>();
  readonly savedFilter = output<void>();
  readonly loadedFilter = output<void>();
  readonly selected = signal<T[]>([]);
  readonly drawerRow = signal<T | null>(null);
  readonly statusControl = new FormControl<'all' | 'active' | 'inactive'>('all', {
    nonNullable: true,
  });
  readonly rowActions: Array<'view' | 'edit' | 'delete' | 'print' | 'duplicate' | 'history'> = [
    'view',
    'edit',
    'delete',
    'print',
    'duplicate',
    'history',
  ];
  readonly drawerTitle = computed(() => {
    const row = this.drawerRow();
    return row
      ? String(row[this.config().rowName] ?? this.config().singular)
      : this.config().singular;
  });
  private readonly searches = new Subject<string>();
  constructor() {
    const destroyRef = inject(DestroyRef);
    this.searches
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed(destroyRef))
      .subscribe((value) => this.searchChange.emit(value));
  }
  queueSearch(value: string): void {
    this.searches.next(value.trim());
  }
  applyFilters(): void {
    this.statusChange.emit(this.statusControl.value);
  }
  resetFilters(): void {
    this.statusControl.setValue('all');
    this.statusChange.emit('all');
  }
  emitAction(action: MasterAction): void {
    this.action.emit({ action, row: this.selected()[0], rows: this.selected() });
  }
  page(event: PageEvent): void {
    this.pageChange.emit({ pageIndex: event.pageIndex, pageSize: event.pageSize });
  }
  row(event: GridRowAction<T>): void {
    if (event.action === 'view') this.drawerRow.set(event.row);
    else this.action.emit({ action: event.action, row: event.row, rows: [event.row] });
  }
  drawerEdit(): void {
    const row = this.drawerRow();
    if (row) this.action.emit({ action: 'edit', row, rows: [row] });
  }
}
