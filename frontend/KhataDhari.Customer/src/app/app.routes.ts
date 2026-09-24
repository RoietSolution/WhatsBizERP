import { Routes } from '@angular/router';
import { CartPage } from './pages/cart.page';
import { OrdersPage } from './pages/orders.page';
import { ProductDetailsPage } from './pages/product-details.page';
import { StoreHomePage } from './pages/store-home.page';
import { StoreShell } from './store-shell';
import { WelcomePage } from './pages/welcome.page';

export const routes: Routes = [
  { path: '', component: WelcomePage, pathMatch: 'full' },
  {
    path: ':storeKey',
    component: StoreShell,
    children: [
      { path: '', component: StoreHomePage, pathMatch: 'full' },
      { path: 'products/:productId', component: ProductDetailsPage },
      { path: 'cart', component: CartPage },
      { path: 'orders', component: OrdersPage },
    ],
  },
  { path: '**', redirectTo: '' },
];
