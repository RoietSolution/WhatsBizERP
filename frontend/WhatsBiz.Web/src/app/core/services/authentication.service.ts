import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { finalize, Observable, shareReplay, tap, throwError } from 'rxjs';
import { AuthSession, CurrentUser, JwtStorageService } from './jwt-storage.service';
import { CurrentUserService } from './current-user.service';

export interface ForgotPasswordResponse { message: string; resetToken?: string; userId?: string; }

@Injectable({ providedIn: 'root' })
export class AuthenticationService {
  readonly isAuthenticated = signal(false);
  private refreshRequest?: Observable<AuthSession>;

  constructor(private readonly http: HttpClient, private readonly storage: JwtStorageService, private readonly currentUser: CurrentUserService, private readonly router: Router) {
    const session = storage.session;
    if (session) { this.currentUser.set(session.user); this.isAuthenticated.set(true); }
  }

  login(username: string, password: string): Observable<AuthSession> { return this.http.post<AuthSession>('/api/auth/login', { username, password }).pipe(tap(session => this.apply(session))); }
  forgotPassword(identifier: string): Observable<ForgotPasswordResponse> { return this.http.post<ForgotPasswordResponse>('/api/auth/forgot-password', { identifier }); }
  resetPassword(userId: string, token: string, newPassword: string): Observable<void> { return this.http.post<void>('/api/auth/reset-password', { userId, token, newPassword }); }
  changePassword(currentPassword: string, newPassword: string): Observable<void> { return this.http.post<void>('/api/auth/change-password', { currentPassword, newPassword }); }
  updateProfile(email: string): Observable<CurrentUser> { return this.http.put<CurrentUser>('/api/auth/profile', { email }).pipe(tap(user => { const updated = { ...this.currentUser.user(), ...user } as CurrentUser; this.currentUser.set(updated); this.storage.updateUser(updated); })); }

  refresh(): Observable<AuthSession> {
    if (this.refreshRequest) return this.refreshRequest;
    const refreshToken = this.storage.refreshToken;
    if (!refreshToken) return throwError(() => new Error('No refresh token is available.'));
    this.refreshRequest = this.http.post<AuthSession>('/api/auth/refresh', { refreshToken }).pipe(tap(session => this.apply(session)), finalize(() => this.refreshRequest = undefined), shareReplay({ bufferSize: 1, refCount: false }));
    return this.refreshRequest;
  }

  logout(): void { const refreshToken = this.storage.refreshToken; if (refreshToken) this.http.post('/api/auth/logout', { refreshToken }).subscribe({ error: () => undefined }); this.clearSession(); }
  clearSession(): void { this.storage.clear(); this.currentUser.clear(); this.isAuthenticated.set(false); void this.router.navigateByUrl('/login'); }
  private apply(session: AuthSession): void { this.storage.save(session); this.currentUser.set(session.user); this.isAuthenticated.set(true); }
}
