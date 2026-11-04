import { Routes } from '@angular/router';
import { CartPage } from './pages/cart.page';
import { OrdersPage } from './pages/orders.page';
import { OrdersPhase3Page } from './pages/orders-phase3.page';
import { OrderDetailsPage } from './pages/order-details.page';
import { OrderDetailsPhase3Page } from './pages/order-details-phase3.page';
import { ProductDetailsPage } from './pages/product-details.page';
import { StoreHomePage } from './pages/store-home.page';
import { StoreShell } from './store-shell';
import { WelcomePage } from './pages/welcome.page';
import { StoreDirectoryPage } from './pages/store-directory.page';
import { WishlistPage } from './pages/wishlist.page';
import { WishlistPhase4Page } from './pages/wishlist-phase4.page';
import { CustomerAuthPage } from './pages/customer-auth.page';
import { AccountPage } from './pages/account.page';
import { OfferDetailsPage } from './pages/offer-details.page';
import { customerAuthGuard } from './customer-auth.guard';
import { AddressesPage } from './pages/addresses.page';
import { StorefrontContentPage } from './pages/storefront-content.page';
import { AccountProfilePage } from './pages/account-profile.page';
import { RewardsPage } from './pages/rewards.page';
import { CustomerReferralLandingPage } from './pages/customer-referral-landing.page';
import { FeedbackPage } from './pages/feedback.page';
import { PaymentReturnPage } from './pages/payment-return.page';

export const routes: Routes = [
  { path: '', component: StoreDirectoryPage, pathMatch: 'full' },
  { path: 'ref/:code', component: CustomerReferralLandingPage },
  {
    path: ':storeKey',
    component: StoreShell,
    children: [
      { path: '', component: StoreHomePage, pathMatch: 'full' },
      { path: 'categories', component: StoreHomePage },
      { path: 'products', component: StoreHomePage },
      { path: 'stores', component: StoreHomePage },
      { path: 'search', component: StoreHomePage },
      { path: 'products/:productId', component: ProductDetailsPage },
      { path: 'offers/:offerId', component: OfferDetailsPage },
      { path: 'cart', component: CartPage, canActivate: [customerAuthGuard] },
      { path: 'checkout', component: CartPage, canActivate: [customerAuthGuard] },
      { path: 'orders', component: OrdersPhase3Page, canActivate: [customerAuthGuard] },
      { path: 'orders/:orderId', component: OrderDetailsPhase3Page, canActivate: [customerAuthGuard] },
      { path: 'wishlist', component: WishlistPhase4Page },
      { path: 'auth', component: CustomerAuthPage },
      { path: 'payment-return', component: PaymentReturnPage },
      { path: 'feedback', component: FeedbackPage },
      { path: 'account', component: AccountPage, canActivate: [customerAuthGuard], children: [
        { path: '', redirectTo: 'profile', pathMatch: 'full' },
        { path: 'profile', component: AccountProfilePage },
        { path: 'wishlist', component: WishlistPhase4Page },
        { path: 'orders', component: OrdersPhase3Page },
        { path: 'rewards', component: RewardsPage },
        { path: 'addresses', component: AddressesPage },
        { path: 'support', component: StorefrontContentPage, data: { kind: 'support' } },
        { path: 'reviews', component: StorefrontContentPage, data: { kind: 'reviews' } },
        { path: 'terms', component: StorefrontContentPage, data: { kind: 'terms' } },
        { path: 'privacy', component: StorefrontContentPage, data: { kind: 'privacy' } },
        { path: 'feedback', component: FeedbackPage },
      ] },
      { path: 'addresses', component: AddressesPage, canActivate: [customerAuthGuard] },
      { path: 'support', component: StorefrontContentPage, canActivate: [customerAuthGuard], data: { kind: 'support' } },
      { path: 'terms', component: StorefrontContentPage, data: { kind: 'terms' } },
      { path: 'privacy', component: StorefrontContentPage, data: { kind: 'privacy' } },
      { path: 'reviews', component: StorefrontContentPage, canActivate: [customerAuthGuard], data: { kind: 'reviews' } },
    ],
  },
  { path: '**', redirectTo: '' },
];

