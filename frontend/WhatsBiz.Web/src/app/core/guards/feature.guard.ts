import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { FeatureService } from '../services/feature.service';
export const featureGuard: CanActivateFn = (route) => inject(FeatureService).hasFeature(route.data['feature'] as string) ? true : inject(Router).createUrlTree(['/403']);
