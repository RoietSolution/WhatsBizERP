import { Injectable } from '@angular/core';
import { CurrentUserService } from './current-user.service';
@Injectable({ providedIn: 'root' })
export class PermissionService {
  constructor(private readonly currentUser: CurrentUserService) {}
  has(permission: string): boolean {
    return this.currentUser.user()?.permissions.includes(permission) ?? false;
  }
}
