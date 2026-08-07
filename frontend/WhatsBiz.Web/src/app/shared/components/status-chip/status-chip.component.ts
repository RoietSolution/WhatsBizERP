import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-status-chip',
  templateUrl: './status-chip.component.html',
  styleUrl: './status-chip.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusChipComponent {
  readonly label = input.required<string>();
  readonly tone = input<'success' | 'warning' | 'danger' | 'info' | 'neutral' | null>(null);
  readonly resolvedTone = computed(() => this.tone() ?? this.infer(this.label()));
  private infer(label: string): string {
    const value = label.toLowerCase();
    if (['active', 'approved', 'completed', 'paid', 'success'].some((x) => value.includes(x)))
      return 'success';
    if (['pending', 'draft', 'partial', 'warning'].some((x) => value.includes(x))) return 'warning';
    if (['failed', 'inactive', 'overdue', 'cancelled', 'deleted'].some((x) => value.includes(x)))
      return 'danger';
    return 'info';
  }
}
