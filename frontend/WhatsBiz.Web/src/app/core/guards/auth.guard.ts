import { inject } from '@angular/core'; import { CanActivateFn, Router } from '@angular/router'; import { AuthenticationService } from '../services/authentication.service';
export const authGuard: CanActivateFn = () => inject(AuthenticationService).isAuthenticated() ? true : inject(Router).createUrlTree(['/login']);
