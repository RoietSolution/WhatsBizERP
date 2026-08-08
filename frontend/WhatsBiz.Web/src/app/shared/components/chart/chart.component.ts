import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ChartComponent as ApexChartComponent } from 'ng-apexcharts';

export interface DashboardChartConfig {
  type: 'line' | 'area' | 'bar' | 'donut';
  height?: number;
  series: unknown;
  labels?: string[];
  categories?: string[];
  colors?: string[];
  currency?: boolean;
  horizontal?: boolean;
}

@Component({
  selector: 'app-chart',
  imports: [ApexChartComponent],
  templateUrl: './chart.component.html',
  styleUrl: './chart.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartComponent {
  readonly config = input.required<DashboardChartConfig>();
  readonly ariaLabel = input('Interactive analytics chart');
  readonly palette = ['#1D4ED8', '#0EA5E9', '#10B981', '#F59E0B', '#8B5CF6', '#EF4444'];
  readonly responsive = [
    {
      breakpoint: 768,
      options: {
        chart: { height: 260 },
        legend: { position: 'bottom' },
        xaxis: { labels: { rotate: -45 } },
      },
    },
  ];
  readonly valueLabel = (value: number): string =>
    this.config().currency ? this.compactCurrency(value) : this.compactNumber(value);
  readonly tooltipLabel = (value: number): string =>
    this.config().currency
      ? new Intl.NumberFormat('en-IN', {
          style: 'currency',
          currency: 'INR',
          maximumFractionDigits: 2,
        }).format(value)
      : new Intl.NumberFormat('en-IN').format(value);
  private compactCurrency(value: number): string {
    return `₹${this.compactNumber(value)}`;
  }
  private compactNumber(value: number): string {
    return new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 }).format(
      value,
    );
  }
}
