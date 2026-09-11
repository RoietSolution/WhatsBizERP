import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { AdminApiService, SystemLogEntry } from './admin-api.service';

@Component({
  selector: 'app-system-log-viewer',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RouterLink,
    PageContainerComponent,
    PageHeaderComponent,
  ],
  templateUrl: './system-log-viewer.component.html',
  styleUrl: './system-log-viewer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SystemLogViewerComponent {
  readonly rows = signal<SystemLogEntry[]>([]);
  readonly totalCount = signal(0);
  readonly selected = signal<SystemLogEntry | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));
  fromDate = this.dateValue(new Date());
  toDate = this.dateValue(new Date());
  level = '';
  requestPath = '';
  statusCode: number | null = null;
  traceId = '';
  search = '';
  pageNumber = 1;
  readonly pageSize = 50;

  constructor(private readonly api: AdminApiService) {
    this.load();
  }

  apply(): void {
    this.pageNumber = 1;
    this.selected.set(null);
    this.load();
  }

  clear(): void {
    this.fromDate = this.dateValue(new Date());
    this.toDate = this.fromDate;
    this.level = '';
    this.requestPath = '';
    this.statusCode = null;
    this.traceId = '';
    this.search = '';
    this.apply();
  }

  previous(): void {
    if (this.pageNumber <= 1) return;
    this.pageNumber--;
    this.load();
  }

  next(): void {
    if (this.pageNumber >= this.pageCount()) return;
    this.pageNumber++;
    this.load();
  }

  private load(): void {
    if (!this.fromDate || !this.toDate) return;
    this.loading.set(true);
    this.error.set('');
    this.api.systemLogs({
      from: this.boundary(this.fromDate),
      to: this.boundary(this.toDate, true),
      level: this.level || undefined,
      requestPath: this.requestPath,
      statusCode: this.statusCode ?? undefined,
      traceId: this.traceId,
      search: this.search,
      pageNumber: this.pageNumber,
      pageSize: this.pageSize,
    }).subscribe({
      next: (result) => {
        this.rows.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.rows.set([]);
        this.totalCount.set(0);
        this.loading.set(false);
        this.error.set(response.error?.detail ?? 'The system logs could not be loaded.');
      },
    });
  }

  private boundary(value: string, nextDay = false): string {
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day + (nextDay ? 1 : 0)).toISOString();
  }

  private dateValue(value: Date): string {
    const year = value.getFullYear();
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const day = String(value.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
