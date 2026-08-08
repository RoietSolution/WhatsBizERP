import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-summary-card',
  templateUrl: './summary-card.component.html',
  styleUrl: './summary-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SummaryCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number | null>();
  readonly icon = input('analytics');
  readonly tone = input<'primary' | 'success' | 'warning' | 'danger' | 'info'>('primary');
  readonly trend = input('');
  readonly trendPositive = input(true);
  readonly sparkline = input<number[]>([]);
}
