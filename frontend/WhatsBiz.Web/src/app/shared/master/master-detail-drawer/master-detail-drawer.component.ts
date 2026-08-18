import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MasterDetailField } from '../master.models';

@Component({
  selector: 'app-master-detail-drawer',
  imports: [MatButtonModule, MatTabsModule],
  templateUrl: './master-detail-drawer.component.html',
  styleUrl: './master-detail-drawer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterDetailDrawerComponent<T extends object> {
  readonly open = input(false);
  readonly title = input('Record details');
  readonly icon = input('description');
  readonly record = input<T | null>(null);
  readonly fields = input<MasterDetailField<T>[]>([]);
  readonly close = output<void>();
  readonly closed = output<void>();
  readonly edit = output<void>();
  dismiss() {
    this.close.emit();
    this.closed.emit();
  }
  display(key: keyof T): unknown {
    const value = this.record()?.[key];
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    return value ?? '—';
  }
}
