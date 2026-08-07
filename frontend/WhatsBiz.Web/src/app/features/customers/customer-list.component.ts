import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize } from 'rxjs';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import {
  MasterActionEvent,
  MasterPageComponent,
  MasterPageConfig,
  MasterPageEvent,
  MasterSortEvent,
} from '../../shared/master/public-api';
import { CustomerApiService } from './customer-api.service';
import { CustomerList } from './customer.models';
@Component({
  selector: 'app-customer-list',
  imports: [MasterPageComponent],
  templateUrl: './customer-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerListComponent {
  readonly file = viewChild<ElementRef<HTMLInputElement>>('file');
  readonly items = signal<CustomerList[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly summaries = computed(() => cards(this.total(), this.items()));
  search = '';
  status: 'all' | 'active' | 'inactive' = 'all';
  page = 1;
  size = 20;
  sortBy = 'customerName';
  descending = false;
  readonly config: MasterPageConfig<CustomerList> = {
    title: 'Customers',
    singular: 'Customer',
    description: 'Manage customer profiles, GST details, credit limits, and relationships.',
    icon: 'groups',
    newRoute: '/customers/new',
    importEnabled: true,
    exportEnabled: true,
    rowId: 'customerId',
    rowName: 'customerName',
    columns: [
      { field: 'customerCode', headerName: 'Code' },
      { field: 'customerName', headerName: 'Customer name', minWidth: 220 },
      { field: 'customerType', headerName: 'Type' },
      { field: 'gstin', headerName: 'GSTIN' },
      { field: 'mobile', headerName: 'Mobile' },
      { field: 'currency', headerName: 'Currency' },
      {
        field: 'isActive',
        headerName: 'Status',
        valueFormatter: (p) => (p.value ? 'Active' : 'Inactive'),
      },
    ],
    detailFields: [
      { label: 'Customer code', key: 'customerCode' },
      { label: 'Customer name', key: 'customerName' },
      { label: 'Type', key: 'customerType' },
      { label: 'GSTIN', key: 'gstin' },
      { label: 'Mobile', key: 'mobile' },
      { label: 'Email', key: 'email' },
      { label: 'Currency', key: 'currency' },
      { label: 'Credit limit', key: 'creditLimit' },
      { label: 'Active', key: 'isActive' },
    ],
  };
  constructor(
    private api: CustomerApiService,
    private snack: MatSnackBar,
    private dialog: MatDialog,
    private router: Router,
  ) {
    this.load();
  }
  load() {
    this.loading.set(true);
    this.api
      .search({
        search: this.search || undefined,
        isActive: this.status === 'all' ? undefined : this.status === 'active',
        sortBy: this.sortBy,
        descending: this.descending,
        pageNumber: this.page,
        pageSize: this.size,
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (x) => {
          this.items.set(x.items);
          this.total.set(x.totalCount);
        },
        error: () => this.snack.open('Unable to load customers.', 'Dismiss', { duration: 4000 }),
      });
  }
  paged(e: MasterPageEvent) {
    this.page = e.pageIndex + 1;
    this.size = e.pageSize;
    this.load();
  }
  sorted(e: MasterSortEvent) {
    this.sortBy = e.field || 'customerName';
    this.descending = e.direction === 'desc';
    this.page = 1;
    this.load();
  }
  handle(e: MasterActionEvent<CustomerList>) {
    if (e.action === 'refresh') this.load();
    else if (e.action === 'import') this.file()?.nativeElement.click();
    else if (e.action === 'export')
      this.api.export().subscribe((b) => download(b, 'customers.xlsx'));
    else if (e.action === 'print') window.print();
    else if (e.action === 'edit' && e.row)
      this.router.navigate(['/customers', e.row.customerId, 'edit']);
    else if (e.action === 'delete' && e.rows?.length) this.remove(e.rows);
    else
      this.snack.open(`${e.action} is ready for the selected customer(s).`, undefined, {
        duration: 2500,
      });
  }
  remove(rows: CustomerList[]) {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete customers', message: `Delete ${rows.length} selected customer(s)?` },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (ok)
          rows.forEach((x, i) =>
            this.api.delete(x.customerId).subscribe({
              complete: () => {
                if (i === rows.length - 1) this.load();
              },
            }),
          );
      });
  }
  upload(e: Event) {
    const input = e.target as HTMLInputElement,
      f = input.files?.[0];
    if (!f) return;
    this.api.import(f).subscribe((x) => {
      input.value = '';
      this.snack.open(`Imported ${x.importedCount} customer(s).`, undefined, { duration: 3000 });
      this.load();
    });
  }
}
function cards<T extends { isActive: boolean }>(total: number, items: T[]) {
  return [
    {
      label: 'Total records',
      value: total,
      subtitle: 'All records',
      icon: 'database',
      tone: 'primary' as const,
    },
    {
      label: 'Active',
      value: items.filter((x) => x.isActive).length,
      subtitle: 'Visible page',
      icon: 'check_circle',
      tone: 'success' as const,
    },
    {
      label: 'Inactive',
      value: items.filter((x) => !x.isActive).length,
      subtitle: 'Visible page',
      icon: 'pause_circle',
      tone: 'warning' as const,
    },
    {
      label: 'Recently loaded',
      value: items.length,
      subtitle: 'Current page',
      icon: 'schedule',
      tone: 'info' as const,
    },
  ];
}
function download(b: Blob, n: string) {
  const u = URL.createObjectURL(b),
    a = document.createElement('a');
  a.href = u;
  a.download = n;
  a.click();
  URL.revokeObjectURL(u);
}
