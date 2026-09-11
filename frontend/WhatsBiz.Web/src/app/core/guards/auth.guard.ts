import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthenticationService } from '../services/authentication.service';
import { CurrentUserService } from '../services/current-user.service';
export const authGuard: CanActivateFn = (_route, state) => {
  const authentication = inject(AuthenticationService);
  const router = inject(Router);
  if (!authentication.isAuthenticated()) return router.createUrlTree(['/login']);
  if (inject(CurrentUserService).user()?.mustChangePassword && state.url !== '/change-password')
    return router.createUrlTree(['/change-password']);
  return true;
};
