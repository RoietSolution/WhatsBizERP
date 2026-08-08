import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  CellContextMenuEvent,
  CellDoubleClickedEvent,
  ColDef,
  GridApi,
  GridReadyEvent,
  ModuleRegistry,
  RowSelectionOptions,
  SortChangedEvent,
  themeQuartz,
} from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { SearchBoxComponent } from '../search-box/search-box.component';
import { EmptyStateComponent } from '../empty-state/empty-state.component';

export interface GridRowAction<T extends object> {
  action: 'view' | 'edit' | 'delete' | 'print' | 'duplicate' | 'history';
  row: T;
}
export interface GridSort {
  field: string;
  direction: 'asc' | 'desc' | '';
}
ModuleRegistry.registerModules([AllCommunityModule]);
@Component({
  selector: 'app-data-table',
  imports: [AgGridAngular, MatButtonModule, SearchBoxComponent, EmptyStateComponent],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTableComponent<T extends object> {
  readonly rows = input<T[]>([]);
  readonly columns = input<ColDef<any>[]>([]);
  readonly pagination = input(true);
  readonly pageSize = input(25);
  readonly loading = input(false);
  readonly searchPlaceholder = input('Search records');
  readonly emptyTitle = input('No records found');
  readonly emptyDescription = input('Try changing your search or filters.');
  readonly actions = input<Array<'view' | 'edit' | 'delete' | 'print' | 'duplicate' | 'history'>>([
    'view',
    'edit',
    'delete',
  ]);
  readonly selectionChange = output<T[]>();
  readonly searchChange = output<string>();
  readonly sortChange = output<GridSort>();
  readonly rowAction = output<GridRowAction<T>>();
  readonly rowOpen = output<T>();
  readonly gridTheme = themeQuartz.withParams({
    accentColor: '#1D4ED8',
    borderColor: '#E5E7EB',
    fontFamily: 'Inter, Segoe UI, sans-serif',
    fontSize: 13,
    headerBackgroundColor: '#F9FAFB',
    headerTextColor: '#374151',
    rowHoverColor: '#F8FAFC',
    selectedRowBackgroundColor: '#EFF6FF',
    wrapperBorderRadius: 0,
  });
  readonly defaultColumn: ColDef<T> = {
    sortable: true,
    filter: true,
    resizable: true,
    minWidth: 120,
    flex: 1,
    suppressHeaderMenuButton: false,
  };
  readonly pageSizes = [10, 20, 50, 100];
  readonly selection: RowSelectionOptions = {
    mode: 'multiRow',
    headerCheckbox: true,
    enableClickSelection: false,
  };
  readonly displayColumns = computed<ColDef<T>[]>(() => [
    ...this.columns(),
    {
      colId: '__actions',
      headerName: '',
      sortable: false,
      filter: false,
      resizable: false,
      suppressMovable: true,
      pinned: 'right',
      lockPosition: 'right',
      width: 156,
      minWidth: 156,
      maxWidth: 156,
      cellRenderer: (params: { data?: T }) => this.actionRenderer(params.data),
    },
  ]);
  private api?: GridApi<T>;
  gridReady(event: GridReadyEvent<T>): void {
    this.api = event.api;
  }
  search(value: string): void {
    this.api?.setGridOption('quickFilterText', value);
    this.searchChange.emit(value);
  }
  exportCsv(): void {
    this.api?.exportDataAsCsv();
  }
  openColumns(): void {
    this.api?.showColumnChooser();
  }
  emitSelection(): void {
    this.selectionChange.emit(this.api?.getSelectedRows() ?? []);
  }
  emitSort(event: SortChangedEvent<T>): void {
    const column = event.api.getColumnState().find((item) => item.sort);
    this.sortChange.emit({ field: column?.colId ?? '', direction: column?.sort ?? '' });
  }
  openRow(event: CellDoubleClickedEvent<T>): void {
    if (event.data) this.rowOpen.emit(event.data);
  }
  contextRow(event: CellContextMenuEvent<T>): void {
    if (event.data) this.rowAction.emit({ action: 'view', row: event.data });
  }
  private actionRenderer(row?: T): HTMLElement {
    const host = document.createElement('div');
    host.className = 'wb-grid-actions';
    if (!row) return host;
    const icons = {
      view: 'visibility',
      edit: 'edit',
      delete: 'delete',
      print: 'print',
      duplicate: 'content_copy',
      history: 'history',
    } as const;
    for (const action of this.actions()) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'wb-grid-action';
      button.title = `${action[0].toUpperCase()}${action.slice(1)}`;
      button.setAttribute('aria-label', `${button.title} record`);
      const icon = document.createElement('span');
      icon.className = 'material-symbols-rounded';
      icon.textContent = icons[action];
      button.append(icon);
      button.addEventListener('click', (event) => {
        event.stopPropagation();
        this.rowAction.emit({ action, row });
      });
      host.append(button);
    }
    return host;
  }
}
