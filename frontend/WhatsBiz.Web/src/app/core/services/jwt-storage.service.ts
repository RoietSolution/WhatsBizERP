import { Injectable } from '@angular/core';
@Injectable({ providedIn: 'root' })
export class JwtStorageService {
  private readonly key = 'whatsbiz.auth';
  get session(): AuthSession | null {
    return this.read();
  }
  get token(): string | null {
    return this.read()?.accessToken ?? null;
  }
  get refreshToken(): string | null {
    return this.read()?.refreshToken ?? null;
  }
  save(value: AuthSession): void {
    sessionStorage.setItem(this.key, JSON.stringify(value));
  }
  updateUser(user: CurrentUser): void {
    const session = this.read();
    if (session) this.save({ ...session, user });
  }
  clear(): void {
    sessionStorage.removeItem(this.key);
  }
  private read(): AuthSession | null {
    const value = sessionStorage.getItem(this.key);
    if (!value) return null;
    try {
      return JSON.parse(value) as AuthSession;
    } catch {
      this.clear();
      return null;
    }
  }
}
export interface AuthSession {
  accessToken: string;
  refreshToken: string;
  expiresOnUtc: string;
  user: CurrentUser;
}
export interface CurrentUser {
  userId: string;
  tenantId: string;
  username: string;
  email: string;
  roles: string[];
  permissions: string[];
  features: Record<string, boolean>;
}
