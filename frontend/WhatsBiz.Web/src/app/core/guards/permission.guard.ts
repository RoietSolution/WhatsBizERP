import { inject } from '@angular/core'; import { CanActivateFn, Router } from '@angular/router'; import { PermissionService } from '../services/permission.service';
export const permissionGuard: CanActivateFn = route => inject(PermissionService).has(route.data['permission'] as string) ? true : inject(Router).createUrlTree(['/403']);
