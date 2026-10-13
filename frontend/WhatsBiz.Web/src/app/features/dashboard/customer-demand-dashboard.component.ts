import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface DemandKeyword { keyword: string; searches: number; outcome: string; interestedCustomers: number; lastSearchedAtUtc: string; }
interface DemandAvailability { id: string; productId: string; productName: string; productCode?: string; availableStock: number; interestedCustomers: number; status: string; requestedAtUtc: string; activeRequestCount: number; cancelledRequestCount: number; }
interface DemandMissing { id: string; keyword: string; searches: number; interestedCustomers: number; status: string; lastRequestedAtUtc: string; }
interface DemandTrend { date: string; searches: number; availableSearches: number; outOfStockSearches: number; noResultSearches: number; mixedSearches: number; }
interface Demand { totalSearches: number; uniqueKeywords: number; availableSearches: number; outOfStockSearches: number; noResultSearches: number; mixedSearches: number; keywords: DemandKeyword[]; availabilityRequests: DemandAvailability[]; missingRequests: DemandMissing[]; trends: DemandTrend[]; }

@Component({
  standalone: true,
  imports: [CommonModule],
  template: `
    <section class="page">
      <header class="page-header">
        <div><small>Analytics / Customer Demand</small><h1>Customer Demand</h1><p>Search interest and unavailable-product requests for this tenant.</p></div>
        <button type="button" class="secondary-button" [disabled]="loading()" (click)="load()">{{ loading() ? 'Loading...' : 'Refresh' }}</button>
      </header>
      <form class="filters" (submit)="$event.preventDefault(); load()">
        <label>From date<input type="date" [value]="from" (input)="from = inputValue($event)" /></label>
        <label>To date<input type="date" [value]="to" (input)="to = inputValue($event)" /></label>
        <label class="keyword-filter">Keyword<input type="search" [value]="keyword" (input)="keyword = inputValue($event)" placeholder="Filter keywords" /></label>
        <button type="submit" class="primary-button" [disabled]="loading()">Apply</button>
        <button type="button" class="link-button" (click)="clearFilters()">Clear filters</button>
      </form>
      @if (error()) { <div class="error-state" role="alert"><span>{{ error() }}</span><button type="button" (click)="load()">Retry</button></div> }
      @if (loading() && !data()) { <div class="loading-state" role="status">Loading customer demand...</div> }
      @if (data(); as report) {
        <div class="cards">
          <article><b>{{ report.totalSearches }}</b><span>Total searches</span></article><article><b>{{ report.uniqueKeywords }}</b><span>Unique keywords</span></article><article><b>{{ report.availableSearches }}</b><span>Available results</span></article><article><b>{{ report.outOfStockSearches }}</b><span>Out of stock</span></article><article><b>{{ report.noResultSearches }}</b><span>No results</span></article><article><b>{{ report.mixedSearches }}</b><span>Mixed results</span></article>
        </div>
        <article class="panel"><div class="panel-heading"><h2>Top searched keywords</h2><span>{{ filteredKeywords().length }} shown</span></div>@if(filteredKeywords().length){<div class="table-wrap"><table><thead><tr><th>Keyword</th><th>Searches</th><th>Outcome</th><th>Interested</th><th>Last searched</th></tr></thead><tbody>@for(row of filteredKeywords(); track row.keyword){<tr><td>{{ row.keyword }}</td><td>{{ row.searches }}</td><td><span class="status-pill">{{ row.outcome }}</span></td><td>{{ row.interestedCustomers }}</td><td>{{ row.lastSearchedAtUtc | date: 'medium' }}</td></tr>}</tbody></table></div>}@else{<p class="empty">No searches match the selected filters.</p>}</article>
        <article class="panel"><div class="panel-heading"><h2>Search trends</h2><span>Daily totals</span></div>@if(report.trends.length){<div class="table-wrap"><table><thead><tr><th>Date</th><th>Searches</th><th>Available</th><th>Out of stock</th><th>No results</th><th>Mixed</th></tr></thead><tbody>@for(row of report.trends; track row.date){<tr><td>{{ row.date | date: 'mediumDate' }}</td><td>{{ row.searches }}</td><td>{{ row.availableSearches }}</td><td>{{ row.outOfStockSearches }}</td><td>{{ row.noResultSearches }}</td><td>{{ row.mixedSearches }}</td></tr>}</tbody></table></div>}@else{<p class="empty">No search trend data for this period.</p>}</article>
        <article class="panel"><div class="panel-heading"><h2>Back-in-stock requests</h2><span>Analytics only</span></div>@if(report.availabilityRequests.length){<div class="table-wrap"><table><thead><tr><th>Product</th><th>SKU</th><th>Stock</th><th>Interested</th><th>Active</th><th>Cancelled</th><th>Last requested</th></tr></thead><tbody>@for(row of report.availabilityRequests; track row.id){<tr><td class="product-cell">@if(imageUrls()[row.productId]){<img [src]="imageUrls()[row.productId]" [alt]="row.productName" />}@else{<span class="image-fallback" aria-hidden="true">▦</span>}<span>{{ row.productName }}</span></td><td>{{ row.productCode || '—' }}</td><td>{{ row.availableStock }}</td><td>{{ row.interestedCustomers }}</td><td>{{ row.activeRequestCount }}</td><td>{{ row.cancelledRequestCount }}</td><td>{{ row.requestedAtUtc | date: 'medium' }}</td></tr>}</tbody></table></div>}@else{<p class="empty">No availability requests for this period.</p>}</article>
        <article class="panel"><div class="panel-heading"><h2>Missing product requests</h2><span>{{ report.missingRequests.length }} shown</span></div>@if(report.missingRequests.length){<div class="table-wrap"><table><thead><tr><th>Keyword</th><th>Searches</th><th>Interested</th><th>Status</th><th>Last requested</th><th>Manage</th></tr></thead><tbody>@for(row of report.missingRequests; track row.id){<tr><td>{{ row.keyword }}</td><td>{{ row.searches }}</td><td>{{ row.interestedCustomers }}</td><td><span class="status-pill">{{ row.status }}</span></td><td>{{ row.lastRequestedAtUtc | date: 'medium' }}</td><td><select [value]="row.status" [disabled]="updatingId() === row.id" (change)="updateStatus(row, inputValue($event))" [attr.aria-label]="'Update status for ' + row.keyword">@for(status of allowedStatuses(row.status); track status){<option [value]="status">{{ status }}</option>}</select></td></tr>}</tbody></table></div>}@else{<p class="empty">No missing-product requests for this period.</p>}</article>
      }
    </section>
  `,
  styles: [`
    :host{display:block;min-width:0}.page{max-width:1240px;margin:0 auto;padding:24px;min-width:0}.page-header{display:flex;justify-content:space-between;gap:18px;align-items:flex-start}.page-header h1{margin:4px 0;font:800 25px Manrope,sans-serif}.page-header p,.page-header small{color:#667085;font-size:11px}.page-header p{margin:0}.filters{display:flex;flex-wrap:wrap;align-items:end;gap:10px;margin:20px 0 14px;padding:14px;border:1px solid #dce3df;border-radius:12px;background:#fff}.filters label{display:grid;gap:5px;color:#344054;font-size:10px;font-weight:800}.filters input,.filters select{min-height:38px;border:1px solid #cbd5e1;border-radius:8px;padding:0 10px;font:inherit}.keyword-filter{min-width:210px}.primary-button,.secondary-button,.link-button,.error-state button{min-height:38px;border-radius:8px;padding:0 14px;font:800 10px Manrope,sans-serif;cursor:pointer}.primary-button{border:0;color:#fff;background:#256d4a}.secondary-button{border:1px solid #cbd5e1;color:#344054;background:#fff}.link-button{border:0;color:#256d4a;background:transparent}.primary-button:disabled,.secondary-button:disabled{opacity:.55;cursor:not-allowed}.cards{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:10px;margin:14px 0}.cards article,.panel{border:1px solid #dce3df;border-radius:12px;background:#fff}.cards article{padding:14px}.cards b{display:block;font-size:23px;color:#173b2b}.cards span,.panel-heading span{color:#667085;font-size:10px}.panel{margin-top:14px;padding:16px}.panel-heading{display:flex;justify-content:space-between;align-items:center;gap:10px;margin-bottom:10px}.panel h2{margin:0;font-size:15px}.table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:11px}th,td{text-align:left;padding:9px;border-bottom:1px solid #e5e7eb;white-space:nowrap}th{color:#667085;font-size:10px}.status-pill{display:inline-flex;border-radius:99px;padding:4px 7px;color:#256d4a;background:#e8f4ec;font-size:9px;font-weight:800}.product-cell{display:flex;align-items:center;gap:8px;min-width:190px;white-space:normal}.product-cell img,.image-fallback{width:34px;height:34px;flex:0 0 34px;border-radius:6px;object-fit:contain;background:#f4f7f5}.image-fallback{display:grid;place-items:center;color:#667085;font-size:17px}.panel select{min-height:32px;border:1px solid #cbd5e1;border-radius:7px;padding:0 6px;font:inherit}.empty,.loading-state{padding:18px;color:#667085;font-size:11px}.error-state{display:flex;justify-content:space-between;gap:10px;align-items:center;margin:12px 0;border:1px solid #f1b8b2;border-radius:10px;padding:10px 12px;color:#9b2c22;background:#fff3f1;font-size:11px}.error-state button{min-height:30px;border:1px solid #d99a92;background:#fff}.loading-state{text-align:center}@media(max-width:1000px){.cards{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:640px){.page{padding:14px}.page-header{display:block}.page-header .secondary-button{margin-top:12px}.filters{align-items:stretch;flex-direction:column}.filters label,.keyword-filter{min-width:0}.filters button{width:100%}.cards{grid-template-columns:repeat(2,minmax(0,1fr))}.panel{padding:12px}}
  `],
})
export class CustomerDemandDashboardComponent implements OnInit, OnDestroy {
  readonly data = signal<Demand | null>(null);
  readonly error = signal('');
  readonly loading = signal(false);
  readonly imageUrls = signal<Record<string, string>>({});
  readonly updatingId = signal('');
  keyword = '';
  from = '';
  to = '';

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    const end = new Date();
    this.to = end.toISOString().slice(0, 10);
    this.from = new Date(end.getTime() - 30 * 86400000).toISOString().slice(0, 10);
    void this.load();
  }

  ngOnDestroy(): void { this.releaseImages(); }
  inputValue(event: Event): string { return (event.target as HTMLInputElement | HTMLSelectElement).value; }
  filteredKeywords(): DemandKeyword[] { const term = this.keyword.trim().toLowerCase(); return (this.data()?.keywords ?? []).filter(row => !term || row.keyword.toLowerCase().includes(term)); }

  async load(): Promise<void> {
    this.loading.set(true); this.error.set(''); this.releaseImages();
    try {
      const params = new HttpParams().set('from', `${this.from}T00:00:00.000Z`).set('to', `${this.to}T23:59:59.999Z`);
      const report = await firstValueFrom(this.http.get<Demand>('/api/customer-demand', { params }));
      this.data.set(report);
      await this.loadImages(report.availabilityRequests);
    } catch (error) { this.error.set(this.apiMessage(error) ?? 'Customer demand is unavailable. Please retry.'); }
    finally { this.loading.set(false); }
  }

  clearFilters(): void { const end = new Date(); this.to = end.toISOString().slice(0, 10); this.from = new Date(end.getTime() - 30 * 86400000).toISOString().slice(0, 10); this.keyword = ''; void this.load(); }
  allowedStatuses(current: string): string[] { return ({ NEW: ['NEW', 'REVIEWING', 'CLOSED'], REVIEWING: ['REVIEWING', 'SOURCING', 'CLOSED'], SOURCING: ['SOURCING', 'AVAILABLE', 'CLOSED'], AVAILABLE: ['AVAILABLE', 'CLOSED'], CLOSED: ['CLOSED'] } as Record<string, string[]>)[current] ?? [current]; }

  async updateStatus(row: DemandMissing, status: string): Promise<void> {
    if (!this.allowedStatuses(row.status).includes(status) || status === row.status) return;
    this.updatingId.set(row.id); this.error.set('');
    try { await firstValueFrom(this.http.patch(`/api/customer-demand/missing/${encodeURIComponent(row.id)}`, { status })); await this.load(); }
    catch (error) { this.error.set(this.apiMessage(error) ?? 'The missing-product status could not be updated.'); }
    finally { this.updatingId.set(''); }
  }

  private async loadImages(rows: DemandAvailability[]): Promise<void> {
    const ids = [...new Set(rows.map(row => row.productId))];
    const loaded = await Promise.all(ids.map(async id => { try { const blob = await firstValueFrom(this.http.get(`/api/products/${id}/image?thumbnail=true`, { responseType: 'blob' })); return [id, URL.createObjectURL(blob)] as const; } catch { return null; } }));
    const images: Record<string, string> = {}; loaded.forEach(item => { if (item) images[item[0]] = item[1]; }); this.imageUrls.set(images);
  }
  private releaseImages(): void { Object.values(this.imageUrls()).forEach(url => URL.revokeObjectURL(url)); this.imageUrls.set({}); }
  private apiMessage(error: unknown): string | null { if (error instanceof HttpErrorResponse) return typeof error.error === 'object' ? error.error?.message ?? null : null; return null; }
}
