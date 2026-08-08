import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-filter-panel',
  imports: [MatButtonModule],
  templateUrl: './filter-panel.component.html',
  styleUrl: './filter-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterPanelComponent {
  readonly title = input('Filters');
  readonly apply = output<void>();
  readonly reset = output<void>();
  readonly save = output<void>();
  readonly load = output<void>();
  readonly collapsed = signal(false);
  toggle(): void {
    this.collapsed.update((value) => !value);
  }
}
