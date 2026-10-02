import { CurrencyPipe } from '@angular/common';
import { Component, Input } from '@angular/core';
import { CartQuote } from '../models/storefront.models';

@Component({
  selector: 'shop-free-delivery-progress',
  standalone: true,
  imports: [CurrencyPipe],
  template: `
    @if (quote && quote.freeDeliveryEnabled && quote.freeDeliveryThreshold && quote.progressPercent >= 0) {
      <section class="delivery-progress" [class.toast]="mode === 'toast'" role="status" aria-live="polite">
        @if (quote.isFreeDeliveryUnlocked) {
          <div class="delivery-unlocked"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg><strong>FREE DELIVERY UNLOCKED</strong></div>
          <span>You've unlocked FREE delivery!</span>
        } @else {
          <strong>FREE DELIVERY</strong>
          <div class="progress-row"><div class="progress-track" role="progressbar" [attr.aria-valuenow]="quote.progressPercent" aria-valuemin="0" aria-valuemax="100" aria-label="Free delivery progress"><span [style.width.%]="quote.progressPercent"></span></div><small>{{ quote.progressPercent }}%</small></div>
          <span>Add {{ quote.remainingAmount | currency:'INR':'symbol':'1.0-2' }} more for FREE delivery</span>
        }
      </section>
    }
  `,
  styles: [`
    .delivery-progress.toast{z-index:90;max-width:280px}.delivery-progress.toast .progress-row{gap:6px}
    :host{display:block}.delivery-progress{display:grid;gap:7px;padding:13px 18px;border-bottom:1px solid var(--store-border);background:var(--store-primary-soft);min-width:0;color:var(--store-primary-dark)}.delivery-progress strong{font-size:11px;letter-spacing:.2px}.delivery-progress>span{font-size:10px;color:var(--store-muted)}.progress-row{display:flex;align-items:center;gap:8px}.progress-track{flex:1;height:7px;overflow:hidden;border-radius:99px;background:#d9e5dc}.progress-track span{display:block;height:100%;border-radius:99px;background:var(--store-primary);transition:width .28s ease}.progress-row small{min-width:30px;color:var(--store-muted);font-size:9px;text-align:right}.delivery-unlocked{display:flex;align-items:center;gap:7px}.delivery-unlocked svg{width:17px;height:17px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:2}.delivery-unlocked strong{font-size:11px}.delivery-progress.toast{position:fixed;z-index:25;right:18px;bottom:22px;width:min(320px,calc(100vw - 32px));border:1px solid var(--store-border);border-radius:14px;padding:13px 15px;background:#fff;box-shadow:0 10px 28px rgba(12,70,39,.16);animation:delivery-toast-in .2s ease-out}.delivery-progress.toast strong{font-size:12px}.delivery-progress.toast>span{font-size:10px}.delivery-progress.toast .progress-track{height:8px}@keyframes delivery-toast-in{from{opacity:0;transform:translateY(7px)}to{opacity:1;transform:translateY(0)}}@media(max-width:560px){.delivery-progress.toast{right:12px;bottom:max(14px,env(safe-area-inset-bottom));width:calc(100vw - 24px);border-radius:15px}.delivery-progress.toast{padding:12px 14px}}@media(prefers-reduced-motion:reduce){.delivery-progress.toast{animation:none}.progress-track span{transition:none}}
  `],
})
export class FreeDeliveryProgressComponent {
  @Input({ required: true }) quote!: CartQuote;
  @Input() mode: 'toast' | 'cart' = 'toast';
}
