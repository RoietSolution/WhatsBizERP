import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { FeatureService } from '../services/feature.service';
export const featureGuard: CanActivateFn = (route, state) => {
  const features = inject(FeatureService), router = inject(Router);
  const required = (route.data['feature'] as string | undefined) ?? features.requiredFeature(state.url);
  if (!required) return true;
  return features.load().pipe(map(() => features.hasFeature(required) ? true : router.createUrlTree(['/403'])));
};
