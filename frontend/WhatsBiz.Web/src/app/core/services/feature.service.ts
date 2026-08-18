import { Injectable } from '@angular/core';
import { CurrentUserService } from './current-user.service';
@Injectable({ providedIn: 'root' })
export class FeatureService {
  constructor(private readonly currentUser: CurrentUserService) {}
  hasFeature(featureKey: string): boolean { return this.currentUser.user()?.features?.[featureKey] === true; }
}
