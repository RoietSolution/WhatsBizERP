import { Component, DestroyRef, HostListener, OnInit, effect, inject, signal } from '@angular/core';
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
import { CityMarketplacePage } from './pages/city-marketplace.page';

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
      <shop-city-marketplace [slug]="routeSlug()" />
    } @else if (routeKind() === 'NOT_FOUND') {
      <main class="state-page"><span class="state-icon">?</span><h1>Page not found</h1><p>This store or city marketplace is unavailable.</p><a routerLink="/">Browse KhataDhari Shop</a></main>
    } @else if (loadFailed()) {
      <main class="state-page"><span class="state-icon">!</span><h1>Store temporarily unavailable</h1><p>We couldn't connect to this store. Check your connection and try again.</p><button type="button" (click)="retry()">Try again</button></main>
    } @else if (store(); as currentStore) {
      <div class="store-frame" [style.--store-primary]="currentStore.accentColor">
        <shop-campaign-popup [storeKey]="currentStore.storeKey" />
        <header class="store-header">
          <div class="header-inner">
            <a class="brand" [routerLink]="['/', currentStore.storeKey]" aria-label="Store home">
              @if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="currentStore.logoUrl" alt="" (error)="logoLoadFailed.set(true)" /> }
              @else { <span class="brand-mark">{{ currentStore.name.slice(0, 1) }}</span> }
              <span class="brand-copy"><strong>{{ currentStore.displayName || currentStore.name }}</strong><small>Powered by KhataDhari</small></span>
            </a>
            <form class="search-wrap" (submit)="search($event)">
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21 21-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z"/></svg>
              <input aria-label="Search products" placeholder="Search for products..." [value]="searchText" (input)="updateSearch($event)" />
              @if (searchText) { <button type="button" class="clear-search" (click)="clearSearch()" aria-label="Clear search">&times;</button> }
            </form>
            <button class="desktop-cart" type="button" (click)="goCart()" aria-label="Open cart">
              <span class="cart-icon-wrap"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 4h2l2.1 10.2a2 2 0 0 1 2 1.6h7.8a2 2 0 0 0 2-1.6L20 8H6M10 20h.01M17 20h.01"/></svg>@if (cart.itemCount()) { <b class="cart-badge" [attr.aria-label]="cart.itemCount() + ' items in cart'">{{ cart.itemCount() > 99 ? '99+' : cart.itemCount() }}</b> }</span>
              <span class="cart-copy"><strong>Cart</strong>@if (cart.itemCount()) { <small>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'item' : 'items' }} &middot; {{ cart.total() | currency:'INR':'symbol':'1.0-0' }}</small> } @else { <small>Empty</small> }</span>
            </button>
            <a class="header-icon-action wishlist-header" [routerLink]="['/',currentStore.storeKey,'account','wishlist']" aria-label="Open wishlist"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 8.5c0 4-8 10-8 10s-8-6-8-10a4.3 4.3 0 0 1 8-2.4A4.3 4.3 0 0 1 20 8.5z"/></svg>@if(wishlist.ids().size){<b class="wishlist-badge">{{wishlist.ids().size > 99 ? '99+' : wishlist.ids().size}}</b>}</a>
            @if (session.active()) {
              <div class="account-menu-wrap">
                <button class="header-avatar-button" type="button" (click)="toggleAccountMenu()" [attr.aria-expanded]="accountMenuOpen()" aria-haspopup="menu" aria-label="Open account menu">@if(session.customer()?.profileImageUrl; as avatar){<img class="header-avatar" [src]="avatar" alt="">} @else {<span class="header-avatar initials">{{initials(session.customer()?.name||'')}}</span>}</button>
                @if(accountMenuOpen()) { <div class="account-dropdown" role="menu">
                  <div class="account-dropdown-summary">@if(session.customer()?.profileImageUrl; as avatar){<img class="header-avatar" [src]="avatar" alt="">} @else {<span class="header-avatar initials">{{initials(session.customer()?.name||'')}}</span>}<span><strong>{{session.customer()?.name}}</strong><small>{{session.customer()?.mobile}}</small><em>&#10003; Verified</em></span></div>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','profile']" (click)="closeAccountMenu()">My Profile <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','wishlist']" (click)="closeAccountMenu()">My Wishlist <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','orders']" (click)="closeAccountMenu()">My Orders <span>&rsaquo;</span></a>@if(rewards.enabled()){<a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','rewards']" (click)="closeAccountMenu()">My Rewards <span>&rsaquo;</span></a>}
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','addresses']" (click)="closeAccountMenu()">Addresses <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','support']" (click)="closeAccountMenu()">Support &amp; FAQs <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'feedback']" (click)="closeAccountMenu()">Help &amp; Feedback <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','reviews']" (click)="closeAccountMenu()">Ratings &amp; Reviews <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','terms']" (click)="closeAccountMenu()">Terms &amp; Conditions <span>&rsaquo;</span></a>
                  <a role="menuitem" [routerLink]="['/',currentStore.storeKey,'account','privacy']" (click)="closeAccountMenu()">Privacy Policy <span>&rsaquo;</span></a>
                  <button role="menuitem" type="button" (click)="signOut(currentStore.storeKey)">Logout <span>&rsaquo;</span></button>
                </div> }
              </div>
            } @else { <a class="compact-sign-in" [routerLink]="['/',currentStore.storeKey,'auth']">Sign In</a> }
          </div>
        </header>
         @if (notify.message(); as message) { <div class="store-toast" role="status" aria-live="polite">{{message}}</div> }
          @if (pwa.canInstall()) { <aside class="pwa-install-card" aria-label="Install store app"><span class="pwa-install-icon">@if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="currentStore.logoUrl" alt="" (error)="logoLoadFailed.set(true)" /> } @else { {{ currentStore.name.slice(0, 1) }} }</span><span class="pwa-install-copy"><strong>Install {{ currentStore.name }} App</strong><small>Shop faster from your home screen</small></span><button type="button" (click)="installApp()">{{pwa.installing()?'Installing...':'Install'}}</button><button class="pwa-dismiss" type="button" aria-label="Dismiss install prompt" (click)="pwa.dismissInstall()">&times;</button></aside> }
          @if (pwa.iosInstallHint()) { <aside class="pwa-install-card pwa-ios-hint" aria-label="Add store to home screen"><span class="pwa-install-icon">@if (currentStore.logoUrl && !logoLoadFailed()) { <img [src]="currentStore.logoUrl" alt="" (error)="logoLoadFailed.set(true)" /> } @else { {{ currentStore.name.slice(0, 1) }} }</span><span class="pwa-install-copy"><strong>Add {{ currentStore.name }} to your Home Screen</strong><small>Share &#8594; Add to Home Screen</small></span><button class="pwa-dismiss" type="button" aria-label="Dismiss home screen instructions" (click)="pwa.dismissInstall()">&times;</button></aside> }
         @if(analyticsPrompt()){<aside class="analytics-consent" aria-label="Store analytics privacy choice"><div><strong>Help improve this store?</strong><p>With your permission, anonymous browsing and shopping activity is used for aggregated store and KhataDhari reports. No name, mobile number, address, GPS location or ad fingerprint is collected for this report. You can withdraw this choice from Privacy choices.</p>@if(analyticsConsentError()){<small role="alert">{{analyticsConsentError()}}</small>}</div><button type="button" (click)="chooseAnalytics(currentStore.storeKey,true)" [disabled]="analyticsConsentBusy()">{{analyticsConsentBusy()?'Saving…':'Allow analytics'}}</button><button class="consent-secondary" type="button" (click)="chooseAnalytics(currentStore.storeKey,false)" [disabled]="analyticsConsentBusy()">Only necessary</button></aside>}
         <main class="store-content"><router-outlet /></main><div class="store-privacy-actions"><a [routerLink]="['/',currentStore.storeKey,'privacy']">Privacy policy</a><button type="button" (click)="openAnalyticsPreferences()">Privacy choices</button></div>@if (shoppingRoute() && currentStore.orderContactNumber) {
           <nav class="order-contact-actions" aria-label="Contact this store to place an order">
             <a class="call-order" [href]="'tel:' + currentStore.orderContactNumber" aria-label="Call this store to place an order"><span aria-hidden="true">&#9742;</span><span>Call to Order</span></a>
             <a class="whatsapp-order" [href]="whatsappOrderUrl(currentStore)" target="_blank" rel="noopener noreferrer" aria-label="Message this store on WhatsApp to place an order"><span aria-hidden="true">&#9673;</span><span>Order on WhatsApp</span></a>
           </nav>
         }@if (shoppingRoute()) { @if (shoppingQuote(); as deliveryQuote) { <shop-free-delivery-progress [quote]="deliveryQuote" mode="toast" /> } }

      </div>
    } @else {
      <main class="state-page"><span class="state-icon">?</span><h1>Store not found</h1><p>This shopping link may be incorrect or the store is unavailable.</p><a routerLink="/">Back to shops</a></main>
    }
  `,
  styles: [`
    :host{display:block;min-height:100vh}.store-frame{min-height:100vh;--tenant-primary:var(--store-primary)}.store-header{z-index:100;overflow:visible}.header-inner{grid-template-columns:minmax(180px,250px) minmax(220px,1fr) auto auto auto}.header-icon-action,.header-avatar-button{display:grid;position:relative;width:42px;height:42px;place-items:center;border:1px solid var(--store-border);border-radius:12px;color:var(--store-primary-dark);background:#fff;cursor:pointer;text-decoration:none}.header-icon-action svg{width:20px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.wishlist-header{isolation:isolate}.wishlist-badge{position:absolute;top:-6px;right:-6px;display:grid;min-width:17px;height:17px;place-items:center;border:2px solid #fff;border-radius:99px;padding:0 3px;color:#fff;background:var(--store-primary);font-size:8px;line-height:1}.header-icon-action:hover,.header-avatar-button:hover{border-color:var(--store-primary);background:var(--store-primary-soft)}.header-avatar-button{border-radius:50%;padding:3px}.header-avatar{display:grid;width:34px;height:34px;place-items:center;border-radius:50%;object-fit:cover}.header-avatar.initials{color:#fff;background:var(--store-primary);font-size:12px;font-weight:800}.compact-sign-in{display:inline-flex;align-items:center;justify-content:center;min-height:38px;border-radius:10px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;text-decoration:none}.account-dropdown{z-index:110}.account-dropdown-summary{display:flex;align-items:center;gap:9px;margin:2px 2px 6px;padding:8px;border-bottom:1px solid var(--store-border)}.account-dropdown-summary>span{display:grid;min-width:0;gap:2px}.account-dropdown-summary strong{overflow:hidden;font-size:12px;text-overflow:ellipsis;white-space:nowrap}.account-dropdown-summary small{color:var(--store-muted);font-size:9px}.account-dropdown-summary em{color:var(--store-primary-dark);font-size:9px;font-style:normal;font-weight:800}
    .account-menu-wrap{position:relative}.account-trigger{font:750 10px Manrope,sans-serif}.header-avatar{display:grid;width:24px;height:24px;place-items:center;border-radius:50%;object-fit:cover}.header-avatar.initials{color:#fff;background:var(--store-primary);font-size:10px}.account-dropdown{position:absolute;z-index:30;top:calc(100% + 7px);right:0;display:grid;width:230px;border:1px solid var(--store-border);border-radius:13px;padding:6px;background:#fff;box-shadow:0 14px 34px rgba(31,48,39,.16)}.account-dropdown a,.account-dropdown button{display:flex;align-items:center;justify-content:space-between;min-height:42px;border:0;border-radius:8px;padding:0 11px;color:var(--store-text);background:#fff;font:750 11px Manrope,sans-serif;text-align:left;text-decoration:none;cursor:pointer}.account-dropdown a:hover,.account-dropdown a:focus-visible,.account-dropdown button:hover,.account-dropdown button:focus-visible{outline:0;background:var(--store-primary-soft);color:var(--store-primary-dark)}.account-dropdown span{color:var(--store-muted);font-size:17px}
    .store-header{position:sticky;z-index:20;top:0;border-bottom:1px solid var(--store-border);background:rgba(255,255,255,.96);backdrop-filter:blur(14px)}.customer-nav{display:flex;justify-content:flex-end;gap:7px;width:min(var(--store-content-width),100%);margin:-6px auto 8px;padding:0 24px;overflow-x:auto;scrollbar-width:none}.customer-nav::-webkit-scrollbar{display:none}.account-action{display:inline-flex;flex:0 0 auto;align-items:center;justify-content:center;gap:6px;min-height:32px;border:1px solid var(--store-border);border-radius:9px;padding:5px 10px;color:var(--store-primary-dark);background:#fff;font:750 10px Manrope,sans-serif;text-decoration:none;white-space:nowrap;cursor:pointer;transition:background .16s,border-color .16s,box-shadow .16s}.account-action:hover{border-color:var(--store-primary);background:var(--store-primary-soft)}.account-action:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.account-action svg{width:15px;height:15px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.account-action.primary-action{border-color:var(--store-primary);color:#fff;background:var(--store-primary)}.account-action.primary-action:hover{background:var(--store-primary-dark)}.account-action.sign-out-action{color:var(--store-muted)}
    .header-inner{display:grid;grid-template-columns:250px minmax(280px,680px);align-items:center;gap:22px;width:min(var(--store-content-width),100%);min-height:76px;margin:auto;padding:12px 24px}
    .brand{display:flex;align-items:center;gap:10px;min-width:0;text-decoration:none}.brand-mark,.brand img{display:grid;flex:0 0 auto;width:42px;height:42px;place-items:center;border-radius:12px}.brand-mark{color:#fff;background:var(--store-primary);font:800 19px Manrope,sans-serif}.brand img{object-fit:cover}
    .brand-copy{display:grid;min-width:0;line-height:1.15}.brand-copy strong{overflow:hidden;color:var(--store-text);font:800 17px Manrope,sans-serif;text-overflow:ellipsis;white-space:nowrap}.brand-copy small{margin-top:4px;color:var(--store-muted);font-size:10px}
    .search-wrap{display:flex;align-items:center;height:48px;border:1px solid var(--store-border);border-radius:14px;padding:0 12px;background:var(--store-background);transition:.18s}.search-wrap:focus-within{border-color:var(--store-primary);background:#fff;box-shadow:0 0 0 3px var(--store-primary-soft)}.search-wrap svg{width:20px;fill:none;stroke:var(--store-muted);stroke-linecap:round;stroke-width:1.8}.search-wrap input{flex:1;min-width:0;height:100%;border:0;padding:0 10px;color:var(--store-text);background:transparent;outline:0;font-size:14px}.search-wrap input::placeholder{color:#87918a}.clear-search{display:grid;width:30px;height:30px;place-items:center;border:0;border-radius:50%;color:var(--store-muted);background:transparent;font-size:22px;cursor:pointer}.clear-search:hover{background:var(--store-surface-muted)}
    .desktop-cart{display:flex;align-items:center;justify-content:center;gap:10px;min-height:48px;border:1px solid var(--store-primary);border-radius:14px;padding:7px 12px;color:var(--store-primary-dark);background:var(--store-primary-soft);cursor:pointer}.cart-icon-wrap{position:relative;display:grid;place-items:center}.desktop-cart svg{width:23px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.cart-copy{display:grid;text-align:left}.desktop-cart strong{font-size:13px}.cart-badge{display:none;position:absolute;top:-8px;right:-11px;min-width:18px;height:18px;align-items:center;justify-content:center;border:2px solid #fff;border-radius:99px;padding:0 4px;color:#fff;background:var(--store-primary);font-size:9px;line-height:1}.desktop-cart small{margin-top:1px;color:var(--store-muted);font-size:10px;white-space:nowrap}
    .store-content{width:min(var(--store-content-width),100%);min-height:calc(100vh - 77px);margin:auto;padding:24px 24px 80px}    .pwa-install-card{display:grid;grid-template-columns:40px minmax(0,1fr) auto auto;align-items:center;gap:10px;width:min(var(--store-content-width),calc(100% - 48px));margin:12px auto 0;border:1px solid var(--store-border);border-radius:14px;padding:9px 10px;background:#fff;box-shadow:0 5px 16px rgba(31,48,39,.08)}.pwa-install-icon{display:grid;width:40px;height:40px;place-items:center;border-radius:10px;color:#fff;background:var(--store-primary);font-weight:800}.pwa-install-icon img{width:100%;height:100%;border-radius:10px;object-fit:cover}.pwa-install-copy{display:grid;min-width:0;gap:2px}.pwa-install-copy strong{overflow:hidden;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.pwa-install-copy small{overflow:hidden;color:var(--store-muted);font-size:9px;text-overflow:ellipsis;white-space:nowrap}.pwa-install-card>button:not(.pwa-dismiss){min-height:32px;border:0;border-radius:9px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;cursor:pointer}.pwa-dismiss{display:grid;width:26px;height:26px;place-items:center;border:0;border-radius:50%;color:var(--store-muted);background:transparent;font-size:18px;cursor:pointer}.pwa-dismiss:hover{background:var(--store-surface-muted)}.pwa-ios-hint{margin-top:12px}
    .store-toast{position:fixed;z-index:50;right:18px;bottom:24px;border-radius:10px;padding:11px 16px;color:#fff;background:var(--store-primary-dark);box-shadow:0 8px 24px rgba(0,0,0,.18);font-size:12px;font-weight:700}.state-page{display:grid;min-height:100vh;align-content:center;justify-items:center;padding:28px;text-align:center}.state-icon{display:grid;width:58px;height:58px;place-items:center;border-radius:18px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 24px Manrope,sans-serif}.state-page h1{margin:16px 0 5px;font:800 24px Manrope,sans-serif}.state-page p{max-width:390px;margin:0;color:var(--store-muted);line-height:1.6}.state-page button,.state-page a{margin-top:18px;border:0;border-radius:12px;padding:12px 18px;color:#fff;background:var(--store-primary);font-weight:700;text-decoration:none;cursor:pointer}
    .shell-skeleton{width:min(var(--store-content-width),100%);margin:auto;padding:15px 24px}.skeleton-header{display:flex;align-items:center;gap:10px}.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{display:block;background:linear-gradient(90deg,#edf0eb 25%,#f8f9f7 50%,#edf0eb 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.skeleton-header i{width:42px;height:42px;border-radius:12px}.skeleton-header span{width:150px;height:17px;border-radius:6px}.skeleton-header b{width:120px;height:42px;margin-left:auto;border-radius:12px}.skeleton-search{height:48px;margin:18px 0;border-radius:14px}.skeleton-body{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-top:40px}.skeleton-body div{height:280px;border-radius:18px}@keyframes shimmer{to{background-position:-200% 0}}
    @media(max-width:520px){.pwa-install-card{width:calc(100% - 24px);grid-template-columns:34px minmax(0,1fr) auto auto;gap:7px;padding:8px}.pwa-install-icon{width:34px;height:34px}.pwa-install-card>button:not(.pwa-dismiss){padding:0 9px;font-size:9px}}
    @media(max-width:800px){.header-inner{grid-template-columns:minmax(0,1fr) auto;gap:10px;min-height:auto;padding:10px 16px 12px}.brand-mark,.brand img{width:38px;height:38px}.brand-copy strong{font-size:16px}.desktop-cart{width:44px;min-height:42px;padding:0}.cart-copy{display:none}.cart-badge{display:flex}.search-wrap{grid-column:1/-1;grid-row:2;height:46px}.customer-nav{justify-content:flex-start;padding:0 16px 9px}.store-content{padding:18px 16px 104px}.skeleton-body{grid-template-columns:repeat(2,1fr);gap:10px}.skeleton-body div{height:235px}}
    @media(max-width:360px){.header-inner,.store-content{padding-right:12px;padding-left:12px}.customer-nav{padding-right:12px;padding-left:12px}.brand-copy small{display:none}}
    @media(prefers-reduced-motion:reduce){.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{animation:none}}
  `, `@media(max-width:800px){.header-inner{grid-template-columns:minmax(0,1fr) auto auto auto}.header-icon-action,.header-avatar-button{width:38px;height:38px}.header-avatar{width:30px;height:30px}.search-wrap{grid-column:1/-1;grid-row:2}.account-dropdown{position:fixed;top:70px;right:12px;width:min(280px,calc(100vw - 24px))}}@media(max-width:360px){.header-inner{padding-right:12px;padding-left:12px}.header-icon-action,.header-avatar-button{width:36px;height:36px}}`,
  `.order-contact-actions{position:fixed;z-index:18;right:max(16px,env(safe-area-inset-right));bottom:max(16px,env(safe-area-inset-bottom));display:flex;gap:9px}.order-contact-actions a{display:inline-flex;align-items:center;justify-content:center;gap:8px;min-height:46px;border:1px solid transparent;border-radius:999px;padding:0 17px;color:#fff;font:750 12px Manrope,sans-serif;text-decoration:none;box-shadow:0 8px 24px rgba(20,48,35,.2);transition:transform .16s,box-shadow .16s}.order-contact-actions a:hover{transform:translateY(-2px);box-shadow:0 11px 27px rgba(20,48,35,.23)}.order-contact-actions a:focus-visible{outline:3px solid #8bc4a9;outline-offset:3px}.order-contact-actions .call-order{background:var(--store-primary-dark)}.order-contact-actions .whatsapp-order{color:#173b29;background:#e4f5e9;border-color:#b7dec4}.order-contact-actions span[aria-hidden=true]{font-size:18px;line-height:1}@media(max-width:800px){.order-contact-actions{right:12px;bottom:max(12px,env(safe-area-inset-bottom));gap:7px}.order-contact-actions a{min-height:44px;padding:0 13px;font-size:11px}.store-content{padding-bottom:120px}}@media(max-width:360px){.order-contact-actions{right:8px;left:8px}.order-contact-actions a{flex:1;gap:5px;padding:0 8px;font-size:10px}}@media(prefers-reduced-motion:reduce){.order-contact-actions a{transition:none}.order-contact-actions a:hover{transform:none}}`,
  `.analytics-consent{display:flex;align-items:center;gap:12px;width:min(var(--store-content-width),calc(100% - 32px));margin:12px auto 0;border:1px solid var(--store-border);border-radius:12px;padding:12px 14px;background:var(--store-surface);box-shadow:0 3px 14px rgba(20,40,30,.07)}.analytics-consent>div{flex:1;min-width:0}.analytics-consent strong{font-size:12px}.analytics-consent p{margin:4px 0 0;color:var(--store-muted);font-size:10px;line-height:1.45}.analytics-consent small{display:block;margin-top:5px;color:var(--store-danger)}.analytics-consent button,.store-privacy-actions button{min-height:36px;border:1px solid var(--store-primary);border-radius:9px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;cursor:pointer}.analytics-consent .consent-secondary{color:var(--store-primary-dark);background:var(--store-surface)}.analytics-consent button:disabled{opacity:.55;cursor:wait}.store-privacy-actions{display:flex;justify-content:center;gap:12px;padding:8px 16px 20px;color:var(--store-muted);font-size:10px}.store-privacy-actions a{align-self:center;color:inherit}.store-privacy-actions button{min-height:30px;padding:0 9px;color:var(--store-muted);border-color:var(--store-border);background:var(--store-surface)}@media(max-width:620px){.analytics-consent{align-items:stretch;flex-wrap:wrap;width:calc(100% - 24px);padding:11px}.analytics-consent>div{flex:1 0 100%}.analytics-consent button{flex:1;min-width:0;padding:0 7px}.store-privacy-actions{padding-bottom:24px}}`],
})
export class StoreShell implements OnInit {
  readonly routeKind = signal<'RETAILER'|'CITY'|'NOT_FOUND'>('RETAILER');
  readonly routeSlug = signal('');
  readonly store = signal<Store | null>(null);
  readonly accountMenuOpen = signal(false);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly shoppingRoute = signal(false);
  readonly shoppingQuote = signal<CartQuote | null>(null);
  readonly logoLoadFailed = signal(false);
  readonly analyticsPrompt = signal(false);
  readonly analyticsConsentBusy = signal(false);
  readonly analyticsConsentError = signal('');
  private quoteRequest = 0;
  private storeLoadKey = '';
  private storeLoad: Promise<Store | null> = Promise.resolve(null);
  searchText = '';
  initials(name:string):string{return name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join('').toUpperCase();}
  private readonly destroyRef = inject(DestroyRef);
  private engagementTimer?: ReturnType<typeof setInterval>;

  constructor(private readonly route: ActivatedRoute, private readonly router: Router, private readonly data: StorefrontDataService, private readonly publicRoutes: PublicMarketplaceApiService, readonly cart: CartService, readonly session: CustomerSessionService, readonly wishlist: WishlistService, readonly notify: StorefrontNotificationService, readonly pwa: PwaInstallService, readonly rewards: CustomerRewardsService, readonly activity: StorefrontActivityService) { effect(() => { const key = this.cart.storeKey(); const lines = this.cart.lines(); if (key && lines.length && this.shoppingRoute()) void this.refreshShoppingQuote(key, lines); else this.shoppingQuote.set(null); }); effect(() => { const key = this.cart.storeKey(); if (!key) return; const customerId = this.session.customerId(); this.cart.switchCustomer(customerId); void this.wishlist.switchCustomer(key, customerId); if(customerId) { void this.rewards.get(key).catch(()=>{}); void this.rewards.capturePending(key); } }); }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params: ParamMap) => { void this.openPublicRoute(params.get('storeKey') ?? ''); });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => this.searchText = params.get('q') ?? '');
    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(event => { if (event instanceof NavigationEnd) { this.updateShoppingRoute(event.urlAfterRedirects); this.closeAccountMenu(); const key=this.store()?.storeKey;if(key)this.activity.record(key,'PAGE_VIEW'); } });
    this.engagementTimer=setInterval(()=>{const key=this.store()?.storeKey;if(key&&document.visibilityState==='visible')this.activity.record(key,'FOREGROUND_ENGAGEMENT',undefined,undefined,undefined,undefined,30);},30000);
    this.destroyRef.onDestroy(()=>{if(this.engagementTimer)clearInterval(this.engagementTimer);});
    this.updateShoppingRoute(this.router.url);
  }
  updateSearch(event: Event): void { this.searchText = (event.target as HTMLInputElement).value; }
  clearSearch(): void { this.searchText = ''; this.navigateSearch(); }
  search(event: Event): void { event.preventDefault(); this.navigateSearch(); }
  goCart(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key, 'cart']); }
  toggleAccountMenu():void{this.accountMenuOpen.update(open=>!open);}
  closeAccountMenu():void{this.accountMenuOpen.set(false);}
  openAnalyticsPreferences():void{const key=this.store()?.storeKey;if(!key)return;this.analyticsConsentError.set('');this.activity.resetChoice(key);this.analyticsPrompt.set(true);}
  async chooseAnalytics(storeKey:string,allow:boolean):Promise<void>{this.analyticsConsentBusy.set(true);this.analyticsConsentError.set('');try{if(allow){const ok=await this.activity.grantConsent(storeKey);if(!ok){this.analyticsConsentError.set('Analytics preferences could not be saved. No activity was recorded.');return;}}else await this.activity.rejectOrWithdraw(storeKey);this.analyticsPrompt.set(false);if(allow)this.activity.record(storeKey,'STORE_VISIT');}finally{this.analyticsConsentBusy.set(false);}}
  signOut(storeKey:string):void{this.closeAccountMenu();this.activity.record(storeKey,'CUSTOMER_SIGN_OUT');this.session.signOut(storeKey);}
  installApp(): void { void this.pwa.install(); }
  whatsappOrderUrl(store: Store): string { const number = (store.orderContactNumber ?? '').replace(/[^0-9]/g, ''); const message = encodeURIComponent(`Hi, I'd like to place an order from ${store.displayName || store.name}.`); return `https://wa.me/${number}?text=${message}`; }
  @HostListener('document:keydown.escape') closeAccountMenuOnEscape():void{this.closeAccountMenu();}
  @HostListener('document:click',['$event']) closeAccountMenuOnOutsideClick(event:MouseEvent):void{const target=event.target as Element|null;if(target&&!target.closest('.account-menu-wrap'))this.closeAccountMenu();}
  retry(): void { const key = this.route.snapshot.paramMap.get('storeKey') ?? ''; void this.openPublicRoute(key); }
  private async openPublicRoute(slug:string):Promise<void>{
    this.loading.set(true);this.loadFailed.set(false);this.logoLoadFailed.set(false);this.routeSlug.set(slug);
    try{
      const resolution=await this.publicRoutes.resolve(slug);
      if(!resolution){this.routeKind.set('NOT_FOUND');this.store.set(null);this.storeLoadKey=slug;this.loading.set(false);return;}
      if(resolution.type==='CITY'){this.routeKind.set('CITY');this.store.set(null);this.storeLoadKey=slug;this.loading.set(false);return;}
      const storeKey=resolution.storeKey??slug;this.routeKind.set('RETAILER');this.storeLoadKey=storeKey;this.session.restore(storeKey);this.cart.useStore(storeKey);this.wishlist.initialize(storeKey);this.storeLoad=this.load(storeKey);
    }catch{this.store.set(null);this.loadFailed.set(true);this.loading.set(false);}
  }
  private updateShoppingRoute(url: string): void { const segments = url.split('?')[0].split('/').filter(Boolean); const hidden = ['cart','checkout','account','auth','wishlist','orders']; this.shoppingRoute.set(segments.length > 0 && !hidden.includes(segments[1] ?? '')); }
  private async refreshShoppingQuote(key: string, lines: readonly CartLine[]): Promise<void> { const request = ++this.quoteRequest; let pincode = ''; try { const raw=sessionStorage.getItem('khata-dhari-storefront-serviceability:'+key); const state=raw?JSON.parse(raw) as {pincode?:string;verified?:boolean}:null; if(state?.verified&&state.pincode) pincode=state.pincode; } catch {} try { const quote = await this.data.quote(key, lines, pincode); if (request === this.quoteRequest) this.shoppingQuote.set(quote); } catch { if (request === this.quoteRequest) this.shoppingQuote.set(null); } }  private navigateSearch(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key], { queryParams: { q: this.searchText.trim() || null } }); }
  waitForStore(storeKey: string): Promise<Store | null> {
    const loadedKey = this.storeLoadKey.trim().toLowerCase();
    const requestedKey = storeKey.trim().toLowerCase();
    return loadedKey === requestedKey ? this.storeLoad : Promise.resolve(null);
  }
  private async load(storeKey: string): Promise<Store | null> { try { const loaded = await this.data.getStore(storeKey); this.store.set(loaded); if (loaded) { this.pwa.configure(loaded); void this.activity.initializeConsent(storeKey).then(state=>{this.analyticsPrompt.set(state==='PROMPT');if(state==='READY')this.activity.record(storeKey,'STORE_VISIT');}); } return loaded; } catch { this.store.set(null); this.loadFailed.set(true); return null; } finally { this.loading.set(false); } }
}
