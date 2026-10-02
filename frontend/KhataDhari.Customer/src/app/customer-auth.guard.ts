import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CustomerSessionService } from './customer-session.service';

export const customerAuthGuard: CanActivateFn = (route, state) => {
  const session = inject(CustomerSessionService);
  const router = inject(Router);
  const storeKey = route.paramMap.get('storeKey') ?? route.parent?.paramMap.get('storeKey') ?? '';
  session.restore(storeKey);
  if (session.isAuthenticated(storeKey)) return true;
  return router.createUrlTree(['/', storeKey, 'auth'], { queryParams: { returnUrl: state.url } });
};
