import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, forkJoin, of } from 'rxjs';
import { CurrentUserService } from '../../core/services/current-user.service';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  createdAt: Date;
  read: boolean;
  type: 'info' | 'success' | 'warning' | 'danger';
  referenceType?: string;
  referenceId?: string;
}
interface ApiNotification {
  id: string;
  title: string;
  message?: string;
  generatedOn: string;
  isRead: boolean;
  severity: string;
  referenceType?: string;
  referenceId?: string;
}
interface FeedbackNotification { id: string; ticketId: string; ticketReference: string; title: string; message: string; isRead: boolean; createdAtUtc: string; }

@Injectable({ providedIn: 'root' })
export class NotificationService {
  readonly notifications = signal<AppNotification[]>([]);
  readonly loading = signal(false);
  constructor(private readonly http: HttpClient, private readonly currentUser: CurrentUserService) {
    this.load();
  }
  load(refresh = false): void {
    this.loading.set(true);
    forkJoin({
      dashboard: this.canReadDashboard()
        ? this.http.get<ApiNotification[]>('/api/dashboard/notifications', { params: { refresh } }).pipe(catchError(() => of([])))
        : of([]),
      feedback: this.canReadFeedback()
        ? this.http.get<FeedbackNotification[]>('/api/storefront-feedback/notifications').pipe(catchError(() => of([])))
        : of([]),
    }).subscribe({
        next: ({ dashboard, feedback }) => {
          const feedbackItems = feedback.map((item) => ({ id: `feedback:${item.id}`, title: item.title, message: item.message, createdAt: new Date(item.createdAtUtc), read: item.isRead, type: 'warning' as const, referenceType: 'STOREFRONT_FEEDBACK', referenceId: item.ticketId }));
          this.notifications.set(
            [...dashboard.map((item) => ({
              id: item.id,
              title: item.title,
              message: item.message ?? 'Business notification',
              createdAt: new Date(item.generatedOn),
              read: item.isRead,
              type: this.tone(item.severity),
              referenceType: item.referenceType,
              referenceId: item.referenceId,
            })), ...feedbackItems].sort((a,b) => b.createdAt.getTime() - a.createdAt.getTime()),
          );
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        },
      });
  }
  add(notification: Omit<AppNotification, 'id' | 'createdAt' | 'read'>): void {
    this.notifications.update((items) => [
      { ...notification, id: crypto.randomUUID(), createdAt: new Date(), read: false },
      ...items,
    ]);
  }
  markRead(id: string): void {
    if (id.startsWith('feedback:') && this.canReadFeedback()) this.http.post(`/api/storefront-feedback/notifications/${id.substring(9)}/read`, {}).subscribe({ error: () => undefined });
    this.notifications.update((items) =>
      items.map((item) => (item.id === id ? { ...item, read: true } : item)),
    );
  }
  markAllRead(): void {
    if (this.canReadFeedback()) this.http.post('/api/storefront-feedback/notifications/read-all', {}).subscribe({ error: () => undefined });
    this.notifications.update((items) => items.map((item) => ({ ...item, read: true })));
  }
  remove(id: string): void {
    this.notifications.update((items) => items.filter((item) => item.id !== id));
  }
  private canReadDashboard(): boolean {
    const user = this.currentUser.user();
    return !!user?.permissions.includes('dashboard.view') && user.features?.['DASHBOARD'] !== false;
  }
  private canReadFeedback(): boolean {
    const user = this.currentUser.user();
    return !!user?.permissions.includes('customer.feedback.manage') && user.features?.['STOREFRONT_FEEDBACK'] !== false;
  }
  private tone(value: string): AppNotification['type'] {
    const tone = value.toLowerCase();
    return tone === 'error' || tone === 'critical'
      ? 'danger'
      : tone === 'warning'
        ? 'warning'
        : tone === 'success'
          ? 'success'
          : 'info';
  }
}
