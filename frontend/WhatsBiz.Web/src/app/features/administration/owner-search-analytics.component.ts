import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';

interface FilterOption { id: string; name: string; }
interface KeywordRow { keyword: string; searches: number; zeroResultSearches: number; lastSearchedAtUtc: string; }
interface TrendRow { dateUtc: string; searches: number; zeroResultSearches: number; }
interface AnalyticsResult {
  totalSearches: number; zeroResultSearches: number; uniqueKeywords: number;
  pageNumber: number; pageSize: number; totalKeywordCount: number;
  keywords: KeywordRow[]; trends: TrendRow[];
}
interface FilterOptions { cities: FilterOption[]; retailers: FilterOption[]; }

@Component({
  selector: 'app-owner-search-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="analytics-page">
      <header class="page-heading"><div><p class="eyebrow">Platform insights</p><h1>Marketplace Search Analytics</h1><p class="intro">Aggregated search activity across retailer storefronts and published city marketplaces.</p></div></header>

      <section class="filters" aria-label="Search analytics filters">
        <label>From<input type="date" [(ngModel)]="from" /></label>
        <label>To<input type="date" [(ngModel)]="to" /></label>
        <label>City<select [(ngModel)]="cityId" (ngModelChange)="retailerId=''"><option value="">All cities</option>@for(city of filters()?.cities ?? []; track city.id){<option [value]="city.id">{{city.name}}</option>}</select></label>
        <label>Retailer / store<select [(ngModel)]="retailerId" (ngModelChange)="cityId=''"><option value="">All retailers</option>@for(store of filters()?.retailers ?? []; track store.id){<option [value]="store.id">{{store.name}}</option>}</select></label>
        <button class="primary" type="button" (click)="page=1;load()" [disabled]="loading()">Apply filters</button>
      </section>

      @if(filtersError()){<section class="error" role="alert"><span>{{filtersError()}}</span><button type="button" (click)="loadFilters()">Retry filters</button></section>}
      @if(error()){<section class="error" role="alert"><span>{{error()}}</span><button type="button" (click)="load()">Retry</button></section>}
      @if(loading()){<div class="state" role="status">Loading search analytics…</div>}
      @else if(data(); as report){
        <section class="summary" aria-label="Search totals">
          <article><span>Total searches</span><strong>{{report.totalSearches | number}}</strong></article>
          <article><span>Zero-result searches</span><strong>{{report.zeroResultSearches | number}}</strong></article>
          <article><span>Unique keywords</span><strong>{{report.uniqueKeywords | number}}</strong></article>
        </section>
        @if(report.totalSearches===0){<section class="state empty">No search activity matches these filters and dates.</section>}
        @else {
          <section class="panel"><div class="panel-heading"><div><h2>Most-searched keywords</h2><p>Aggregated terms only. No customer or session identifiers are shown.</p></div><span>{{report.totalKeywordCount | number}} keywords</span></div>
            <div class="table-wrap" role="region" aria-label="Most-searched keyword results; scroll horizontally on small screens" tabindex="0"><table><thead><tr><th>Keyword</th><th>Searches</th><th>RESULT FOUND</th><th>Last searched (UTC)</th></tr></thead><tbody>
              @for(row of report.keywords; track row.keyword){<tr><td>{{row.keyword}}</td><td>{{row.searches | number}}</td><td><span class="result-status" [ngClass]="resultStatusClass(row)" [attr.aria-label]="'Result found: '+resultStatus(row)">{{resultStatus(row)}}</span></td><td>{{row.lastSearchedAtUtc | date:'medium':'UTC'}}</td></tr>}
            </tbody></table></div>
            <nav class="pagination" aria-label="Keyword results pagination"><button type="button" (click)="changePage(-1)" [disabled]="report.pageNumber<=1 || loading()">Previous</button><span>Page {{report.pageNumber}} of {{pageCount(report.totalKeywordCount,report.pageSize)}}</span><button type="button" (click)="changePage(1)" [disabled]="report.pageNumber>=pageCount(report.totalKeywordCount,report.pageSize) || loading()">Next</button></nav>
          </section>
          <section class="panel"><div class="panel-heading"><div><h2>Keyword trends</h2><p>Daily completed searches and zero-result searches.</p></div></div>
            @if(report.trends.length){<div class="table-wrap"><table><thead><tr><th>Date (UTC)</th><th>Searches</th><th>Zero results</th></tr></thead><tbody>@for(day of report.trends; track day.dateUtc){<tr><td>{{day.dateUtc | date:'mediumDate':'UTC'}}</td><td>{{day.searches | number}}</td><td>{{day.zeroResultSearches | number}}</td></tr>}</tbody></table></div>}
            @else {<div class="state">No trend data for this selection.</div>}
          </section>
        }
      }
    </main>`,
  styles: [`
    :host{display:block;color:var(--wb-text,#19232e)}.analytics-page{max-width:1240px;margin:0 auto;padding:28px 24px 40px}.page-heading{margin-bottom:22px}.eyebrow{margin:0 0 6px;color:var(--wb-primary,#16724a);font-size:.75rem;font-weight:750;letter-spacing:.09em;text-transform:uppercase}.page-heading h1{margin:0;font-size:clamp(1.55rem,3vw,2rem)}.intro{margin:8px 0 0;color:var(--wb-text-secondary,#687582)}
    .filters,.summary,.panel{background:var(--wb-surface,#fff);border:1px solid var(--wb-border,#dce4e8);border-radius:14px;box-shadow:0 3px 14px #16332208}.filters{display:grid;grid-template-columns:repeat(4,minmax(130px,1fr)) auto;align-items:end;gap:12px;padding:16px;margin-bottom:18px}.filters label{display:grid;gap:6px;font-size:.83rem;font-weight:650}.filters input,.filters select{min-width:0;width:100%;height:42px;padding:0 10px;border:1px solid var(--wb-border,#cbd5dc);border-radius:9px;background:var(--wb-surface,#fff);color:inherit;font:inherit}.primary,.pagination button,.error button{min-height:42px;border:1px solid var(--wb-primary,#16724a);border-radius:9px;padding:0 15px;background:var(--wb-primary,#16724a);color:white;font:inherit;font-weight:700;cursor:pointer}.primary:disabled{opacity:.6;cursor:wait}
    .summary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1px;margin-bottom:18px;overflow:hidden}.summary article{padding:18px;background:var(--wb-surface,#fff)}.summary span{display:block;color:var(--wb-text-secondary,#687582);font-size:.88rem}.summary strong{display:block;margin-top:5px;font-size:1.65rem}.panel{margin:0 0 18px;overflow:hidden}.panel-heading{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:18px 20px;border-bottom:1px solid var(--wb-border,#e3e9ed)}.panel-heading h2{margin:0;font-size:1.1rem}.panel-heading p{margin:5px 0 0;color:var(--wb-text-secondary,#687582);font-size:.88rem}.panel-heading>span{color:var(--wb-text-secondary,#687582);font-size:.85rem}.table-wrap{width:100%;overflow-x:auto}table{width:100%;border-collapse:collapse;text-align:left}th,td{padding:12px 18px;border-bottom:1px solid var(--wb-border,#edf0f2);white-space:nowrap}th{font-size:.78rem;color:var(--wb-text-secondary,#687582);font-weight:700}td{font-size:.9rem}.result-status{display:inline-flex;align-items:center;min-height:28px;padding:3px 10px;border:1px solid transparent;border-radius:999px;font-size:.82rem;font-weight:750;line-height:1.2}.result-status--true{color:var(--wb-success-text,#176b3a);background:var(--wb-success-bg,#e8f5ec);border-color:var(--wb-success-border,#b9dfc4)}.result-status--false{color:var(--wb-danger-text,#a32e2e);background:var(--wb-danger-bg,#fff0ef);border-color:var(--wb-danger-border,#efc5c2)}.result-status--partial{color:var(--wb-warning-text,#745510);background:var(--wb-warning-bg,#fff7df);border-color:var(--wb-warning-border,#ead79b)}.result-status--na{color:var(--wb-text-secondary,#687582);background:var(--wb-surface-muted,#f1f4f5);border-color:var(--wb-border,#dce4e8)}.pagination{display:flex;align-items:center;justify-content:flex-end;gap:12px;padding:14px 18px}.pagination span{font-size:.88rem;color:var(--wb-text-secondary,#687582)}.pagination button{background:transparent;color:var(--wb-primary,#16724a)}.pagination button:disabled{opacity:.45;cursor:not-allowed}.state{padding:22px;text-align:center;color:var(--wb-text-secondary,#687582)}.empty{background:var(--wb-surface,#fff);border:1px dashed var(--wb-border,#cbd5dc);border-radius:12px}.error{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:13px 16px;margin:0 0 14px;border:1px solid #e8b4ac;border-radius:10px;background:#fff7f5;color:#873b2e}.error button{min-height:36px;background:transparent;color:#873b2e;border-color:#d79b91}
    @media(max-width:900px){.filters{grid-template-columns:repeat(2,minmax(0,1fr))}.filters .primary{width:100%}}
    @media(max-width:560px){.analytics-page{padding:20px 14px 30px}.filters{grid-template-columns:1fr 1fr;gap:10px;padding:12px}.filters label:nth-child(3),.filters label:nth-child(4),.filters .primary{grid-column:1/-1}.summary{grid-template-columns:1fr}.summary article{padding:14px 16px}.summary strong{font-size:1.35rem}.panel-heading{align-items:flex-start;padding:15px}.pagination{justify-content:space-between;padding:12px}.error{align-items:flex-start;flex-direction:column}.error button{width:100%}th,td{padding:11px 12px}}
    @media(prefers-reduced-motion:reduce){*,*::before,*::after{scroll-behavior:auto!important;animation-duration:.01ms!important;transition-duration:.01ms!important}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OwnerSearchAnalyticsComponent implements OnInit {
  readonly filters = signal<FilterOptions | null>(null);
  readonly filtersError = signal('');
  readonly data = signal<AnalyticsResult | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  from = '';
  to = '';
  cityId = '';
  retailerId = '';
  page = 1;
  private readonly pageSize = 25;

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    const now = new Date();
    this.to = now.toISOString().slice(0, 10);
    this.from = new Date(now.getTime() - 29 * 86400000).toISOString().slice(0, 10);
    this.loadFilters();
    this.load();
  }

  loadFilters(): void {
    this.filtersError.set('');
    this.http.get<FilterOptions>('/api/customer-demand/owner/search-analytics/filters').subscribe({
      next: value => this.filters.set(value),
      error: () => this.filtersError.set('Unable to load city and retailer filters.'),
    });
  }

  load(): void {
    if (!this.from || !this.to || this.from > this.to) {
      this.error.set('Choose a valid date range.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.data.set(null);
    const params = new HttpParams()
      .set('from', `${this.from}T00:00:00.000Z`)
      .set('to', `${this.to}T23:59:59.999Z`)
      .set('page', this.page)
      .set('pageSize', this.pageSize);
    const filtered = this.cityId ? params.set('cityId', this.cityId)
      : this.retailerId ? params.set('retailerTenantId', this.retailerId) : params;
    this.http.get<AnalyticsResult>('/api/customer-demand/owner/search-analytics', { params: filtered }).subscribe({
      next: value => { this.data.set(value); this.loading.set(false); },
      error: () => { this.error.set('Search analytics could not be loaded. Check your access and try again.'); this.loading.set(false); },
    });
  }

  changePage(delta: number): void { this.page += delta; this.load(); }
  pageCount(total: number, size: number): number { return Math.max(1, Math.ceil(total / size)); }
  resultStatus(row: KeywordRow): 'True' | 'False' | 'Partial' | 'N/A' {
    if (row.searches === 0) return 'N/A';
    if (row.zeroResultSearches === 0) return 'True';
    if (row.zeroResultSearches === row.searches) return 'False';
    if (row.zeroResultSearches > 0 && row.zeroResultSearches < row.searches) return 'Partial';
    return 'N/A';
  }
  resultStatusClass(row: KeywordRow): string {
    const status = this.resultStatus(row);
    return `result-status--${status === 'True' ? 'true' : status === 'False' ? 'false' : status === 'Partial' ? 'partial' : 'na'}`;
  }
}
