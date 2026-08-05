import { Injectable, signal } from '@angular/core'; import { CurrentUser } from './jwt-storage.service';
@Injectable({ providedIn: 'root' }) export class CurrentUserService { readonly user = signal<CurrentUser | null>(null); set(user: CurrentUser): void { this.user.set(user); } clear(): void { this.user.set(null); } }
