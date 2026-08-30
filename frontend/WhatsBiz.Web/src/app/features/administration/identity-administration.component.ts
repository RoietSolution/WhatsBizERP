import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AdminApiService, AdminRole, AdminUser } from './admin-api.service';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';

@Component({
  imports: [RouterLink, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSnackBarModule, PageContainerComponent],
  template: `
    <app-page-container wide>
      <header><div><h1>{{ mode === 'users' ? 'Employees' : 'Roles & Permissions' }}</h1>
        <p>{{ mode === 'users' ? 'Create retailer employees and give each person only the access they need.' : 'Review role permission assignments.' }}</p></div>
        <div class="header-actions">@if (mode === 'users') { <button mat-flat-button type="button" (click)="startCreate()">Add Employee</button> }<a mat-stroked-button routerLink="/admin">Back to Administration</a></div></header>
      @if (mode === 'users') {
        @if (showEditor()) {
          <section class="editor" aria-label="Employee editor">
            <div class="editor-title"><div><h2>{{ editingId() ? 'Edit employee' : 'Add employee' }}</h2><p>Permissions take effect at the employee's next sign-in.</p></div><button mat-button type="button" (click)="cancelEditor()">Cancel</button></div>
            <div class="fields">
              <mat-form-field appearance="outline"><mat-label>Username</mat-label><input matInput [(ngModel)]="draft.userName" [disabled]="!!editingId()" autocomplete="off" required></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Email</mat-label><input matInput type="email" [(ngModel)]="draft.email" autocomplete="off" required></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Phone (optional)</mat-label><input matInput [(ngModel)]="draft.phoneNumber" autocomplete="off"></mat-form-field>
              @if (!editingId()) { <mat-form-field appearance="outline"><mat-label>Temporary password</mat-label><input matInput type="password" [(ngModel)]="draft.temporaryPassword" autocomplete="new-password" required></mat-form-field> }
            </div>
            <div class="permission-heading"><div><h3>Permissions</h3><p>Select only what this employee needs.</p></div><button mat-stroked-button type="button" (click)="billingOnly()">Billing only</button></div>
            <div class="permissions">
              @for (permission of assignablePermissions(); track permission) {
                <mat-checkbox [checked]="hasPermission(permission)" (change)="setPermission(permission, $event.checked)">{{ permissionLabel(permission) }}</mat-checkbox>
              }
            </div>
            <mat-checkbox [(ngModel)]="draft.isActive">Employee can sign in</mat-checkbox>
            <div class="editor-actions"><button mat-flat-button type="button" [disabled]="saving()" (click)="save()">{{ saving() ? 'Saving…' : 'Save employee' }}</button></div>
          </section>
        }
        <table aria-label="Employees"><thead><tr><th>Employee</th><th>Contact</th><th>Access</th><th>Status</th><th></th></tr></thead><tbody>
          @for (user of users(); track user.userId) { <tr><td><strong>{{ user.userName }}</strong></td><td>{{ user.email }}<small>{{ user.phoneNumber || '' }}</small></td><td><span class="access">{{ accessSummary(user) }}</span></td><td>{{ user.isActive && !user.isDeleted ? 'Active' : 'Inactive' }}</td><td class="row-actions"><button mat-button type="button" (click)="startEdit(user)">Edit</button><button mat-button type="button" (click)="resetPassword(user)">Reset password</button><button mat-button type="button" class="danger" (click)="deactivate(user)">Deactivate</button></td></tr> }
          @empty { <tr><td colspan="5" class="empty">No employees found for this retailer.</td></tr> }
        </tbody></table>
      } @else {
        <table aria-label="Roles"><thead><tr><th>Role</th><th>Permissions</th></tr></thead><tbody>
          @for (role of roles(); track role.roleId) { <tr><td>{{ role.roleName }}</td><td>{{ role.permissions.join(', ') }}</td></tr> }
        </tbody></table>
      }
    </app-page-container>`,
  styles: [`header,.header-actions,.editor-title,.permission-heading,.editor-actions{display:flex;justify-content:space-between;align-items:center;gap:12px}header{margin-bottom:1rem}h1,h2,h3{margin:0}p{margin:.3rem 0;color:var(--wb-text-secondary)}.editor{padding:18px;margin-bottom:18px;border:1px solid var(--wb-border);border-radius:12px;background:var(--wb-surface)}.fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:16px}.permissions{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:6px 16px;padding:12px 0 16px}.editor-actions{justify-content:flex-end;margin-top:12px}table{width:100%;border-collapse:collapse;background:var(--wb-surface)}th,td{text-align:left;padding:12px;border-bottom:1px solid var(--wb-border);vertical-align:top}th{color:var(--wb-text-secondary)}td small{display:block;color:var(--wb-text-secondary);margin-top:3px}.access{display:block;max-width:360px}.row-actions{white-space:nowrap;text-align:right}.danger{color:var(--mat-sys-error)}.empty{text-align:center;color:var(--wb-text-secondary)}@media(max-width:800px){header,.editor-title,.permission-heading{align-items:flex-start;flex-direction:column}.header-actions{width:100%;justify-content:flex-start;flex-wrap:wrap}.fields,.permissions{grid-template-columns:1fr}table,thead,tbody,tr,th,td{display:block}thead{display:none}tr{padding:10px;border-bottom:1px solid var(--wb-border)}td{border:0;padding:5px}.row-actions{text-align:left;white-space:normal}}`],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IdentityAdministrationComponent {
  readonly mode: 'users' | 'roles';
  readonly users = signal<AdminUser[]>([]);
  readonly roles = signal<AdminRole[]>([]);
  readonly assignablePermissions = signal<string[]>([]);
  readonly showEditor = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);
  draft = this.emptyDraft();

  constructor(route: ActivatedRoute, private api: AdminApiService, private snack: MatSnackBar) {
    this.mode = route.snapshot.data['mode'] === 'roles' ? 'roles' : 'users';
    if (this.mode === 'users') {
      this.reload();
      api.employeePermissions().subscribe((x) => this.assignablePermissions.set(x));
    }
    else api.roles().subscribe((x) => this.roles.set(x));
  }

  startCreate() { this.editingId.set(null); this.draft = this.emptyDraft(); this.showEditor.set(true); }
  startEdit(user: AdminUser) {
    this.editingId.set(user.userId);
    this.draft = { userName: user.userName, email: user.email, phoneNumber: user.phoneNumber ?? '', temporaryPassword: '', isActive: user.isActive, permissions: [...user.permissions] };
    this.showEditor.set(true);
  }
  cancelEditor() { this.showEditor.set(false); this.editingId.set(null); }
  hasPermission(permission: string) { return this.draft.permissions.includes(permission); }
  setPermission(permission: string, checked: boolean) { this.draft.permissions = checked ? [...new Set([...this.draft.permissions, permission])] : this.draft.permissions.filter((x) => x !== permission); }
  billingOnly() { this.draft.permissions = this.assignablePermissions().filter((x) => x === 'pos.view' || x === 'pos.create'); }
  permissionLabel(permission: string) { return permission.split('.').map((x) => x.replace(/-/g, ' ')).join(' · '); }
  accessSummary(user: AdminUser) { return user.permissions.length ? user.permissions.map((x) => this.permissionLabel(x)).join(', ') : 'No access assigned'; }

  save() {
    if (!this.draft.userName.trim() || !this.draft.email.trim() || (!this.editingId() && !this.draft.temporaryPassword)) {
      this.notify('Username, email and temporary password are required.'); return;
    }
    this.saving.set(true);
    const id = this.editingId();
    const request = id
      ? this.api.updateEmployee(id, { email: this.draft.email, phoneNumber: this.draft.phoneNumber, isActive: this.draft.isActive, permissions: this.draft.permissions })
      : this.api.createEmployee(this.draft);
    request.subscribe({ next: () => { this.saving.set(false); this.cancelEditor(); this.reload(); this.notify('Employee saved.'); }, error: (error) => { this.saving.set(false); this.notify(this.errorMessage(error)); } });
  }

  resetPassword(user: AdminUser) {
    const temporaryPassword = window.prompt(`Enter a new temporary password for ${user.userName}:`);
    if (!temporaryPassword) return;
    this.api.resetEmployeePassword(user.userId, temporaryPassword).subscribe({ next: () => this.notify('Temporary password updated. Existing sessions were revoked.'), error: (error) => this.notify(this.errorMessage(error)) });
  }

  deactivate(user: AdminUser) {
    if (!window.confirm(`Deactivate ${user.userName}? They will no longer be able to sign in.`)) return;
    this.api.deactivateEmployee(user.userId).subscribe({ next: () => { this.reload(); this.notify('Employee deactivated.'); }, error: (error) => this.notify(this.errorMessage(error)) });
  }

  private reload() { this.api.users().subscribe({ next: (x) => this.users.set(x), error: (error) => this.notify(this.errorMessage(error)) }); }
  private emptyDraft() { return { userName: '', email: '', phoneNumber: '', temporaryPassword: '', isActive: true, permissions: [] as string[] }; }
  private notify(message: string) { this.snack.open(message, 'Close', { duration: 5000 }); }
  private errorMessage(error: any) { return error?.error?.detail || error?.error?.title || 'The employee operation could not be completed.'; }
}
