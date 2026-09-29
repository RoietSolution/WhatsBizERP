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

export const routes: Routes = [
  { path: '', component: WelcomePage, pathMatch: 'full' },
  {
    path: ':storeKey',
    component: StoreShell,
    children: [
      { path: '', component: StoreHomePage, pathMatch: 'full' },
      { path: 'products/:productId', component: ProductDetailsPage },
      { path: 'offers/:offerId', component: OfferDetailsPage },
      { path: 'cart', component: CartPage },
      { path: 'orders', component: OrdersPage },
      { path: 'orders/:orderId', component: OrderDetailsPage },
      { path: 'wishlist', component: WishlistPage },
      { path: 'auth', component: CustomerAuthPage },
      { path: 'account', component: AccountPage },
    ],
  },
  { path: '**', redirectTo: '' },
];
