import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { StorefrontOffer } from '../models/storefront.models';
import { StorefrontDataService } from '../data/storefront-data.service';

@Component({
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe],
  template: `
    <main class="offer-page">
      @if (loading()) { <p role="status">Loading offer…</p> }
      @else if (offer(); as item) {
        @if(item.bannerImageUrl){<img class="offer-banner" [src]="item.bannerImageUrl" [alt]="item.title"/>}
        <a class="back" [href]="'/' + storeKey">← Back to store</a>
        <span class="status" [class.expired]="item.status === 'EXPIRED'">{{statusLabel(item.status)}}</span>
        <h1>{{item.title}}</h1>
        @if(item.shortDescription){<p class="short">{{item.shortDescription}}</p>}
        <p class="benefit">{{item.benefitDescription}}</p>
        @if(item.detailedDescription){<p class="detail">{{item.detailedDescription}}</p>}
        @if(item.promoCode){<p class="code">Use code <strong>{{item.promoCode}}</strong></p>}
        <dl>
          @if(item.validFrom){<div><dt>Valid from</dt><dd>{{item.validFrom | date:'mediumDate'}}</dd></div>}
          @if(item.validUntil){<div><dt>Valid until</dt><dd>{{item.validUntil | date:'mediumDate'}}</dd></div>}
          @if(item.minimumOrderAmount > 0){<div><dt>Minimum order</dt><dd>{{item.minimumOrderAmount | currency:'INR'}}</dd></div>}
          @if(item.maximumDiscount){<div><dt>Maximum discount</dt><dd>{{item.maximumDiscount | currency:'INR'}}</dd></div>}
          @if(item.usageLimitPerCustomer){<div><dt>Usage restriction</dt><dd>Up to {{item.usageLimitPerCustomer}} use(s) per customer</dd></div>}
        </dl>
        @if(item.eligibleItemsDescription){<section><h2>Eligible products or categories</h2><p>{{item.eligibleItemsDescription}}</p></section>}
        @if(item.termsAndConditions){<section><h2>Terms & Conditions</h2><p class="terms">{{item.termsAndConditions}}</p></section>}
        <button type="button" [disabled]="item.status === 'EXPIRED'" (click)="shop()">{{item.ctaLabel || (item.eligibleItemsDescription ? 'View Eligible Products' : 'Shop Now')}}</button>
      } @else { <section class="missing"><h1>Offer unavailable</h1><p>This offer is no longer available.</p><button type="button" (click)="shop()">Continue shopping</button></section> }
    </main>
  `,
  styles: [`:host{display:block}.offer-page{max-width:850px;margin:0 auto;padding:24px 0 70px;color:var(--store-text)}.offer-banner{display:block;width:100%;max-height:340px;aspect-ratio:5/2;object-fit:cover;border-radius:20px;margin-bottom:22px}.back{display:inline-block;color:var(--store-primary);font-weight:700;text-decoration:none;margin-bottom:18px}.status{display:block;width:max-content;border-radius:30px;padding:6px 11px;color:#11633e;background:#e6f5ec;font-size:12px;font-weight:800}.status.expired{color:#9a3412;background:#fff0e8}h1{font-size:clamp(26px,5vw,40px);margin:13px 0}.short{font-size:18px;color:var(--store-muted)}.benefit{display:inline-block;padding:11px 15px;border-radius:12px;color:#124f36;background:#e7f5ec;font-weight:800}.detail,.terms,section p{line-height:1.7;white-space:pre-line}dl{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin:22px 0}dl div,section{border:1px solid var(--store-border);border-radius:13px;padding:14px}dt{color:var(--store-muted);font-size:12px}dd{margin:5px 0 0;font-weight:750}section h2{font-size:17px;margin:0 0 6px}.code strong{font-family:monospace;border:1px dashed #8aa893;padding:5px 8px}.offer-page>button,.missing button{border:0;border-radius:12px;padding:13px 20px;color:white;background:var(--store-primary);font-weight:800;cursor:pointer}.offer-page>button:disabled{opacity:.55;cursor:not-allowed}.missing{text-align:center;margin-top:60px}.missing p{color:var(--store-muted)}@media(max-width:600px){.offer-page{padding:16px 0 80px}.offer-banner{border-radius:14px}dl{grid-template-columns:1fr}}`],
})
export class OfferDetailsPage implements OnInit {
  readonly offer = signal<StorefrontOffer | null>(null);
  readonly loading = signal(true);
  storeKey = '';
  private offerId = '';
  constructor(private readonly route: ActivatedRoute, private readonly router: Router, private readonly data: StorefrontDataService) {}
  ngOnInit(): void { this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? ''; this.offerId = this.route.snapshot.paramMap.get('offerId') ?? ''; void this.load(); }
  async load(): Promise<void> { try { this.offer.set(await this.data.getOffer(this.storeKey, this.offerId)); } finally { this.loading.set(false); } }
  statusLabel(status: StorefrontOffer['status']): string { return status === 'EXPIRED' ? 'Offer expired' : status === 'UPCOMING' ? 'Coming soon' : 'Offer available'; }
  shop(): void { void this.router.navigate(['/', this.storeKey]); }
}
