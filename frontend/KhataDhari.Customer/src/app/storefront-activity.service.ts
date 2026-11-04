import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';

type ConsentInit = 'PROMPT' | 'READY' | 'UNAVAILABLE';
interface EventInput { eventId: string; eventType: string; productId?: string; categoryId?: string; searchTerm?: string; resultCount?: number; engagedSeconds?: number; }

@Injectable({ providedIn: 'root' })
export class StorefrontActivityService {
  private readonly queued = new Map<string, EventInput[]>();
  private readonly lastSearch = new Map<string, { term: string; at: number }>();
  private readonly eventUrl = (store: string) => `${environment.apiBaseUrl}/api/store/${encodeURIComponent(store)}/visitor-analytics`;
  private choiceKey(store: string): string { return `khata-dhari-analytics-choice:${store.toLowerCase()}`; }
  private visitorKey(store: string): string { return `khata-dhari-analytics-visitor:${store.toLowerCase()}`; }
  private sessionKey(store: string): string { return `khata-dhari-analytics-session:${store.toLowerCase()}`; }

  constructor(private readonly http: HttpClient) {}

  async initializeConsent(storeKey: string): Promise<ConsentInit> {
    const status = await firstValueFrom(this.http.get<{ available: boolean; noticeVersion: string }>(`${this.eventUrl(storeKey)}/status`)).catch(() => null);
    if (!status?.available) return 'UNAVAILABLE';
    const choice = this.localChoice(storeKey);
    if (choice === 'DENIED') return 'UNAVAILABLE';
    if (choice !== 'GRANTED') return 'PROMPT';
    await this.createSessionConsent(storeKey, status.noticeVersion).catch(() => undefined);
    return this.hasSession(storeKey) ? 'READY' : 'UNAVAILABLE';
  }

  async grantConsent(storeKey: string): Promise<boolean> {
    const status = await firstValueFrom(this.http.get<{ available: boolean; noticeVersion: string }>(`${this.eventUrl(storeKey)}/status`)).catch(() => null);
    if (!status?.available) return false;
    try {
      await this.createSessionConsent(storeKey, status.noticeVersion);
      localStorage.setItem(this.choiceKey(storeKey), 'GRANTED');
      return true;
    } catch { return false; }
  }

  async rejectOrWithdraw(storeKey: string): Promise<void> {
    const visitor = this.readStorage(localStorage, this.visitorKey(storeKey));
    const session = this.readStorage(sessionStorage, this.sessionKey(storeKey));
    if (visitor && session) {
      await firstValueFrom(this.http.delete(`${this.eventUrl(storeKey)}/consent`, { params: { visitorId: visitor, sessionId: session } })).catch(() => undefined);
    }
    this.clearStore(storeKey);
    try { localStorage.setItem(this.choiceKey(storeKey), 'DENIED'); } catch { /* storage may be blocked */ }
  }

  resetChoice(storeKey: string): void {
    try { localStorage.removeItem(this.choiceKey(storeKey)); } catch { /* storage may be blocked */ }
  }

  record(storeKey: string, eventType: string, productId?: string, searchTerm?: string, categoryId?: string, resultCount?: number, engagedSeconds?: number): void {
    if (!this.hasConsent(storeKey)) return;
    const allowed = new Set(['STORE_VISIT','PAGE_VIEW','PRODUCT_VIEW','CATEGORY_VIEW','SEARCH','ADD_TO_CART','REMOVE_FROM_CART','ADD_TO_WISHLIST','REMOVE_FROM_WISHLIST','CHECKOUT_STARTED','FOREGROUND_ENGAGEMENT']);
    if (!allowed.has(eventType)) return;
    const normalizedType = eventType === 'ADD_TO_WISHLIST' || eventType === 'REMOVE_FROM_WISHLIST' ? 'PAGE_VIEW' : eventType;
    if (normalizedType === 'SEARCH' && searchTerm) {
      const previous = this.lastSearch.get(storeKey.toLowerCase());
      const term = searchTerm.trim().replace(/\s+/g, ' ').toLocaleLowerCase();
      if (previous?.term === term && Date.now() - previous.at < 2000) return;
      this.lastSearch.set(storeKey.toLowerCase(), { term, at: Date.now() });
    }
    const id = this.readStorage(sessionStorage, this.sessionKey(storeKey));
    if (!id) return;
    const event: EventInput = { eventId: crypto.randomUUID(), eventType: normalizedType, ...(productId ? { productId } : {}), ...(categoryId ? { categoryId } : {}), ...(searchTerm ? { searchTerm: searchTerm.slice(0, 200) } : {}), ...(resultCount === undefined ? {} : { resultCount: Math.max(0, Math.min(1_000_000, Math.trunc(resultCount))) }), ...(engagedSeconds === undefined ? {} : { engagedSeconds: Math.max(0, Math.min(120, Math.trunc(engagedSeconds))) }) };
    const key = storeKey.toLowerCase(); const queue = this.queued.get(key) ?? []; queue.push(event); this.queued.set(key, queue);
    if (queue.length >= 10) void this.flush(storeKey, id);
    else setTimeout(() => void this.flush(storeKey, id), 2500);
  }

