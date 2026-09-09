import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from '../services/current-user.service';

export const roleGuard: CanActivateFn = (route) => {
  const requiredRole = route.data['role'] as string;
  const roles = inject(CurrentUserService).user()?.roles ?? [];
  return roles.includes(requiredRole) ? true : inject(Router).createUrlTree(['/403']);
};
