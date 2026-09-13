import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { LandingRouteService } from '../services/landing-route.service';

export const landingGuard: CanActivateFn = () =>
  inject(Router).createUrlTree([inject(LandingRouteService).resolve()]);
