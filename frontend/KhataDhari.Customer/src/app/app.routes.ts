import { Routes } from '@angular/router';
import { CartPage } from './pages/cart.page';
import { OrdersPage } from './pages/orders.page';
import { OrderDetailsPage } from './pages/order-details.page';
import { ProductDetailsPage } from './pages/product-details.page';
import { StoreHomePage } from './pages/store-home.page';
import { StoreShell } from './store-shell';
import { WelcomePage } from './pages/welcome.page';
import { WishlistPage } from './pages/wishlist.page';
import { CustomerAuthPage } from './pages/customer-auth.page';
import { AccountPage } from './pages/account.page';
import { OfferDetailsPage } from './pages/offer-details.page';
import { customerAuthGuard } from './customer-auth.guard';
import { AddressesPage } from './pages/addresses.page';
import { StorefrontContentPage } from './pages/storefront-content.page';
import { AccountProfilePage } from './pages/account-profile.page';

export const routes: Routes = [
  { path: '', component: WelcomePage, pathMatch: 'full' },
  {
    path: ':storeKey',
    component: StoreShell,
    children: [
      { path: '', component: StoreHomePage, pathMatch: 'full' },
      { path: 'products/:productId', component: ProductDetailsPage },
      { path: 'offers/:offerId', component: OfferDetailsPage },
      { path: 'cart', component: CartPage, canActivate: [customerAuthGuard] },
      { path: 'checkout', component: CartPage, canActivate: [customerAuthGuard] },
      { path: 'orders', component: OrdersPage, canActivate: [customerAuthGuard] },
      { path: 'orders/:orderId', component: OrderDetailsPage, canActivate: [customerAuthGuard] },
      { path: 'wishlist', component: WishlistPage },
      { path: 'auth', component: CustomerAuthPage },
      { path: 'account', component: AccountPage, canActivate: [customerAuthGuard], children: [
        { path: '', redirectTo: 'profile', pathMatch: 'full' },
        { path: 'profile', component: AccountProfilePage },
        { path: 'wishlist', component: WishlistPage },
        { path: 'orders', component: OrdersPage },
        { path: 'addresses', component: AddressesPage },
        { path: 'support', component: StorefrontContentPage, data: { kind: 'support' } },
        { path: 'reviews', component: StorefrontContentPage, data: { kind: 'reviews' } },
        { path: 'terms', component: StorefrontContentPage, data: { kind: 'terms' } },
        { path: 'privacy', component: StorefrontContentPage, data: { kind: 'privacy' } },
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

