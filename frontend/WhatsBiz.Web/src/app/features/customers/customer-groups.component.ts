import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CustomerGroupApiService } from './customer-group-api.service';
import { CustomerGroup } from './customer-group.models';
@Component({ selector: 'app-customer-groups', imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, PageContainerComponent, PageHeaderComponent], template: `<app-page-container><app-page-header title="Customer Groups" description="Create groups such as VIP, Wholesale, or Local Customers for targeted WhatsApp broadcasts."><a header-actions mat-button routerLink="/customers">Customers</a></app-page-header><section class="create"><mat-form-field appearance="outline"><mat-label>Group code</mat-label><input matInput [(ngModel)]="code" placeholder="VIP" /></mat-form-field><mat-form-field appearance="outline"><mat-label>Group name</mat-label><input matInput [(ngModel)]="name" placeholder="VIP Customers" /></mat-form-field><button mat-flat-button color="primary" type="button" (click)="create()">Create group</button></section><div class="list">@for(group of groups(); track group.customerGroupId){<article><strong>{{group.groupName}}</strong><span>{{group.groupCode}}</span></article>}@empty{<p>No customer groups created yet.</p>}</div></app-page-container>`, styles: [`.create{display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:20px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}.create mat-form-field{width:240px}.list{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px;margin-top:16px}.list article{display:flex;flex-direction:column;gap:5px;padding:16px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}.list span{color:var(--wb-text-secondary);font-size:.85rem}@media(max-width:700px){.create mat-form-field,.create button{width:100%}}`], changeDetection: ChangeDetectionStrategy.OnPush })
export class CustomerGroupsComponent {
  readonly groups = signal<CustomerGroup[]>([]); code = ''; name = '';
  constructor(private readonly api: CustomerGroupApiService, private readonly snack: MatSnackBar) { this.load(); }
  load() { this.api.list().subscribe({ next: x => this.groups.set(x), error: () => this.snack.open('Customer groups could not be loaded.', 'Dismiss', { duration: 3500 }) }); }
  create() { if (!this.code.trim() || !this.name.trim()) { this.snack.open('Enter a group code and name.', 'Dismiss', { duration: 3000 }); return; } this.api.create({ groupCode: this.code, groupName: this.name, isActive: true }).subscribe({ next: x => { this.groups.update(items => [...items, x].sort((a, b) => a.groupName.localeCompare(b.groupName))); this.code = ''; this.name = ''; this.snack.open('Customer group created.', undefined, { duration: 2500 }); }, error: e => this.snack.open(e?.error?.detail || 'Customer group could not be created.', 'Dismiss', { duration: 4000 }) }); }
}
