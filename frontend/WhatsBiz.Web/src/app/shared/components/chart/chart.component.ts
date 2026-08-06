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
  template: '<div class="chart" role="img" [attr.aria-label]="ariaLabel()"><apx-chart [series]="$any(config().series)" [chart]="{ type: config().type, height: config().height ?? 300, toolbar: { show: false }, animations: { enabled: true, speed: 200 }, fontFamily: `Inter, Segoe UI, sans-serif`, background: `transparent` }" [colors]="config().colors ?? palette" [labels]="config().labels ?? []" [xaxis]="{ categories: config().categories ?? [], labels: { style: { colors: `#6B7280`, fontSize: `11px` } }, axisBorder: { show: false }, axisTicks: { show: false } }" [yaxis]="{ labels: { formatter: valueLabel, style: { colors: `#6B7280`, fontSize: `11px` } } }" [stroke]="{ curve: `smooth`, width: config().type === `bar` ? 0 : 3 }" [fill]="{ type: config().type === `area` ? `gradient` : `solid`, gradient: { shadeIntensity: 1, opacityFrom: .3, opacityTo: .04, stops: [0, 95, 100] } }" [dataLabels]="{ enabled: false }" [grid]="{ borderColor: `#E5E7EB`, strokeDashArray: 4, padding: { left: 8, right: 8 } }" [legend]="{ show: true, position: `bottom`, fontSize: `12px`, labels: { colors: `#6B7280` }, markers: { size: 5 } }" [tooltip]="{ theme: `light`, y: { formatter: tooltipLabel } }" [plotOptions]="{ bar: { borderRadius: 5, columnWidth: `48%`, horizontal: config().horizontal ?? false } }" [responsive]="responsive" /></div>',
  styleUrl: './chart.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartComponent {
  readonly config = input.required<DashboardChartConfig>();
  readonly ariaLabel = input('Interactive analytics chart');
  readonly palette = ['#1D4ED8', '#0EA5E9', '#10B981', '#F59E0B', '#8B5CF6', '#EF4444'];
  readonly responsive = [{ breakpoint: 768, options: { chart: { height: 260 }, legend: { position: 'bottom' }, xaxis: { labels: { rotate: -45 } } } }];
  readonly valueLabel = (value: number): string => this.config().currency ? this.compactCurrency(value) : this.compactNumber(value);
  readonly tooltipLabel = (value: number): string => this.config().currency ? new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value) : new Intl.NumberFormat('en-IN').format(value);
  private compactCurrency(value: number): string { return `₹${this.compactNumber(value)}`; }
  private compactNumber(value: number): string { return new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 }).format(value); }
}
