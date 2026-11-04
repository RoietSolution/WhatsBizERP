import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { LandingRouteService } from '../services/landing-route.service';
import { WarehouseUiVisibilityService } from '../../shared/services/warehouse-ui-visibility.service';

export const warehouseUiGuard: CanActivateFn = () => {
  const visibility = inject(WarehouseUiVisibilityService);
  return visibility.showManagement || inject(Router).createUrlTree([inject(LandingRouteService).resolve()]);
};
