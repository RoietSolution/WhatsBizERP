import { ChangeDetectionStrategy, Component, computed, input, output, viewChild } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import { AllCommunityModule, ColDef, GridApi, GridReadyEvent, ModuleRegistry, RowSelectionOptions, themeQuartz } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { SearchBoxComponent } from '../search-box/search-box.component';
import { EmptyStateComponent } from '../empty-state/empty-state.component';

ModuleRegistry.registerModules([AllCommunityModule]);
@Component({ selector: 'app-data-table', imports: [AgGridAngular, MatButtonModule, SearchBoxComponent, EmptyStateComponent], template: '<section class="data-table"><header><app-search-box placeholder="Search records" (searchChange)="quickFilter($event)" /><div><ng-content select="[table-actions]" /><button mat-icon-button type="button" aria-label="Export CSV" (click)="exportCsv()"><span class="material-symbols-rounded">download</span></button><button mat-icon-button type="button" aria-label="Choose columns" (click)="openColumns()"><span class="material-symbols-rounded">view_column</span></button></div></header>@if (rows().length) { <ag-grid-angular [theme]="gridTheme" [rowData]="rows()" [columnDefs]="columns()" [defaultColDef]="defaultColumn" [pagination]="pagination()" [paginationPageSize]="pageSize()" [paginationPageSizeSelector]="pageSizes" [rowSelection]="selection" [animateRows]="true" domLayout="autoHeight" (gridReady)="gridReady($event)" (selectionChanged)="emitSelection()" /> } @else { <app-empty-state [title]="emptyTitle()" [description]="emptyDescription()" /> }</section>', styleUrl: './data-table.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class DataTableComponent<T extends object> {
  readonly rows = input<T[]>([]); readonly columns = input<ColDef<T>[]>([]); readonly pagination = input(true); readonly pageSize = input(25); readonly emptyTitle = input('No records found'); readonly emptyDescription = input('Try changing your search or filters.'); readonly selectionChange = output<T[]>();
  readonly grid = viewChild(AgGridAngular<T>); readonly gridTheme = themeQuartz.withParams({ accentColor: '#1D4ED8', borderColor: '#E5E7EB', fontFamily: 'Inter, Segoe UI, sans-serif', headerBackgroundColor: '#F9FAFB', rowHoverColor: '#F8FAFC', selectedRowBackgroundColor: '#EFF6FF' });
  readonly defaultColumn: ColDef<T> = { sortable: true, filter: true, resizable: true, minWidth: 120, flex: 1 }; readonly pageSizes = [10, 25, 50, 100]; readonly selection: RowSelectionOptions = { mode: 'multiRow', headerCheckbox: true, enableClickSelection: false };
  private api?: GridApi<T>;
  gridReady(event: GridReadyEvent<T>): void { this.api = event.api; }
  quickFilter(value: string): void { this.api?.setGridOption('quickFilterText', value); }
  exportCsv(): void { this.api?.exportDataAsCsv(); }
  openColumns(): void { this.api?.showColumnChooser(); }
  emitSelection(): void { this.selectionChange.emit(this.api?.getSelectedRows() ?? []); }
}
