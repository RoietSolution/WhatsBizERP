import { CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CartQuote } from '../models/storefront.models';

@Component({
  selector: 'shop-free-delivery-progress',
  standalone: true,
  imports: [CurrencyPipe],
  template: `
    @if (quote && quote.freeDeliveryEnabled && quote.freeDeliveryThreshold && !quote.isFreeDeliveryUnlocked && quote.remainingAmount > 0 && (mode === 'cart' || (showProgress && locationVerified && quote.isDeliveryEnabled && quote.isPincodeServiceable && quote.eligibleAmount > 0))) {
      <section class="delivery-progress" [class.toast]="mode === 'toast'" role="status" aria-live="polite">
        <div class="progress-heading"><strong>FREE DELIVERY</strong>@if (mode === 'toast') { <button type="button" class="toast-close progress-close" aria-label="Dismiss free delivery progress" (click)="dismissProgress.emit()">&times;</button> }</div>
        <div class="progress-row"><div class="progress-track" role="progressbar" [attr.aria-valuenow]="clampedProgress" aria-valuemin="0" aria-valuemax="100" aria-label="Free delivery progress"><span [style.width.%]="clampedProgress"></span></div><small>{{ clampedProgress }}%</small></div>
        <span>Add {{ quote.remainingAmount | currency:'INR':'symbol':'1.0-2' }} more for FREE delivery</span>      </section>
    }
    @if (mode === 'toast' && showUnlockedToast && quote && locationVerified) {
      <section class="delivery-progress toast unlocked-toast" role="status" aria-live="polite">
        <span class="unlocked-icon" aria-hidden="true">✓</span><strong>Free delivery unlocked</strong>
        <button type="button" class="toast-close" aria-label="Dismiss free delivery notification" (click)="dismissUnlocked.emit()">×</button>
      </section>
    }
  `,
  styles: [`
    .delivery-progress.toast{z-index:90;max-width:280px}.delivery-progress.toast .progress-row{gap:6px}
    :host{display:block}.delivery-progress{display:grid;gap:7px;padding:13px 18px;border-bottom:1px solid var(--store-border);background:var(--store-primary-soft);min-width:0;color:var(--store-primary-dark)}.delivery-progress strong{font-size:11px;letter-spacing:.2px}.delivery-progress>span{font-size:10px;color:var(--store-muted)}.progress-heading{display:flex;align-items:center;justify-content:space-between;gap:8px;min-width:0}.progress-row{display:flex;align-items:center;gap:8px}.progress-track{flex:1;height:7px;overflow:hidden;border-radius:99px;background:#d9e5dc}.progress-track span{display:block;height:100%;border-radius:99px;background:var(--store-primary);transition:width .28s ease}.progress-row small{min-width:30px;color:var(--store-muted);font-size:9px;text-align:right}.delivery-unlocked{display:flex;align-items:center;gap:7px}.delivery-unlocked svg{width:17px;height:17px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:2}.delivery-unlocked strong{font-size:11px}.delivery-progress.toast{position:fixed;z-index:25;right:18px;bottom:22px;width:min(320px,calc(100vw - 32px));border:1px solid var(--store-border);border-radius:14px;padding:10px 12px 13px 15px;background:#fff;box-shadow:0 10px 28px rgba(12,70,39,.16);animation:delivery-toast-in .2s ease-out}.delivery-progress.toast strong{font-size:12px}.delivery-progress.toast>span{font-size:10px}.delivery-progress.toast .progress-track{height:8px}.delivery-progress.toast .progress-close{flex:0 0 38px;width:38px;height:38px;margin:-5px -5px -5px 0}.delivery-progress.toast.unlocked-toast{display:flex;align-items:center;gap:9px;width:min(260px,calc(100vw - 32px));padding:11px 12px;border-color:#b8dfc3;background:#f5fbf6}.unlocked-icon{display:grid;flex:0 0 25px;width:25px;height:25px;place-items:center;border-radius:50%;color:#fff;background:var(--store-primary);font-size:14px;font-weight:800}.delivery-progress.toast.unlocked-toast strong{flex:1;color:var(--store-primary-dark);font-size:12px}.toast-close{display:grid;flex:0 0 30px;width:30px;height:30px;place-items:center;border:0;border-radius:8px;color:var(--store-muted);background:transparent;font-size:20px;cursor:pointer}.toast-close:hover{background:var(--store-primary-soft);color:var(--store-primary-dark)}.toast-close:focus-visible{outline:2px solid var(--store-primary);outline-offset:1px}@keyframes delivery-toast-in{from{opacity:0;transform:translateY(7px)}to{opacity:1;transform:translateY(0)}}@media(max-width:560px){.delivery-progress.toast{right:12px;bottom:max(78px,calc(env(safe-area-inset-bottom) + 72px));width:calc(100vw - 24px);border-radius:15px}.delivery-progress.toast{padding:10px 12px 12px}.delivery-progress.toast.unlocked-toast{padding:9px 10px}}@media(prefers-reduced-motion:reduce){.delivery-progress.toast{animation:none}.progress-track span{transition:none}}
  `],
})
export class FreeDeliveryProgressComponent {
  @Input({ required: true }) quote!: CartQuote;
  @Input() mode: 'toast' | 'cart' = 'toast';
  @Input() locationVerified = false;
  @Input() showProgress = true;
  @Input() showUnlockedToast = false;
  @Output() dismissProgress = new EventEmitter<void>();
  @Output() dismissUnlocked = new EventEmitter<void>();

  get clampedProgress(): number {
    const value = Number(this.quote?.progressPercent);
    return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  }
}
