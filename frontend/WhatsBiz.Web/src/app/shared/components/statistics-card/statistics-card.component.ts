import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-statistics-card',
  templateUrl: './statistics-card.component.html',
  styleUrl: './statistics-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatisticsCardComponent {
  readonly title = input.required<string>();
  readonly subtitle = input('');
}
