import { Component, DestroyRef, HostListener, OnInit, afterRenderEffect, effect, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, ParamMap, Router, RouterLink, RouterOutlet } from '@angular/router';
import { CartService } from './cart/cart.service';
import { StorefrontDataService } from './data/storefront-data.service';
import { CartLine, CartQuote, Store } from './models/storefront.models';
import { CustomerSessionService } from './customer-session.service';
import { StorefrontNotificationService } from './storefront-notification.service';
import { WishlistService } from './wishlist.service';
import { PwaInstallService } from './pwa-install.service';
import { FreeDeliveryProgressComponent } from './shared/free-delivery-progress.component';
import { CustomerRewardsService } from './customer-rewards.service';
import { StorefrontActivityService } from './storefront-activity.service';
import { StorefrontCampaignPopupComponent } from './shared/storefront-campaign-popup.component';
import { PublicMarketplaceApiService } from './data/public-marketplace-api.service';
import { getCustomStoreDomain } from './custom-store-domain';
import { CityMarketplacePage } from './pages/city-marketplace.page';
import { markStorefrontStartup, measureStorefrontStartup, scheduleStorefrontIdle } from './storefront-startup-performance';

@Component({
  selector: 'shop-store-shell',
  standalone: true,
  imports: [CurrencyPipe, RouterLink, RouterOutlet, FreeDeliveryProgressComponent, StorefrontCampaignPopupComponent, CityMarketplacePage],
  template: `
    @if (loading()) {
      <div class="shell-skeleton" role="status" aria-label="Opening store">
        <div class="skeleton-header"><i></i><span></span><b></b></div><div class="skeleton-search"></div>
        <div class="skeleton-body"><div></div><div></div><div></div><div></div></div>
      </div>
    } @else if (routeKind() === 'CITY') {
      @defer (when routeKind() === 'CITY') { <shop-city-marketplace [slug]="routeSlug()" /> }
      @placeholder { <div class="shell-skeleton" role="status" aria-label="Opening city marketplace"><div class="skeleton-header"><i></i><span></span><b></b></div><div class="skeleton-body"><div></div><div></div><div></div><div></div></div></div> }
    } @else if (routeKind() === 'NOT_FOUND') {
      <main class="state-page"><span class="state-icon">?</span><h1>Page not found</h1><p>This store or city marketplace is unavailable.</p><a routerLink="/">Browse KhataDhari Shop</a></main>
    } @else if (loadFailed()) {
      <main class="state-page"><span class="state-icon">!</span><h1>Store temporarily unavailable</h1><p>We couldn't connect to this store. Check your connection and try again.</p><button type="button" (click)="retry()">Try again</button></main>
    } @else if (store(); as currentStore) {
      <div class="store-frame marketplace-store-frame" [style.--store-primary]="currentStore.accentColor">
        @if (startupContentReady()) { @defer (on idle) { <shop-campaign-popup [storeKey]="currentStore.storeKey" /> } }
        <header class="store-header">
          <div class="header-inner">
            <button class="home-menu-button" type="button" (click)="toggleHeaderMenu()" [attr.aria-expanded]="headerMenuOpen()" aria-controls="store-quick-menu" aria-label="Open store navigation"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>
            <a class="brand" [routerLink]="['/', currentStore.storeKey]" aria-label="Store home">
              @if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="logoImageUrl(currentStore.logoUrl)" alt="" width="1200" height="1200" decoding="async" (error)="logoImageFailed(currentStore.logoUrl,$event)" /> }
              @else { <span class="brand-mark">{{ currentStore.name.slice(0, 1) }}</span> }
              <span class="brand-copy"><strong>{{ currentStore.displayName || currentStore.name }}</strong>@if(currentStore.tagline){<small>{{currentStore.tagline}}</small>}</span>
            </a>
            <form class="search-wrap" (submit)="search($event)">
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21 21-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z"/></svg>
              <input aria-label="Search products, categories, and brands" placeholder="Search products, categories, brands..." [value]="searchText" (input)="updateSearch($event)" />
              @if (searchText) { <button type="button" class="clear-search" (click)="clearSearch()" aria-label="Clear search">&times;</button> }
            </form>
            <a class="header-icon-action wishlist-header" [routerLink]="['/',currentStore.storeKey,'account','wishlist']" aria-label="Open wishlist"><span class="header-count-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 8.5c0 4-8 10-8 10s-8-6-8-10a4.3 4.3 0 0 1 8-2.4A4.3 4.3 0 0 1 20 8.5z"/></svg>@if(wishlist.ids().size){<b class="header-count-badge wishlist-badge" [attr.aria-label]="wishlist.ids().size + ' items in wishlist'">{{wishlist.ids().size > 99 ? '99+' : wishlist.ids().size}}</b>}</span></a>
            <button class="desktop-cart" type="button" (click)="goCart()" aria-label="Open cart">
              <span class="cart-icon-wrap header-count-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 4h2l2.1 10.2a2 2 0 0 1 2 1.6h7.8a2 2 0 0 0 2-1.6L20 8H6M10 20h.01M17 20h.01"/></svg>@if (cart.itemCount()) { <b class="header-count-badge cart-badge" [attr.aria-label]="cart.itemCount() + ' items in cart'">{{ cart.itemCount() > 99 ? '99+' : cart.itemCount() }}</b> }</span>
              <span class="cart-copy"><strong>Cart</strong>@if (cart.itemCount()) { <small>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'item' : 'items' }} &middot; {{ cart.total() | currency:'INR':'symbol':'1.0-0' }}</small> } @else { <small>Empty</small> }</span>
            </button>
            @if (session.active()) { <a class="header-avatar-button" [routerLink]="['/',currentStore.storeKey,'account','profile']" aria-label="Open customer profile">@if(session.customer()?.profileImageUrl; as avatar){<img class="header-avatar" [src]="avatar" alt="">} @else {<span class="header-avatar initials">{{initials(session.customer()?.name||'')}}</span>}</a> }
            @else { <a class="header-avatar-button guest-profile" [routerLink]="['/',currentStore.storeKey,'auth']" [queryParams]="{returnUrl:currentReturnUrl()}" aria-label="Sign in to your account"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="3.2"/><path d="M5 20c.6-3.3 3.1-5.1 7-5.1s6.4 1.8 7 5.1"/></svg></a> }
          </div>
          @if(headerMenuOpen()){<nav id="store-quick-menu" class="store-quick-menu" aria-label="Store navigation"><a [routerLink]="['/',currentStore.storeKey]" (click)="closeHeaderMenu()">Store home</a><a [routerLink]="['/',currentStore.storeKey,'categories']" (click)="closeHeaderMenu()">Shop categories</a><a [routerLink]="['/',currentStore.storeKey,'account','wishlist']" (click)="closeHeaderMenu()">My wishlist</a>@if(session.active()){<a [routerLink]="['/',currentStore.storeKey,'account','orders']" (click)="closeHeaderMenu()">My orders</a><button type="button" (click)="signOut(currentStore.storeKey)">Sign out</button>}</nav>}
        </header>
         @if (notify.message(); as message) { <div class="store-toast" role="status" aria-live="polite">{{message}}</div> }
          @if (pwa.canInstall()) { <aside class="pwa-install-card" aria-label="Install store app"><span class="pwa-install-icon">@if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="logoImageUrl(currentStore.logoUrl)" alt="" width="1200" height="1200" decoding="async" (error)="logoImageFailed(currentStore.logoUrl,$event)" /> } @else { {{ currentStore.name.slice(0, 1) }} }</span><span class="pwa-install-copy"><strong>Install {{ currentStore.name }} App</strong><small>Shop faster from your home screen</small></span><button type="button" (click)="installApp()">{{pwa.installing()?'Installing...':'Install'}}</button><button class="pwa-dismiss" type="button" aria-label="Dismiss install prompt" (click)="pwa.dismissInstall()">&times;</button></aside> }
          @if (pwa.iosInstallHint()) { <aside class="pwa-install-card pwa-ios-hint" aria-label="Add store to home screen"><span class="pwa-install-icon">@if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="logoImageUrl(currentStore.logoUrl)" alt="" width="1200" height="1200" decoding="async" (error)="logoImageFailed(currentStore.logoUrl,$event)" /> } @else { {{ currentStore.name.slice(0, 1) }} }</span><span class="pwa-install-copy"><strong>Add {{ currentStore.name }} to your Home Screen</strong><small>Share &#8594; Add to Home Screen</small></span><button class="pwa-dismiss" type="button" aria-label="Dismiss home screen instructions" (click)="pwa.dismissInstall()">&times;</button></aside> }
         <main class="store-content"><router-outlet /></main>
         @if(analyticsPrompt()) {
           <aside class="analytics-consent" aria-labelledby="analytics-consent-title" aria-describedby="analytics-consent-message">
             <div class="analytics-consent-copy">
               <h2 id="analytics-consent-title">Help us improve your shopping experience</h2>
               <p id="analytics-consent-message">Optional analytics helps improve this store. Your choice will not affect shopping or orders.</p>
               <a class="analytics-details" [routerLink]="['/',currentStore.storeKey,'privacy']">Privacy details</a>
               @if(analyticsConsentError()) { <div class="analytics-consent-error" role="alert">{{analyticsConsentError()}} <button type="button" (click)="retryAnalyticsChoice()" [disabled]="analyticsConsentBusy()">Try again</button></div> }
             </div>
             <div class="analytics-consent-actions">
               <button type="button" (click)="chooseAnalytics(currentStore.storeKey,true)" [disabled]="analyticsConsentBusy()">{{analyticsConsentBusy()?'Saving...':'Allow analytics'}}</button>
               <button type="button" class="consent-secondary" (click)="chooseAnalytics(currentStore.storeKey,false)" [disabled]="analyticsConsentBusy()">Only necessary</button>
             </div>
           </aside>
         }
         <div class="store-privacy-actions"><a [routerLink]="['/',currentStore.storeKey,'terms-and-conditions']">Terms &amp; Conditions</a><a [routerLink]="['/',currentStore.storeKey,'cancellation-return-refund']">Cancellation, returns &amp; refunds</a><a [routerLink]="['/',currentStore.storeKey,'privacy']">Privacy policy</a><button type="button" (click)="openAnalyticsPreferences()">Privacy choices</button></div>@if (shoppingRoute() && currentStore.orderContactNumber) {
           <nav class="order-contact-actions" aria-label="Contact this store to place an order">
             <a class="call-order" [href]="'tel:' + currentStore.orderContactNumber" aria-label="Call this store to place an order"><span aria-hidden="true">&#9742;</span><span>Call to Order</span></a>
             <a class="whatsapp-order" [href]="whatsappOrderUrl(currentStore)" target="_blank" rel="noopener noreferrer" aria-label="Message this store on WhatsApp to place an order"><span aria-hidden="true">&#9673;</span><span>Order on WhatsApp</span></a>
           </nav>
         }@defer (when freeDeliveryProgressRoute()) { @if (freeDeliveryProgressRoute() && shoppingQuote(); as deliveryQuote) { <shop-free-delivery-progress [quote]="deliveryQuote" mode="toast" [locationVerified]="deliveryLocationVerified()" [showProgress]="canShowFreeDeliveryProgress()" [showUnlockedToast]="freeDeliveryToastVisible()" (dismissProgress)="dismissFreeDeliveryToast()" (dismissUnlocked)="dismissFreeDeliveryToast()" /> } }

      </div>
    } @else {
      <main class="state-page"><span class="state-icon">?</span><h1>Store not found</h1><p>This shopping link may be incorrect or the store is unavailable.</p><a routerLink="/">Back to shops</a></main>
    }
  `,
  styles: [`
    :host{display:block;min-height:100vh}.store-frame{min-height:100vh;--tenant-primary:var(--store-primary)}.store-header{z-index:100;overflow:visible}.header-inner{grid-template-columns:minmax(180px,250px) minmax(220px,1fr) auto auto auto}.header-icon-action,.header-avatar-button{display:grid;position:relative;width:42px;height:42px;place-items:center;border:1px solid var(--store-border);border-radius:12px;color:var(--store-primary-dark);background:#fff;cursor:pointer;text-decoration:none}.header-icon-action svg{width:20px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.wishlist-header{isolation:isolate}.wishlist-badge{position:absolute;top:-6px;right:-6px;display:grid;min-width:17px;height:17px;place-items:center;border:2px solid #fff;border-radius:99px;padding:0 3px;color:#fff;background:var(--store-primary);font-size:8px;line-height:1}.header-icon-action:hover,.header-avatar-button:hover{border-color:var(--store-primary);background:var(--store-primary-soft)}.header-avatar-button{border-radius:50%;padding:3px}.header-avatar{display:grid;width:34px;height:34px;place-items:center;border-radius:50%;object-fit:cover}.header-avatar.initials{color:#fff;background:var(--store-primary);font-size:12px;font-weight:800}.compact-sign-in{display:inline-flex;align-items:center;justify-content:center;min-height:38px;border-radius:10px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;text-decoration:none}.account-dropdown{z-index:110}.account-dropdown-summary{display:flex;align-items:center;gap:9px;margin:2px 2px 6px;padding:8px;border-bottom:1px solid var(--store-border)}.account-dropdown-summary>span{display:grid;min-width:0;gap:2px}.account-dropdown-summary strong{overflow:hidden;font-size:12px;text-overflow:ellipsis;white-space:nowrap}.account-dropdown-summary small{color:var(--store-muted);font-size:9px}.account-dropdown-summary em{color:var(--store-primary-dark);font-size:9px;font-style:normal;font-weight:800}
    .account-menu-wrap{position:relative}.account-trigger{font:750 10px Manrope,sans-serif}.header-avatar{display:grid;width:24px;height:24px;place-items:center;border-radius:50%;object-fit:cover}.header-avatar.initials{color:#fff;background:var(--store-primary);font-size:10px}.account-dropdown{position:absolute;z-index:30;top:calc(100% + 7px);right:0;display:grid;width:230px;border:1px solid var(--store-border);border-radius:13px;padding:6px;background:#fff;box-shadow:0 14px 34px rgba(31,48,39,.16)}.account-dropdown a,.account-dropdown button{display:flex;align-items:center;justify-content:space-between;min-height:42px;border:0;border-radius:8px;padding:0 11px;color:var(--store-text);background:#fff;font:750 11px Manrope,sans-serif;text-align:left;text-decoration:none;cursor:pointer}.account-dropdown a:hover,.account-dropdown a:focus-visible,.account-dropdown button:hover,.account-dropdown button:focus-visible{outline:0;background:var(--store-primary-soft);color:var(--store-primary-dark)}.account-dropdown span{color:var(--store-muted);font-size:17px}
    .store-header{position:sticky;z-index:20;top:0;border-bottom:1px solid var(--store-border);background:rgba(255,255,255,.96);backdrop-filter:blur(14px)}.customer-nav{display:flex;justify-content:flex-end;gap:7px;width:min(var(--store-content-width),100%);margin:-6px auto 8px;padding:0 24px;overflow-x:auto;scrollbar-width:none}.customer-nav::-webkit-scrollbar{display:none}.account-action{display:inline-flex;flex:0 0 auto;align-items:center;justify-content:center;gap:6px;min-height:32px;border:1px solid var(--store-border);border-radius:9px;padding:5px 10px;color:var(--store-primary-dark);background:#fff;font:750 10px Manrope,sans-serif;text-decoration:none;white-space:nowrap;cursor:pointer;transition:background .16s,border-color .16s,box-shadow .16s}.account-action:hover{border-color:var(--store-primary);background:var(--store-primary-soft)}.account-action:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.account-action svg{width:15px;height:15px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.account-action.primary-action{border-color:var(--store-primary);color:#fff;background:var(--store-primary)}.account-action.primary-action:hover{background:var(--store-primary-dark)}.account-action.sign-out-action{color:var(--store-muted)}
    .header-inner{display:grid;grid-template-columns:minmax(180px,240px) minmax(220px,1fr) auto auto auto;align-items:center;gap:14px;width:min(var(--store-content-width),100%);min-height:68px;margin:auto;padding:9px 24px}
    .brand{display:flex;align-items:center;gap:10px;min-width:0;text-decoration:none}.brand-mark,.brand img{display:grid;flex:0 0 auto;width:42px;height:42px;place-items:center;border-radius:12px}.brand picture{display:block;flex:0 0 42px;width:42px;height:42px}.brand-mark{color:#fff;background:var(--store-primary);font:800 19px Manrope,sans-serif}.brand img{object-fit:cover}
    .brand-copy{display:grid;min-width:0;line-height:1.15}.brand-copy strong{overflow:hidden;color:var(--store-text);font:800 17px Manrope,sans-serif;text-overflow:ellipsis;white-space:nowrap}.brand-copy small{margin-top:4px;color:var(--store-muted);font-size:10px}
    .search-wrap{display:flex;align-items:center;width:100%;height:44px;border:1px solid var(--store-border);border-radius:12px;padding:0 12px;background:var(--store-background);transition:.18s}.search-wrap:focus-within{border-color:var(--store-primary);background:#fff;box-shadow:0 0 0 3px var(--store-primary-soft)}.search-wrap svg{width:20px;fill:none;stroke:var(--store-muted);stroke-linecap:round;stroke-width:1.8}.search-wrap input{flex:1;min-width:0;height:100%;border:0;padding:0 10px;color:var(--store-text);background:transparent;outline:0;font-size:14px}.search-wrap input::placeholder{color:#87918a}.clear-search{display:grid;width:30px;height:30px;place-items:center;border:0;border-radius:50%;color:var(--store-muted);background:transparent;font-size:22px;cursor:pointer}.clear-search:hover{background:var(--store-surface-muted)}
    .desktop-cart{display:flex;align-items:center;justify-content:center;gap:8px;min-height:42px;border:1px solid var(--store-primary);border-radius:11px;padding:5px 10px;color:var(--store-primary-dark);background:var(--store-primary-soft);cursor:pointer}.desktop-cart:hover{background:#dcefe1}.desktop-cart:focus-visible,.compact-sign-in:focus-visible,.header-icon-action:focus-visible,.header-avatar-button:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.cart-icon-wrap{position:relative;display:grid;place-items:center}.desktop-cart svg{width:21px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.cart-copy{display:grid;text-align:left}.desktop-cart strong{font-size:12px}.cart-badge{display:none;position:absolute;top:-8px;right:-11px;min-width:18px;height:18px;align-items:center;justify-content:center;border:2px solid #fff;border-radius:99px;padding:0 4px;color:#fff;background:var(--store-primary);font-size:9px;line-height:1}.desktop-cart small{margin-top:1px;color:var(--store-muted);font-size:10px;white-space:nowrap}
    .store-content{width:min(var(--store-content-width),100%);min-height:calc(100vh - 77px);margin:auto;padding:24px 24px 80px}    .pwa-install-card{display:grid;grid-template-columns:40px minmax(0,1fr) auto auto;align-items:center;gap:10px;width:min(var(--store-content-width),calc(100% - 48px));margin:12px auto 0;border:1px solid var(--store-border);border-radius:14px;padding:9px 10px;background:#fff;box-shadow:0 5px 16px rgba(31,48,39,.08)}.pwa-install-icon{display:grid;width:40px;height:40px;place-items:center;border-radius:10px;color:#fff;background:var(--store-primary);font-weight:800}.pwa-install-icon img{width:100%;height:100%;border-radius:10px;object-fit:cover}.pwa-install-copy{display:grid;min-width:0;gap:2px}.pwa-install-copy strong{overflow:hidden;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.pwa-install-copy small{overflow:hidden;color:var(--store-muted);font-size:9px;text-overflow:ellipsis;white-space:nowrap}.pwa-install-card>button:not(.pwa-dismiss){min-height:32px;border:0;border-radius:9px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;cursor:pointer}.pwa-dismiss{display:grid;width:26px;height:26px;place-items:center;border:0;border-radius:50%;color:var(--store-muted);background:transparent;font-size:18px;cursor:pointer}.pwa-dismiss:hover{background:var(--store-surface-muted)}.pwa-ios-hint{margin-top:12px}
    .store-toast{position:fixed;z-index:50;right:18px;bottom:24px;border-radius:10px;padding:11px 16px;color:#fff;background:var(--store-primary-dark);box-shadow:0 8px 24px rgba(0,0,0,.18);font-size:12px;font-weight:700}.state-page{display:grid;min-height:100vh;align-content:center;justify-items:center;padding:28px;text-align:center}.state-icon{display:grid;width:58px;height:58px;place-items:center;border-radius:18px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 24px Manrope,sans-serif}.state-page h1{margin:16px 0 5px;font:800 24px Manrope,sans-serif}.state-page p{max-width:390px;margin:0;color:var(--store-muted);line-height:1.6}.state-page button,.state-page a{margin-top:18px;border:0;border-radius:12px;padding:12px 18px;color:#fff;background:var(--store-primary);font-weight:700;text-decoration:none;cursor:pointer}
    .shell-skeleton{width:min(var(--store-content-width),100%);margin:auto;padding:15px 24px}.skeleton-header{display:flex;align-items:center;gap:10px}.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{display:block;background:linear-gradient(90deg,#edf0eb 25%,#f8f9f7 50%,#edf0eb 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.skeleton-header i{width:42px;height:42px;border-radius:12px}.skeleton-header span{width:150px;height:17px;border-radius:6px}.skeleton-header b{width:120px;height:42px;margin-left:auto;border-radius:12px}.skeleton-search{height:48px;margin:18px 0;border-radius:14px}.skeleton-body{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-top:40px}.skeleton-body div{height:280px;border-radius:18px}@keyframes shimmer{to{background-position:-200% 0}}
    @media(max-width:520px){.pwa-install-card{width:calc(100% - 24px);grid-template-columns:34px minmax(0,1fr) auto auto;gap:7px;padding:8px}.pwa-install-icon{width:34px;height:34px}.pwa-install-card>button:not(.pwa-dismiss){padding:0 9px;font-size:9px}}
    @media(min-width:768px) and (max-width:1199px){.header-inner{grid-template-columns:minmax(150px,205px) minmax(150px,1fr) auto auto auto;gap:10px;padding-right:18px;padding-left:18px}.brand-mark,.brand img{width:38px;height:38px}.brand-copy strong{font-size:15px}.brand-copy small{font-size:9px}.desktop-cart{gap:6px;padding:5px 8px}.desktop-cart small{display:none}}
    @media(max-width:767px){.header-inner{grid-template-columns:minmax(0,1fr) auto;gap:10px;min-height:auto;padding:10px 16px 12px}.brand-mark,.brand img{width:38px;height:38px}.brand-copy strong{font-size:16px}.desktop-cart{width:44px;min-height:42px;padding:0}.cart-copy{display:none}.cart-badge{display:flex}.search-wrap{grid-column:1/-1;grid-row:2;height:46px}.customer-nav{justify-content:flex-start;padding:0 16px 9px}.store-content{padding:18px 16px 104px}.skeleton-body{grid-template-columns:repeat(2,1fr);gap:10px}.skeleton-body div{height:235px}}
    @media(max-width:360px){.header-inner,.store-content{padding-right:12px;padding-left:12px}.customer-nav{padding-right:12px;padding-left:12px}.brand-copy small{display:none}}
    @media(prefers-reduced-motion:reduce){.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{animation:none}}
  `, `@media(max-width:767px){.header-inner{grid-template-columns:minmax(0,1fr) auto auto auto}.header-icon-action,.header-avatar-button{width:38px;height:38px}.header-avatar{width:30px;height:30px}.search-wrap{grid-column:1/-1;grid-row:2}.account-dropdown{position:fixed;top:70px;right:12px;width:min(280px,calc(100vw - 24px))}}@media(max-width:360px){.header-inner{padding-right:12px;padding-left:12px}.header-icon-action,.header-avatar-button{width:36px;height:36px}}`,
  `
    .home-menu-button,.home-search-shortcut{display:grid;place-items:center;width:42px;height:42px;border:1px solid transparent;border-radius:13px;color:#fff;background:transparent;cursor:pointer}
    .home-menu-button svg,.home-search-shortcut svg{width:24px;height:24px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}
    .home-menu-button:hover,.home-search-shortcut:hover{background:rgba(255,255,255,.12)}
    .home-menu-button:focus-visible,.home-search-shortcut:focus-visible{outline:2px solid #fff;outline-offset:2px}
    .home-store-frame .store-header{border-bottom:0;background:var(--store-primary-dark);backdrop-filter:none}
    .home-store-frame .header-inner{grid-template-columns:42px minmax(0,1fr) repeat(3,42px) auto;grid-template-rows:auto auto;gap:8px 12px;padding:12px 24px 15px}
    .home-store-frame .home-menu-button{grid-column:1;grid-row:1}
    .home-store-frame .brand{grid-column:2;grid-row:1}
    .home-store-frame .brand-copy strong{color:#fff;font-size:21px}
    .home-store-frame .brand-copy small{color:rgba(255,255,255,.82);font-size:11px}
    .home-store-frame .home-search-shortcut{grid-column:3;grid-row:1}
    .home-store-frame .wishlist-header{grid-column:4;grid-row:1}
    .home-store-frame .desktop-cart{grid-column:5;grid-row:1;width:42px;min-height:42px;border-color:transparent;padding:0;color:#fff;background:transparent}
    .home-store-frame .desktop-cart:hover{background:rgba(255,255,255,.12)}
    .home-store-frame .cart-copy{display:none}
    .home-store-frame .desktop-cart svg{width:25px;height:25px}
    .home-store-frame .home-menu-button,.home-store-frame .home-search-shortcut,.home-store-frame .wishlist-header,.home-store-frame .desktop-cart,.home-store-frame .header-avatar-button{color:#fff;border-color:transparent;background:transparent}
    .home-store-frame .header-icon-action:hover,.home-store-frame .header-avatar-button:hover{background:rgba(255,255,255,.12)}
    .home-store-frame .wishlist-header svg{width:25px;height:25px}
    .home-store-frame .wishlist-badge,.home-store-frame .cart-badge{display:flex;border-color:var(--store-primary-dark);background:#ed3d43}
    .home-store-frame .compact-sign-in{grid-column:6;grid-row:1;min-height:38px;border:1px solid rgba(255,255,255,.55);border-radius:999px;padding:0 14px;color:#fff;background:rgba(255,255,255,.12);font-size:11px}
    .home-store-frame .account-menu-wrap{grid-column:6;grid-row:1}
    .home-store-frame .header-avatar-button{width:42px;height:42px}
    .home-store-frame .header-avatar.initials{color:var(--store-primary-dark);background:#fff}
    .home-store-frame .search-wrap{grid-column:1/-1;grid-row:2;height:48px;border:0;border-radius:14px;padding:0 15px;background:#fff}
    .home-store-frame .search-wrap:focus-within{box-shadow:0 0 0 3px rgba(255,255,255,.24)}
    .home-store-frame .search-wrap svg{width:22px;height:22px;color:#59645d}
    .home-store-frame .search-wrap input{font-size:14px}
    .home-store-frame .search-wrap input::placeholder{color:#68736c}
    .home-store-frame .store-content{padding-top:18px}
    @media(max-width:767px){
      .home-store-frame .header-inner{grid-template-columns:36px minmax(0,1fr) repeat(3,38px) auto;gap:7px 6px;padding:10px 16px 14px}
      .home-store-frame .home-menu-button,.home-store-frame .home-search-shortcut,.home-store-frame .wishlist-header,.home-store-frame .desktop-cart{display:grid;width:38px;height:38px;min-height:38px}
      .home-store-frame .home-menu-button{grid-column:1;grid-row:1}
      .home-store-frame .brand{grid-column:2;grid-row:1;gap:7px}
      .home-store-frame .brand-mark,.home-store-frame .brand img,.home-store-frame .brand picture{width:36px;height:36px}
      .home-store-frame .brand-copy strong{font-size:16px}
      .home-store-frame .brand-copy small{display:block;max-width:125px;overflow:hidden;font-size:8px;text-overflow:ellipsis;white-space:nowrap}
      .home-store-frame .home-search-shortcut{grid-column:3;grid-row:1}
      .home-store-frame .wishlist-header{grid-column:4;grid-row:1}
      .home-store-frame .desktop-cart{grid-column:5;grid-row:1}
      .home-store-frame .account-menu-wrap,.home-store-frame .compact-sign-in{grid-column:6;grid-row:1}
      .home-store-frame .header-avatar-button{width:38px;height:38px}
      .home-store-frame .search-wrap{grid-column:1/-1;grid-row:2;height:46px}
      .home-store-frame .store-content{padding:14px 16px 104px}
    }
    @media(max-width:390px){.home-store-frame .header-inner{padding-right:12px;padding-left:12px;gap:6px 4px}.home-store-frame .home-menu-button,.home-store-frame .home-search-shortcut,.home-store-frame .wishlist-header,.home-store-frame .desktop-cart{width:34px;height:36px;min-height:36px}.home-store-frame .brand-mark,.home-store-frame .brand img,.home-store-frame .brand picture{width:33px;height:33px}.home-store-frame .brand-copy small{max-width:104px;font-size:7.5px}}
    @media(prefers-reduced-motion:reduce){.home-menu-button,.home-search-shortcut{scroll-behavior:auto}}
  `,
  `.order-contact-actions{position:fixed;z-index:18;right:max(16px,env(safe-area-inset-right));bottom:max(16px,env(safe-area-inset-bottom));display:flex;gap:9px}.order-contact-actions a{display:inline-flex;align-items:center;justify-content:center;gap:8px;min-height:46px;border:1px solid transparent;border-radius:999px;padding:0 17px;color:#fff;font:750 12px Manrope,sans-serif;text-decoration:none;box-shadow:0 8px 24px rgba(20,48,35,.2);transition:transform .16s,box-shadow .16s}.order-contact-actions a:hover{transform:translateY(-2px);box-shadow:0 11px 27px rgba(20,48,35,.23)}.order-contact-actions a:focus-visible{outline:3px solid #8bc4a9;outline-offset:3px}.order-contact-actions .call-order{background:var(--store-primary-dark)}.order-contact-actions .whatsapp-order{color:#173b29;background:#e4f5e9;border-color:#b7dec4}.order-contact-actions span[aria-hidden=true]{font-size:18px;line-height:1}@media(max-width:800px){.order-contact-actions{right:12px;bottom:max(12px,env(safe-area-inset-bottom));gap:7px}.order-contact-actions a{min-height:44px;padding:0 13px;font-size:11px}.store-content{padding-bottom:120px}}@media(max-width:360px){.order-contact-actions{right:8px;left:8px}.order-contact-actions a{flex:1;gap:5px;padding:0 8px;font-size:10px}}@media(prefers-reduced-motion:reduce){.order-contact-actions a{transition:none}.order-contact-actions a:hover{transform:none}}`,
  `.analytics-consent{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:14px;width:min(var(--store-content-width),calc(100% - 32px));margin:16px auto 0;border:1px solid var(--store-border);border-radius:14px;padding:14px 16px;background:var(--store-surface);box-shadow:0 3px 14px rgba(20,40,30,.06)}.analytics-consent-copy{display:grid;min-width:0;gap:5px}.analytics-consent-copy h2{margin:0;color:var(--store-text);font:800 14px Manrope,sans-serif;line-height:1.35}.analytics-consent-copy p{margin:0;color:var(--store-muted);font-size:11px;line-height:1.45}.analytics-consent-copy>small{color:var(--store-muted);font-size:10px;line-height:1.4}.analytics-details{width:max-content;color:var(--store-primary-dark);font-size:10px;font-weight:750;text-underline-offset:2px}.analytics-consent-actions{display:flex;align-items:center;gap:8px}.analytics-consent-actions button{display:inline-flex;align-items:center;justify-content:center;min-height:40px;border:1px solid var(--store-primary);border-radius:9px;padding:0 13px;color:#fff;background:var(--store-primary);font:750 10px Manrope,sans-serif;white-space:nowrap;cursor:pointer}.analytics-consent-actions button:hover:not(:disabled){background:var(--store-primary-dark)}.analytics-consent-actions button.consent-secondary{color:var(--store-primary-dark);background:var(--store-surface)}.analytics-consent-actions button.consent-secondary:hover:not(:disabled){background:var(--store-primary-soft)}.analytics-consent-actions button:focus-visible,.analytics-details:focus-visible,.analytics-consent-error button:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.analytics-consent-actions button:disabled,.analytics-consent-error button:disabled{opacity:.6;cursor:wait}.analytics-consent-error{grid-column:1/-1;display:flex;align-items:center;gap:10px;color:var(--store-danger);font-size:11px;line-height:1.4}.analytics-consent-error button{border:0;padding:2px;color:var(--store-primary-dark);background:transparent;font:750 10px Manrope,sans-serif;text-decoration:underline;text-underline-offset:2px;cursor:pointer}@media(max-width:620px){.analytics-consent{grid-template-columns:minmax(0,1fr);gap:8px;width:calc(100% - 24px);margin-top:8px;padding:10px}.analytics-consent-copy{gap:3px}.analytics-consent-copy h2{font-size:12px}.analytics-consent-copy p{font-size:10px;line-height:1.35}.analytics-details{font-size:9px}.analytics-consent-actions{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:7px}.analytics-consent-actions button{min-width:0;min-height:38px;padding:0 8px;white-space:normal}.analytics-consent-error{align-items:flex-start;flex-wrap:wrap;font-size:10px}}`,
     '.store-privacy-actions{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));justify-content:center;align-items:center;gap:8px 22px;width:min(var(--store-content-width),calc(100% - 32px));margin:26px auto 0;padding:16px 24px;border-top:1px solid var(--store-border);color:var(--store-muted)}.store-privacy-actions a,.store-privacy-actions button{display:inline-flex;align-items:center;justify-content:center;min-height:36px;max-width:100%;border:0;border-radius:8px;padding:6px 8px;color:var(--store-muted);background:transparent;font:650 11px Manrope,sans-serif;line-height:1.35;text-align:center;text-decoration:none;overflow-wrap:anywhere;cursor:pointer;transition:color .16s,background .16s}.store-privacy-actions a:hover,.store-privacy-actions button:hover{color:var(--store-primary-dark);background:var(--store-primary-soft)}.store-privacy-actions a:focus-visible,.store-privacy-actions button:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px;color:var(--store-primary-dark)}.store-privacy-actions button{appearance:none}@media(min-width:768px) and (max-width:899px){.store-privacy-actions{grid-template-columns:repeat(2,minmax(0,1fr));width:min(620px,calc(100% - 32px))}}@media(max-width:767px){.store-privacy-actions{grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 10px;width:calc(100% - 24px);margin-top:20px;padding:12px 8px}.store-privacy-actions a,.store-privacy-actions button{min-width:0;min-height:40px;padding:7px 6px;font-size:10px;line-height:1.3;overflow-wrap:anywhere}}@media(max-width:359px){.store-privacy-actions{gap:5px 6px;padding-right:4px;padding-left:4px}.store-privacy-actions a,.store-privacy-actions button{font-size:9px;padding-right:3px;padding-left:3px}}',
     '.marketplace-store-frame .store-header{border:0;color:#fff;background:var(--store-primary-dark,#00633f)}.marketplace-store-frame .header-inner{display:grid;grid-template-columns:44px minmax(0,1fr) 44px 44px 44px;grid-template-rows:auto auto;gap:8px 12px;min-height:0;padding:12px 24px 14px}.marketplace-store-frame .home-menu-button{grid-column:1;grid-row:1;display:grid;width:44px;height:44px;place-items:center;border:0;border-radius:12px;color:#fff;background:transparent;cursor:pointer}.marketplace-store-frame .home-menu-button svg{width:25px;height:25px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-width:2}.marketplace-store-frame .brand{grid-column:2;grid-row:1;gap:11px;color:#fff}.marketplace-store-frame .brand-mark,.marketplace-store-frame .brand img,.marketplace-store-frame .brand picture{width:48px;height:48px;flex-basis:48px;border-radius:50%}.marketplace-store-frame .brand img{object-fit:cover;background:#fff}.marketplace-store-frame .brand-copy{gap:3px}.marketplace-store-frame .brand-copy strong{display:-webkit-box;overflow:hidden;color:#fff;font:800 19px/1.08 Manrope,sans-serif;text-overflow:ellipsis;white-space:normal;-webkit-box-orient:vertical;-webkit-line-clamp:2;overflow-wrap:anywhere}.marketplace-store-frame .brand-copy small{display:-webkit-box;overflow:hidden;margin:0;color:rgba(255,255,255,.82);font-size:10px;line-height:1.2;-webkit-box-orient:vertical;-webkit-line-clamp:1}.marketplace-store-frame .wishlist-header{grid-column:3;grid-row:1;overflow:visible}.marketplace-store-frame .desktop-cart{grid-column:4;grid-row:1;display:grid;width:44px;height:44px;min-height:44px;place-items:center;overflow:visible;border:0;border-radius:12px;padding:0;color:#fff;background:transparent}.marketplace-store-frame .desktop-cart svg{width:26px;height:26px;stroke:#fff}.marketplace-store-frame .cart-copy{display:none}.marketplace-store-frame .wishlist-header,.marketplace-store-frame .header-avatar-button{width:44px;height:44px;border:0;border-radius:12px;color:#fff;background:transparent}.marketplace-store-frame .wishlist-header svg,.marketplace-store-frame .guest-profile svg{width:26px;height:26px;fill:none;stroke:#fff;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.marketplace-store-frame .header-avatar{width:32px;height:32px}.marketplace-store-frame .header-avatar.initials{color:var(--store-primary-dark,#00633f);background:#fff}.marketplace-store-frame .header-count-icon{position:relative;display:grid;width:26px;height:26px;place-items:center;overflow:visible}.marketplace-store-frame .header-count-icon>svg{display:block}.marketplace-store-frame .header-count-badge{position:absolute;top:-7px;right:-7px;z-index:2;display:grid;min-width:18px;height:18px;place-items:center;border:2px solid var(--store-primary-dark,#00633f);border-radius:99px;padding:0 4px;color:#fff;background:#ec343f;font:800 9px/1 Manrope,sans-serif;white-space:nowrap}.marketplace-store-frame .cart-badge,.marketplace-store-frame .wishlist-badge{top:-7px;right:-7px}.marketplace-store-frame .search-wrap{grid-column:1/-1;grid-row:2;height:46px;border:0;border-radius:14px;padding:0 13px;background:#fff}.marketplace-store-frame .search-wrap svg{width:20px;height:20px;flex:0 0 auto;stroke:#5c625e}.marketplace-store-frame .search-wrap input{padding-left:9px;color:#17221b;font-size:14px}.marketplace-store-frame .search-wrap input::placeholder{color:#737a75}.marketplace-store-frame .store-quick-menu{position:absolute;z-index:120;top:calc(100% - 5px);left:max(14px,calc((100% - var(--store-content-width))/2 + 22px));display:grid;width:min(260px,calc(100vw - 28px));border:1px solid var(--store-border);border-radius:14px;padding:6px;background:#fff;box-shadow:0 16px 34px rgba(0,0,0,.2)}.marketplace-store-frame .store-quick-menu a,.marketplace-store-frame .store-quick-menu button{display:flex;align-items:center;min-height:43px;border:0;border-radius:9px;padding:0 12px;color:var(--store-text);background:#fff;font:700 12px Manrope,sans-serif;text-align:left;text-decoration:none;cursor:pointer}.marketplace-store-frame .store-quick-menu a:hover,.marketplace-store-frame .store-quick-menu button:hover{color:var(--store-primary-dark);background:var(--store-primary-soft)}.marketplace-store-frame .home-menu-button:focus-visible,.marketplace-store-frame .header-icon-action:focus-visible,.marketplace-store-frame .header-avatar-button:focus-visible,.marketplace-store-frame .desktop-cart:focus-visible{outline:2px solid #b5e3c5;outline-offset:2px}.marketplace-store-frame .store-content{padding-top:18px}@media(max-width:700px){.marketplace-store-frame .header-inner{width:100%;grid-template-columns:36px minmax(0,1fr) 36px 36px 36px;gap:7px 5px;padding:9px 14px 11px}.marketplace-store-frame .home-menu-button,.marketplace-store-frame .desktop-cart,.marketplace-store-frame .wishlist-header,.marketplace-store-frame .header-avatar-button{width:36px;height:38px;min-height:38px}.marketplace-store-frame .brand{gap:7px}.marketplace-store-frame .brand-mark,.marketplace-store-frame .brand img,.marketplace-store-frame .brand picture{width:38px;height:38px;flex-basis:38px}.marketplace-store-frame .brand-copy strong{font-size:15px;line-height:1.05}.marketplace-store-frame .brand-copy small{font-size:9px}.marketplace-store-frame .desktop-cart svg,.marketplace-store-frame .wishlist-header svg,.marketplace-store-frame .guest-profile svg{width:23px;height:23px}.marketplace-store-frame .store-content{padding-top:14px}}@media(max-width:390px){.marketplace-store-frame .header-inner{grid-template-columns:32px minmax(0,1fr) 32px 32px 32px;gap:6px 4px;padding-right:10px;padding-left:10px}.marketplace-store-frame .home-menu-button,.marketplace-store-frame .desktop-cart,.marketplace-store-frame .wishlist-header,.marketplace-store-frame .header-avatar-button{width:32px;height:36px;min-height:36px}.marketplace-store-frame .brand{gap:6px}.marketplace-store-frame .brand-mark,.marketplace-store-frame .brand img,.marketplace-store-frame .brand picture{width:34px;height:34px;flex-basis:34px}.marketplace-store-frame .brand-copy strong{font-size:14px}.marketplace-store-frame .brand-copy small{display:none}.marketplace-store-frame .desktop-cart svg,.marketplace-store-frame .wishlist-header svg,.marketplace-store-frame .guest-profile svg{width:21px;height:21px}}@media(max-width:340px){.marketplace-store-frame .header-inner{grid-template-columns:29px minmax(0,1fr) 29px 29px 29px;gap:5px 3px;padding-right:8px;padding-left:8px}.marketplace-store-frame .home-menu-button,.marketplace-store-frame .desktop-cart,.marketplace-store-frame .wishlist-header,.marketplace-store-frame .header-avatar-button{width:29px;height:34px;min-height:34px}.marketplace-store-frame .brand-mark,.marketplace-store-frame .brand img,.marketplace-store-frame .brand picture{width:31px;height:31px;flex-basis:31px}.marketplace-store-frame .brand-copy strong{font-size:13px}.marketplace-store-frame .brand{gap:5px}}@media(min-width:701px) and (max-width:1050px){.marketplace-store-frame .header-inner{padding-right:20px;padding-left:20px;gap:8px}.marketplace-store-frame .brand-copy strong{font-size:17px}.marketplace-store-frame .brand-copy small{font-size:9px}}',
    '.header-avatar-button,.marketplace-store-frame .header-avatar-button,.home-store-frame .header-avatar-button{box-sizing:border-box;display:grid;flex:0 0 42px;width:42px;min-width:42px;height:42px;min-height:42px;aspect-ratio:1/1;place-items:center;overflow:hidden;border-radius:50%;padding:0}.header-avatar-button .header-avatar,.marketplace-store-frame .header-avatar-button .header-avatar,.home-store-frame .header-avatar-button .header-avatar{display:grid;flex:0 0 100%;width:100%;max-width:none;height:100%;max-height:none;place-items:center;object-fit:cover;border-radius:50%}.header-avatar-button.guest-profile svg{display:block;width:22px;height:22px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.marketplace-store-frame .header-avatar-button{border-radius:50%}@media(max-width:767px){.header-avatar-button,.marketplace-store-frame .header-avatar-button,.home-store-frame .header-avatar-button{flex-basis:38px;width:38px;min-width:38px;height:38px;min-height:38px}.header-avatar-button.guest-profile svg{width:21px;height:21px}}@media(max-width:390px){.header-avatar-button,.marketplace-store-frame .header-avatar-button,.home-store-frame .header-avatar-button{flex-basis:36px;width:36px;min-width:36px;height:36px;min-height:36px}}@media(max-width:340px){.header-avatar-button,.marketplace-store-frame .header-avatar-button,.home-store-frame .header-avatar-button{flex-basis:30px;width:30px;min-width:30px;height:30px;min-height:30px}.header-avatar-button.guest-profile svg{width:18px;height:18px}}.store-privacy-actions{display:flex;flex-wrap:wrap;justify-content:center;align-items:center;gap:4px 12px;width:100%;margin:18px auto 0;border-top:1px solid var(--store-border);padding:9px 16px 12px;background:var(--store-surface-muted,#f4f6f4);color:var(--store-muted)}.store-privacy-actions a,.store-privacy-actions button{min-height:38px;padding:6px 10px;font-size:11px}.store-privacy-actions a:hover,.store-privacy-actions button:hover{background:var(--store-primary-soft)}@media(max-width:767px){.store-privacy-actions{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:2px 8px;margin-top:14px;padding:8px 12px 10px}.store-privacy-actions a,.store-privacy-actions button{min-width:0;min-height:40px;padding:6px 4px;font-size:10px;overflow-wrap:anywhere}}@media(max-width:359px){.store-privacy-actions{gap:2px 5px;padding-right:8px;padding-left:8px}.store-privacy-actions a,.store-privacy-actions button{font-size:9px;padding-right:2px;padding-left:2px}}'
  ],
})
export class StoreShell implements OnInit {
  readonly routeKind = signal<'RETAILER'|'CITY'|'NOT_FOUND'>('RETAILER');
  readonly routeSlug = signal('');
  readonly store = signal<Store | null>(null);
  readonly accountMenuOpen = signal(false);
  readonly headerMenuOpen = signal(false);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly shoppingRoute = signal(false);
  readonly homeRoute = signal(false);
  readonly freeDeliveryProgressRoute = signal(false);
  readonly shoppingQuote = signal<CartQuote | null>(null);
  readonly deliveryLocationVerified = signal(false);
  readonly freeDeliveryToastVisible = signal(false);
  readonly logoLoadFailed = signal(false);
  readonly logoThumbnailFailed = signal(false);
  readonly startupContentReady = signal(false);
  readonly analyticsPrompt = signal(false);
  readonly analyticsConsentBusy = signal(false);
  readonly analyticsConsentError = signal('');
  readonly analyticsChoiceToRetry = signal<boolean | null>(null);
  private quoteRequest = 0;
  private freeDeliveryTimer?: ReturnType<typeof setTimeout>;
  private deliveryToastContext = '';
  private lastDeliveryQuoteEligible = false;
  private lastQuoteMutationVersion = 0;
  private dismissedDeliveryMutationVersion = -1;
  private startupContentReadyStore = '';
  private startupServicesStartedFor = '';
  private firstVisibleStoreMarkedFor = '';
  private pendingInitialQuote: { key: string; lines: readonly CartLine[] } | null = null;
  private pendingCustomerFeatures: { key: string; customerId: string } | null = null;
  private storeLoadKey = '';
  private storeLoad: Promise<Store | null> = Promise.resolve(null);
  searchText = '';
  initials(name:string):string{return name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join('').toUpperCase();}
  presentationThumbnail(url:string):string{return `${url}${url.includes('?')?'&':'?'}thumbnail=true`;}
  logoImageUrl(url:string):string{return this.logoThumbnailFailed()?url:this.presentationThumbnail(url);}
  logoImageFailed(url:string,event:Event):void{const image=event.target as HTMLImageElement;if(image.currentSrc===this.presentationThumbnail(url)||image.src===this.presentationThumbnail(url)){this.logoThumbnailFailed.set(true);return;}this.logoLoadFailed.set(true);}
  private preloadPrimaryBanner(store:Store):void{
    document.head.querySelectorAll('link[data-store-lcp-image-preload]').forEach(link=>link.remove());
    const image=store.banners.find(banner=>banner.slot==='PRIMARY')??store.banners.find(banner=>banner.slot==='SECONDARY');if(!image)return;
    const variants=[
      {url:this.presentationThumbnail(image.imageUrl),media:'(max-width: 420px) and (max-resolution: 1.5dppx)'},
      {url:image.imageUrl,media:'(min-width: 421px), (min-resolution: 1.51dppx)'},
    ];
    for(const variant of variants){const link=document.createElement('link');link.rel='preload';link.as='image';link.href=variant.url;link.media=variant.media;link.setAttribute('fetchpriority','high');link.dataset['storeLcpImagePreload']='true';document.head.append(link);}
  }
  private readonly destroyRef = inject(DestroyRef);
  private engagementTimer?: ReturnType<typeof setInterval>;