  recordDemandSearch(storeKey: string, keyword: string, resultCount?: number): void {
    if (!this.hasConsent(storeKey)) return;
    const sessionId = this.readStorage(sessionStorage, this.sessionKey(storeKey));
    if (!sessionId) return;
    this.record(storeKey, 'SEARCH', undefined, keyword, undefined, resultCount);
    void firstValueFrom(this.http.post(`${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}/search-events`, { keyword, anonymousSessionId: sessionId })).catch(() => undefined);
  }

  private async createSessionConsent(storeKey: string, noticeVersion: string): Promise<void> {
    let visitor = this.readStorage(localStorage, this.visitorKey(storeKey));
    let session = this.readStorage(sessionStorage, this.sessionKey(storeKey));
    if (!visitor) visitor = crypto.randomUUID();
    if (!session) session = crypto.randomUUID();
    const width = typeof window === 'undefined' ? 0 : window.innerWidth;
    const viewportCategory = width < 600 ? 'SMALL' : width < 1024 ? 'MEDIUM' : width ? 'LARGE' : 'UNKNOWN';
    const query = typeof location === 'undefined' ? new URLSearchParams() : new URLSearchParams(location.search);
    const campaignSource = query.get('utm_source'); const campaignMedium = query.get('utm_medium'); const campaignName = query.get('utm_campaign');
    const trafficSource = campaignSource || campaignMedium || campaignName ? 'CAMPAIGN' : this.referrerSource();
    await firstValueFrom(this.http.post(`${this.eventUrl(storeKey)}/consent`, { visitorId: visitor, sessionId: session, granted: true, noticeVersion, viewportCategory, trafficSource, campaignSource, campaignMedium, campaignName }));
    localStorage.setItem(this.visitorKey(storeKey), visitor); sessionStorage.setItem(this.sessionKey(storeKey), session);
  }

  private referrerSource(): string {
    try {
      const host = new URL(document.referrer).hostname.toLowerCase();
      if (!host || host === location.hostname) return 'DIRECT';
      if (/google\.|bing\.|duckduckgo\./.test(host)) return 'SEARCH';
      if (/facebook\.|instagram\.|linkedin\.|youtube\.|x\.com$|twitter\./.test(host)) return 'SOCIAL';
      return 'REFERRAL';
    } catch { return 'DIRECT'; }
  }

  private async flush(storeKey: string, sessionId: string): Promise<void> {
    const key = storeKey.toLowerCase(); const queue = this.queued.get(key);
    if (!queue?.length || !this.hasConsent(storeKey)) return;
    const batch = queue.splice(0, 50); if (!queue.length) this.queued.delete(key);
    await firstValueFrom(this.http.post(`${this.eventUrl(storeKey)}/events`, { sessionId, events: batch })).catch(() => undefined);
  }
  private hasConsent(store: string): boolean { return this.localChoice(store) === 'GRANTED' && this.hasSession(store); }
  private hasSession(store: string): boolean { return !!this.readStorage(sessionStorage, this.sessionKey(store)); }
  private localChoice(store: string): string { try { return localStorage.getItem(this.choiceKey(store)) ?? ''; } catch { return ''; } }
  private readStorage(storage: Storage, key: string): string { try { return storage.getItem(key) ?? ''; } catch { return ''; } }
  private clearStore(store: string): void { try { localStorage.removeItem(this.visitorKey(store)); sessionStorage.removeItem(this.sessionKey(store)); } catch { /* storage may be blocked */ } this.queued.delete(store.toLowerCase()); }
}
