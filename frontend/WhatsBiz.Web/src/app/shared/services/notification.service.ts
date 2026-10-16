import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, forkJoin, of } from 'rxjs';

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
  private feedbackNotificationsAllowed = true;
  constructor(private readonly http: HttpClient) {
    this.load();
  }
  load(refresh = false): void {
    this.loading.set(true);
    forkJoin({
      dashboard: this.http.get<ApiNotification[]>('/api/dashboard/notifications', { params: { refresh } }),
      feedback: this.feedbackNotificationsAllowed
        ? this.http.get<FeedbackNotification[]>('/api/storefront-feedback/notifications').pipe(catchError((error: HttpErrorResponse) => {
            if (error.status === 403) this.feedbackNotificationsAllowed = false;
            return of([]);
          }))
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
    if (id.startsWith('feedback:') && this.feedbackNotificationsAllowed) this.http.post(`/api/storefront-feedback/notifications/${id.substring(9)}/read`, {}).subscribe({ error: (error: HttpErrorResponse) => { if (error.status === 403) this.feedbackNotificationsAllowed = false; } });
    this.notifications.update((items) =>
      items.map((item) => (item.id === id ? { ...item, read: true } : item)),
    );
  }
  markAllRead(): void {
    if (this.feedbackNotificationsAllowed) this.http.post('/api/storefront-feedback/notifications/read-all', {}).subscribe({ error: (error: HttpErrorResponse) => { if (error.status === 403) this.feedbackNotificationsAllowed = false; } });
    this.notifications.update((items) => items.map((item) => ({ ...item, read: true })));
  }
  remove(id: string): void {
    this.notifications.update((items) => items.filter((item) => item.id !== id));
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