  constructor(private readonly route: ActivatedRoute, private readonly router: Router, private readonly data: StorefrontDataService, private readonly publicRoutes: PublicMarketplaceApiService, readonly cart: CartService, readonly session: CustomerSessionService, readonly wishlist: WishlistService, readonly notify: StorefrontNotificationService, readonly pwa: PwaInstallService, readonly rewards: CustomerRewardsService, readonly activity: StorefrontActivityService) {
    effect(() => {
      const key = this.cart.storeKey(), lines = this.cart.lines(), mutationVersion = this.cart.userMutationVersion();
      if (key && lines.length && this.freeDeliveryProgressRoute()) {
        if (this.startupContentReadyStore !== key) this.pendingInitialQuote = { key, lines };
        else void this.refreshShoppingQuote(key, lines, mutationVersion);
      } else {
        this.pendingInitialQuote = null;
        this.quoteRequest++;
        this.shoppingQuote.set(null);
        this.deliveryLocationVerified.set(false);
        this.hideFreeDeliveryToast();
        if (!lines.length) this.lastDeliveryQuoteEligible = false;
      }
    });
    effect(() => {
      const key = this.cart.storeKey(); if (!key) return;
      const customerId = this.session.customerId(); this.cart.switchCustomer(customerId);
      if (!customerId) { this.pendingCustomerFeatures = null; void this.wishlist.switchCustomer(key, null); return; }
      if (this.startupContentReadyStore !== key) { this.pendingCustomerFeatures = { key, customerId }; return; }
      this.scheduleCustomerFeatures(key, customerId);
    });
    afterRenderEffect(() => {
      const currentStore = this.store();
      if (!currentStore || this.loading()) return;
      const key = currentStore.storeKey.toLowerCase();
      if (this.firstVisibleStoreMarkedFor !== key) {
        this.firstVisibleStoreMarkedFor = key;
        markStorefrontStartup('first-visible-storefront-content', { storeKey: key });
      }
      if (!this.isCatalogRoute()) this.markStartupContentReady(currentStore.storeKey);
    });
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params: ParamMap) => { void this.openPublicRoute(params.get('storeKey') ?? ''); });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => this.searchText = params.get('q') ?? '');
    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(event => { if (event instanceof NavigationEnd) { this.updateShoppingRoute(event.urlAfterRedirects); this.closeAccountMenu(); const key=this.store()?.storeKey;if(key)this.activity.record(key,'PAGE_VIEW'); } });
    this.engagementTimer=setInterval(()=>{const key=this.store()?.storeKey;if(key&&document.visibilityState==='visible')this.activity.record(key,'FOREGROUND_ENGAGEMENT',undefined,undefined,undefined,undefined,30);},30000);
    this.destroyRef.onDestroy(()=>{if(this.engagementTimer)clearInterval(this.engagementTimer);if(this.freeDeliveryTimer)clearTimeout(this.freeDeliveryTimer);});
    this.updateShoppingRoute(this.router.url);
  }
  updateSearch(event: Event): void { this.searchText = (event.target as HTMLInputElement).value; }
  clearSearch(): void { this.searchText = ''; this.navigateSearch(); }
  search(event: Event): void { event.preventDefault(); this.navigateSearch(); }
  goCart(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key, 'cart']); }
  toggleAccountMenu():void{this.accountMenuOpen.update(open=>!open);}
  toggleHeaderMenu():void{this.headerMenuOpen.update(open=>!open);}
  closeHeaderMenu():void{this.headerMenuOpen.set(false);}
  currentReturnUrl():string{return this.router.url;}
  closeAccountMenu():void{this.accountMenuOpen.set(false);}
  openAnalyticsPreferences():void{if(!this.store()?.storeKey)return;this.analyticsConsentError.set('');this.analyticsChoiceToRetry.set(null);this.analyticsPrompt.set(true);}
  retryAnalyticsChoice():void{const choice=this.analyticsChoiceToRetry();const key=this.store()?.storeKey;if(key&&choice!==null)void this.chooseAnalytics(key,choice);}
  async chooseAnalytics(storeKey:string,allow:boolean):Promise<void>{if(this.analyticsConsentBusy())return;this.analyticsConsentBusy.set(true);this.analyticsConsentError.set('');try{const result=allow?await this.activity.grantConsent(storeKey):await this.activity.rejectOrWithdraw(storeKey);const saved=allow?result==='SAVED':result==='SAVED';if(!saved){this.analyticsChoiceToRetry.set(allow?true:false);this.analyticsConsentError.set(allow?result==='UNAVAILABLE'?'Optional analytics is not enabled for this store. Your shopping and orders are unaffected.':'Your choice could not be saved because '+(result==='STORAGE_FAILED'?'browser storage is unavailable.':'the consent service is unavailable.')+' Optional analytics remains off.':result==='WITHDRAWAL_FAILED'?'Analytics is off on this device, but saved analytics could not be removed yet.':'Your choice could not be saved in browser storage. Optional analytics remains off.');return;}this.analyticsChoiceToRetry.set(null);this.analyticsPrompt.set(false);if(allow)this.activity.record(storeKey,'STORE_VISIT');}finally{this.analyticsConsentBusy.set(false);}}
  signOut(storeKey:string):void{this.closeAccountMenu();this.closeHeaderMenu();this.activity.record(storeKey,'CUSTOMER_SIGN_OUT');this.session.signOut(storeKey);const returnUrl=this.safePostLogoutReturnUrl(storeKey);void this.router.navigate(['/',storeKey,'auth'],returnUrl?{queryParams:{returnUrl}}:{});}
  private safePostLogoutReturnUrl(storeKey:string):string|null{const current=this.router.url;if(!current.startsWith('/')||current.startsWith('//')||current.includes('://'))return null;const path=current.split(/[?#]/,1)[0],segments=path.split('/').filter(Boolean);if(segments.length<2||segments[0].toLowerCase()!==storeKey.trim().toLowerCase())return null;let section=segments[1].toLowerCase();try{section=decodeURIComponent(section).toLowerCase();}catch{return null;}return ['account','auth','checkout'].includes(section)?null:current;}
  installApp(): void { void this.pwa.install(); }
  whatsappOrderUrl(store: Store): string { const number = (store.orderContactNumber ?? '').replace(/[^0-9]/g, ''); const message = encodeURIComponent(`Hi, I'd like to place an order from ${store.displayName || store.name}.`); return `https://wa.me/${number}?text=${message}`; }
  @HostListener('document:keydown.escape') closeAccountMenuOnEscape():void{this.closeAccountMenu();this.closeHeaderMenu();}
  @HostListener('document:click',['$event']) closeAccountMenuOnOutsideClick(event:MouseEvent):void{const target=event.target as Element|null;if(target&&!target.closest('.account-menu-wrap'))this.closeAccountMenu();}
  @HostListener('document:click',['$event']) closeHeaderMenuOnOutsideClick(event:MouseEvent):void{const target=event.target as Element|null;if(target&&!target.closest('.home-menu-button')&&!target.closest('.store-quick-menu'))this.closeHeaderMenu();}
  retry(): void { const key = this.route.snapshot.paramMap.get('storeKey') ?? ''; void this.openPublicRoute(key); }
  private async openPublicRoute(slug:string):Promise<void>{
    this.loading.set(true);this.loadFailed.set(false);this.logoLoadFailed.set(false);this.logoThumbnailFailed.set(false);this.routeSlug.set(slug);
    this.startupContentReady.set(false);this.startupContentReadyStore='';this.startupServicesStartedFor='';this.pendingInitialQuote=null;this.pendingCustomerFeatures=null;
    markStorefrontStartup('route-resolution-start', { slug });
    try{
      const mappedStoreKey=getCustomStoreDomain();
      if(mappedStoreKey){markStorefrontStartup('route-resolution-complete', { slug, source: 'custom-domain-map', storeKey: mappedStoreKey });measureStorefrontStartup('route-resolution','route-resolution-start','route-resolution-complete');this.routeKind.set('RETAILER');this.storeLoadKey=mappedStoreKey;this.session.selectStore(mappedStoreKey);this.cart.useStore(mappedStoreKey);this.wishlist.initialize(mappedStoreKey);this.storeLoad=this.load(mappedStoreKey);return;}
      const candidateKey=slug.trim().toLowerCase();
      // Overlap the public tenant lookup, but never publish it unless route resolution confirms this exact retailer key.
      const candidateStore=/^[a-z0-9_-]{1,100}$/i.test(candidateKey)?this.fetchStoreMetadata(candidateKey).then(store=>({store,failed:false}),()=>({store:null,failed:true})):null;
      const resolution=await this.publicRoutes.resolve(slug);
      markStorefrontStartup('route-resolution-complete', { slug, type: resolution?.type ?? 'NOT_FOUND', storeKey: resolution?.storeKey ?? null });
      measureStorefrontStartup('route-resolution','route-resolution-start','route-resolution-complete');
      if(!resolution){this.routeKind.set('NOT_FOUND');this.store.set(null);this.storeLoadKey=slug;this.loading.set(false);return;}
      if(resolution.type==='CITY'){this.routeKind.set('CITY');this.store.set(null);this.storeLoadKey=slug;this.loading.set(false);return;}
      const storeKey=resolution.storeKey??slug;this.routeKind.set('RETAILER');this.storeLoadKey=storeKey;this.session.selectStore(storeKey);this.cart.useStore(storeKey);this.wishlist.initialize(storeKey);
      const metadata = candidateStore && candidateKey === storeKey.trim().toLowerCase()
        ? candidateStore.then(result => result.failed || (result.store && result.store.storeKey.trim().toLowerCase() !== storeKey.trim().toLowerCase()) ? this.fetchStoreMetadata(storeKey) : result.store)
        : this.fetchStoreMetadata(storeKey);
      this.storeLoad=this.load(storeKey,metadata);
    }catch{markStorefrontStartup('route-resolution-failed',{slug});measureStorefrontStartup('route-resolution','route-resolution-start','route-resolution-failed');this.store.set(null);this.loadFailed.set(true);this.loading.set(false);}
  }
  private updateShoppingRoute(url: string): void {
    const segments = url.split('?')[0].split('/').filter(Boolean);
    const hidden = ['cart','checkout','account','auth','wishlist','orders'];
    this.shoppingRoute.set(segments.length > 0 && !hidden.includes(segments[1] ?? ''));
    this.homeRoute.set(segments.length <= 1 || (segments.length === 2 && segments[1].toLowerCase() === 'categories'));
    let activeRoute = this.router.routerState.snapshot.root;
    let showProgress = false;
    while (activeRoute) {
      if (activeRoute.routeConfig?.data?.['freeDeliveryProgress'] === true) showProgress = true;
      const nextRoute = activeRoute.firstChild;
      if (!nextRoute) break;
      activeRoute = nextRoute;
    }
    this.freeDeliveryProgressRoute.set(showProgress);
  }
  scrollToHomeCategories(): void { document.getElementById('home-categories')?.scrollIntoView({ block: 'start', behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' }); }
  focusStoreSearch(): void { document.querySelector<HTMLInputElement>('.store-header .search-wrap input')?.focus({ preventScroll: true }); }
  private async refreshShoppingQuote(key: string, lines: readonly CartLine[], mutationVersion: number): Promise<void> {
    const request = ++this.quoteRequest;
    const customerId = this.session.customerId()?.trim().toLowerCase() ?? '';
    const context = `${key.trim().toLowerCase()}:${customerId || 'guest'}`;
    if (this.deliveryToastContext !== context) {
      this.hideFreeDeliveryToast();
      this.deliveryLocationVerified.set(false);
      this.shoppingQuote.set(null);
      this.deliveryToastContext = context;
      this.lastDeliveryQuoteEligible = false;
      this.lastQuoteMutationVersion = mutationVersion;
      this.dismissedDeliveryMutationVersion = this.readDismissedMutationVersion(key, customerId);
    }
    const location = this.readVerifiedDeliveryLocation(key, customerId);
    this.deliveryLocationVerified.set(false);
    this.shoppingQuote.set(null);
    this.hideFreeDeliveryToast();
    try {
      const quote = await this.data.quote(key, lines, location?.pincode ?? '');
      if (request !== this.quoteRequest || this.cart.storeKey() !== key || !this.freeDeliveryProgressRoute() || this.cart.userMutationVersion() !== mutationVersion) return;
      const locationVerified = !!location && Date.now() - location.verifiedAtUtc < 15 * 60 * 1000;
      this.deliveryLocationVerified.set(locationVerified);
      const wasEligible = this.lastDeliveryQuoteEligible;
      const eligible = locationVerified && this.isEligibleForFreeDelivery(quote, lines);
      this.shoppingQuote.set(quote);
      this.lastDeliveryQuoteEligible = eligible;
      if (eligible && !wasEligible && mutationVersion > this.lastQuoteMutationVersion && mutationVersion !== this.dismissedDeliveryMutationVersion) this.showFreeDeliveryToast();
      else if (!eligible) this.hideFreeDeliveryToast();
      this.lastQuoteMutationVersion = Math.max(this.lastQuoteMutationVersion, mutationVersion);
    } catch {
      if (request === this.quoteRequest) {
        this.shoppingQuote.set(null);
        this.lastDeliveryQuoteEligible = false;
        this.deliveryLocationVerified.set(false);
        this.hideFreeDeliveryToast();
      }
    }
  }
  private readVerifiedDeliveryLocation(storeKey: string, customerId: string): { pincode: string; verifiedAtUtc: number } | null {
    if (typeof sessionStorage === 'undefined') return null;
    try {
      const key = `khata-dhari-storefront-serviceability:${storeKey.trim().toLowerCase()}:${customerId || 'guest'}`;
      const raw = sessionStorage.getItem(key);
      const state = raw ? JSON.parse(raw) as { storeKey?: string; pincode?: string; verified?: boolean; verifiedAtUtc?: number; quote?: CartQuote } : null;
      const age = state?.verifiedAtUtc === undefined ? Number.POSITIVE_INFINITY : Date.now() - state.verifiedAtUtc;
      return state?.storeKey?.trim().toLowerCase() === storeKey.trim().toLowerCase()
        && state.verified === true && Number.isFinite(age) && age >= 0 && age < 15 * 60 * 1000
        && typeof state.pincode === 'string' && /^[0-9]{6}$/.test(state.pincode)
        && state.quote?.isDeliveryEnabled === true && state.quote.isPincodeServiceable === true
        ? { pincode: state.pincode, verifiedAtUtc: state.verifiedAtUtc! } : null;
    } catch { return null; }
  }
  private isEligibleForFreeDelivery(quote: CartQuote | null, lines: readonly CartLine[]): boolean {
    return lines.some(line => Number.isFinite(line.quantity) && line.quantity > 0)
      && !!quote && quote.isDeliveryEnabled === true && quote.isPincodeServiceable === true
      && quote.freeDeliveryEnabled === true && Number.isFinite(quote.freeDeliveryThreshold) && (quote.freeDeliveryThreshold ?? 0) > 0
      && Number.isFinite(quote.eligibleAmount) && quote.eligibleAmount > 0 && quote.isFreeDeliveryUnlocked === true;
  }
  canShowFreeDeliveryProgress(): boolean {
    const quote = this.shoppingQuote();
    return this.freeDeliveryProgressRoute() && this.deliveryLocationVerified() && this.cart.lines().some(line => Number.isFinite(line.quantity) && line.quantity > 0)
      && !!quote && quote.isDeliveryEnabled && quote.isPincodeServiceable && quote.freeDeliveryEnabled === true
      && Number.isFinite(quote.freeDeliveryThreshold) && (quote.freeDeliveryThreshold ?? 0) > 0
      && Number.isFinite(quote.eligibleAmount) && quote.eligibleAmount > 0 && !quote.isFreeDeliveryUnlocked
      && Number.isFinite(quote.remainingAmount) && quote.remainingAmount > 0
      && this.cart.userMutationVersion() !== this.dismissedDeliveryMutationVersion;
  }
  private showFreeDeliveryToast(): void {
    if (this.freeDeliveryTimer) clearTimeout(this.freeDeliveryTimer);
    this.freeDeliveryToastVisible.set(true);
    this.freeDeliveryTimer = setTimeout(() => this.freeDeliveryToastVisible.set(false), 5000);
  }
  private hideFreeDeliveryToast(): void {
    if (this.freeDeliveryTimer) clearTimeout(this.freeDeliveryTimer);
    this.freeDeliveryTimer = undefined;
    this.freeDeliveryToastVisible.set(false);
  }
  dismissFreeDeliveryToast(): void {
    this.dismissedDeliveryMutationVersion = this.cart.userMutationVersion();
    const key = this.cart.storeKey();
    if (key) {
      const customerId = this.session.customerId()?.trim().toLowerCase() ?? '';
      try { localStorage.setItem(this.deliveryDismissalStorageKey(key, customerId), String(this.dismissedDeliveryMutationVersion)); } catch {}
    }
    this.hideFreeDeliveryToast();
  }
  private deliveryDismissalStorageKey(storeKey: string, customerId: string): string { return `khata-dhari-free-delivery-dismissal:${this.cart.revisionStorageKey(storeKey, customerId)}`; }
  private readDismissedMutationVersion(storeKey: string, customerId: string): number { try { const raw = localStorage.getItem(this.deliveryDismissalStorageKey(storeKey, customerId)); if (raw === null) return -1; const value = Number(raw); return Number.isSafeInteger(value) && value >= 0 ? value : -1; } catch { return -1; } }
  private navigateSearch(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key], { queryParams: { q: this.searchText.trim() || null } }); }
  markStartupContentReady(storeKey: string): void {
    const key = storeKey.trim().toLowerCase();
    if (!key || this.startupContentReadyStore === key) return;
    this.startupContentReadyStore = key;
    this.startupContentReady.set(true);
    const pendingCustomer = this.pendingCustomerFeatures;
    this.pendingCustomerFeatures = null;
    if (pendingCustomer?.key === key) this.scheduleCustomerFeatures(key, pendingCustomer.customerId);
    const pendingQuote = this.pendingInitialQuote;
    this.pendingInitialQuote = null;
    if (pendingQuote?.key === key) scheduleStorefrontIdle(() => {
      if (this.cart.storeKey() === key && this.freeDeliveryProgressRoute() && this.cart.lines() === pendingQuote.lines) void this.refreshShoppingQuote(key, pendingQuote.lines, this.cart.userMutationVersion());
    });
    if (this.startupServicesStartedFor === key) return;
    this.startupServicesStartedFor = key;
    scheduleStorefrontIdle(() => {
      if (this.store()?.storeKey.trim().toLowerCase() !== key) return;
      void this.session.restore(key);
      markStorefrontStartup('visitor-analytics-start', { storeKey: key });
      void this.activity.initializeConsent(key).then(state => {
        markStorefrontStartup('visitor-analytics-ready', { storeKey: key, state });
        if (this.store()?.storeKey.trim().toLowerCase() === key && state === 'PROMPT') this.analyticsPrompt.set(true);
        if (state === 'READY') this.activity.record(key, 'STORE_VISIT');
      });
    });
  }
  private isCatalogRoute(): boolean {
    const segments = this.router.url.split(/[?#]/, 1)[0].split('/').filter(Boolean).map(segment => segment.toLowerCase());
    const currentStoreKey = this.store()?.storeKey.trim().toLowerCase();
    if (segments[0] === currentStoreKey) segments.shift();
    const childPath = segments[0] ?? '';
    return childPath === '' || ['categories','stores','search'].includes(childPath) || (childPath === 'products' && segments.length === 1);
  }
  private scheduleCustomerFeatures(key: string, customerId: string): void {
    scheduleStorefrontIdle(() => {
      if (this.cart.storeKey() !== key || this.session.customerId() !== customerId) return;
      void this.wishlist.switchCustomer(key, customerId);
      void this.rewards.get(key).catch(() => {});
      void this.rewards.capturePending(key);
    });
  }
  private fetchStoreMetadata(storeKey: string): Promise<Store | null> {
    markStorefrontStartup('store-metadata-start', { storeKey });
    return this.data.getStore(storeKey).then(store => {
      if (store && store.storeKey.trim().toLowerCase() !== storeKey.trim().toLowerCase()) throw new Error('Resolved store metadata key did not match the requested tenant.');
      markStorefrontStartup('store-metadata-available', { storeKey, found: !!store });
      measureStorefrontStartup('store-metadata','store-metadata-start','store-metadata-available');
      return store;
    }, error => {
      markStorefrontStartup('store-metadata-failed', { storeKey });
      measureStorefrontStartup('store-metadata','store-metadata-start','store-metadata-failed');
      throw error;
    });
  }
  waitForStore(storeKey: string): Promise<Store | null> {
    const loadedKey = this.storeLoadKey.trim().toLowerCase();
    const requestedKey = storeKey.trim().toLowerCase();
    return loadedKey === requestedKey ? this.storeLoad : Promise.resolve(null);
  }
  private async load(storeKey: string, metadata: Promise<Store | null> = this.fetchStoreMetadata(storeKey)): Promise<Store | null> { try { const loaded = await metadata; if(loaded)this.preloadPrimaryBanner(loaded); this.store.set(loaded); if (loaded) this.pwa.configure(loaded); return loaded; } catch { this.store.set(null); this.loadFailed.set(true); return null; } finally { this.loading.set(false); } }
}
