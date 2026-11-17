import { Routes } from '@angular/router';
import { StoreShell } from './store-shell';
import { customerAuthGuard } from './customer-auth.guard';
import { customStoreDomainMatcher } from './custom-store-domain';

const storeChildren: Routes = [
      { path: '', loadComponent: () => import('./pages/store-home.page').then(m => m.StoreHomePage), pathMatch: 'full', data: { freeDeliveryProgress: true } },
      { path: 'categories', loadComponent: () => import('./pages/store-home.page').then(m => m.StoreHomePage), data: { freeDeliveryProgress: true } },
      { path: 'products', loadComponent: () => import('./pages/store-home.page').then(m => m.StoreHomePage), data: { freeDeliveryProgress: true } },
      { path: 'stores', loadComponent: () => import('./pages/store-home.page').then(m => m.StoreHomePage), data: { freeDeliveryProgress: true } },
      { path: 'search', loadComponent: () => import('./pages/store-home.page').then(m => m.StoreHomePage), data: { freeDeliveryProgress: true } },
      { path: 'products/:productId', loadComponent: () => import('./pages/product-details.page').then(m => m.ProductDetailsPage) },
      { path: 'offers/:offerId', loadComponent: () => import('./pages/offer-details.page').then(m => m.OfferDetailsPage) },
      { path: 'cart', loadComponent: () => import('./pages/cart.page').then(m => m.CartPage), canActivate: [customerAuthGuard], data: { freeDeliveryProgress: true } },
      { path: 'checkout', loadComponent: () => import('./pages/cart.page').then(m => m.CartPage), canActivate: [customerAuthGuard] },
      { path: 'orders', loadComponent: () => import('./pages/orders-phase3.page').then(m => m.OrdersPhase3Page), canActivate: [customerAuthGuard] },
      { path: 'orders/:orderId', loadComponent: () => import('./pages/order-details-phase3.page').then(m => m.OrderDetailsPhase3Page), canActivate: [customerAuthGuard] },
      { path: 'wishlist', loadComponent: () => import('./pages/wishlist-phase4.page').then(m => m.WishlistPhase4Page) },
      { path: 'auth', loadComponent: () => import('./pages/customer-auth.page').then(m => m.CustomerAuthPage) },
      { path: 'payment-return', loadComponent: () => import('./pages/payment-return.page').then(m => m.PaymentReturnPage) },
      { path: 'feedback', loadComponent: () => import('./pages/feedback.page').then(m => m.FeedbackPage) },
      { path: 'account', loadComponent: () => import('./pages/account.page').then(m => m.AccountPage), canActivate: [customerAuthGuard], children: [
        { path: '', redirectTo: 'profile', pathMatch: 'full' },
        { path: 'profile', loadComponent: () => import('./pages/account-profile.page').then(m => m.AccountProfilePage) },
        { path: 'wishlist', loadComponent: () => import('./pages/wishlist-phase4.page').then(m => m.WishlistPhase4Page) },
        { path: 'orders', loadComponent: () => import('./pages/orders-phase3.page').then(m => m.OrdersPhase3Page) },
        { path: 'rewards', loadComponent: () => import('./pages/rewards.page').then(m => m.RewardsPage) },
        { path: 'addresses', loadComponent: () => import('./pages/addresses.page').then(m => m.AddressesPage) },
        { path: 'support', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'support' } },
        { path: 'reviews', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'reviews' } },
        { path: 'terms', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'terms' } },
        { path: 'terms-and-conditions', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'terms' } },
        { path: 'privacy', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'privacy' } },
        { path: 'cancellation-return-refund', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'cancellation' } },
        { path: 'feedback', loadComponent: () => import('./pages/feedback.page').then(m => m.FeedbackPage) },
      ] },
      { path: 'addresses', loadComponent: () => import('./pages/addresses.page').then(m => m.AddressesPage), canActivate: [customerAuthGuard] },
      { path: 'support', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), canActivate: [customerAuthGuard], data: { kind: 'support' } },
      { path: 'terms', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'terms' } },
      { path: 'terms-and-conditions', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'terms' } },
      { path: 'privacy', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'privacy' } },
      { path: 'cancellation-return-refund', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), data: { kind: 'cancellation' } },
      { path: 'reviews', loadComponent: () => import('./pages/storefront-content.page').then(m => m.StorefrontContentPage), canActivate: [customerAuthGuard], data: { kind: 'reviews' } },
];

export const routes: Routes = [
  { matcher: customStoreDomainMatcher, component: StoreShell, children: storeChildren },
  { path: '', loadComponent: () => import('./pages/store-directory.page').then(m => m.StoreDirectoryPage), pathMatch: 'full' },
  { path: 'ref/:code', loadComponent: () => import('./pages/customer-referral-landing.page').then(m => m.CustomerReferralLandingPage) },
  { path: ':storeKey', component: StoreShell, children: storeChildren },
  { path: '**', redirectTo: '' },
];

