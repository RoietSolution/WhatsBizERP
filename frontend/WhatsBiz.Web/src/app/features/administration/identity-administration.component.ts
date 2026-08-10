import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { AdminApiService, AdminRole, AdminUser } from './admin-api.service';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';

@Component({
  imports: [RouterLink, MatButtonModule, PageContainerComponent],
  template: `
    <app-page-container wide>
      <header><div><h1>{{ mode === 'users' ? 'Users' : 'Roles & Permissions' }}</h1>
        <p>{{ mode === 'users' ? 'Review user access and account status.' : 'Review role permission assignments.' }}</p></div>
        <a mat-stroked-button routerLink="/admin">Back to Administration</a></header>
      @if (mode === 'users') {
        <table aria-label="Users"><thead><tr><th>User</th><th>Email</th><th>Status</th></tr></thead><tbody>
          @for (user of users(); track user.userId) { <tr><td>{{ user.userName }}</td><td>{{ user.email }}</td><td>{{ user.isActive && !user.isDeleted ? 'Active' : 'Inactive' }}</td></tr> }
        </tbody></table>
      } @else {
        <table aria-label="Roles"><thead><tr><th>Role</th><th>Permissions</th></tr></thead><tbody>
          @for (role of roles(); track role.roleId) { <tr><td>{{ role.roleName }}</td><td>{{ role.permissions.join(', ') }}</td></tr> }
        </tbody></table>
      }
    </app-page-container>`,
  styles: [`header{display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem}h1{margin:0}table{width:100%;border-collapse:collapse;background:var(--wb-surface)}th,td{text-align:left;padding:12px;border-bottom:1px solid var(--wb-border)}th{color:var(--wb-text-secondary)}`],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IdentityAdministrationComponent {
  readonly mode: 'users' | 'roles';
  readonly users = signal<AdminUser[]>([]);
  readonly roles = signal<AdminRole[]>([]);
  constructor(route: ActivatedRoute, api: AdminApiService) {
    this.mode = route.snapshot.data['mode'] === 'roles' ? 'roles' : 'users';
    if (this.mode === 'users') api.users().subscribe((x) => this.users.set(x));
    else api.roles().subscribe((x) => this.roles.set(x));
  }
}
