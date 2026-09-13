import { Injectable } from '@angular/core';
import { CurrentUser } from './jwt-storage.service';
import { CurrentUserService } from './current-user.service';
import { navigation } from '../../shared/layout/sidebar/sidebar.component';
import { NavigationItem } from '../../shared/models/navigation.model';
import { FeatureService } from './feature.service';

@Injectable({ providedIn: 'root' })
export class LandingRouteService {
  constructor(private readonly currentUser: CurrentUserService, private readonly features: FeatureService) {}

  resolve(user: CurrentUser | null = this.currentUser.user()): string {
    if (!user) return '/login';
    if (user.mustChangePassword) return '/change-password';
    if (user.roles.includes('ApplicationOwner')) return '/application-owner';
    for (const item of navigation) {
      const route = this.firstAllowed(item, user);
      if (route) return route;
    }
    return '/profile';
  }

  private firstAllowed(item: NavigationItem, user: CurrentUser): string | null {
    if (item.role && !user.roles.includes(item.role)) return null;
    if (item.permission && !user.permissions.includes(item.permission)) return null;
    if (item.feature && !this.features.hasFeature(item.feature)) return null;
    if (item.route) return item.route;
    for (const child of item.children ?? []) {
      const route = this.firstAllowed(child, user);
      if (route) return route;
    }
    return null;
  }
}
