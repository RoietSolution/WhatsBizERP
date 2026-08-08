import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  createdAt: Date;
  read: boolean;
  type: 'info' | 'success' | 'warning' | 'danger';
}
interface ApiNotification {
  id: string;
  title: string;
  message?: string;
  generatedOn: string;
  isRead: boolean;
  severity: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  readonly notifications = signal<AppNotification[]>([]);
  readonly loading = signal(false);
  constructor(private readonly http: HttpClient) {
    this.load();
  }
  load(refresh = false): void {
    this.loading.set(true);
    this.http
      .get<ApiNotification[]>('/api/dashboard/notifications', { params: { refresh } })
      .subscribe({
        next: (items) => {
          this.notifications.set(
            items.map((item) => ({
              id: item.id,
              title: item.title,
              message: item.message ?? 'Business notification',
              createdAt: new Date(item.generatedOn),
              read: item.isRead,
              type: this.tone(item.severity),
            })),
          );
          this.loading.set(false);
        },
        error: () => {
          if (!this.notifications().length)
            this.notifications.set([
              {
                id: 'welcome',
                title: 'Welcome to KhataDhari ERP',
                message: 'Your notification center is ready. New business alerts will appear here.',
                createdAt: new Date(),
                read: false,
                type: 'info',
              },
            ]);
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
    this.notifications.update((items) =>
      items.map((item) => (item.id === id ? { ...item, read: true } : item)),
    );
  }
  markAllRead(): void {
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
