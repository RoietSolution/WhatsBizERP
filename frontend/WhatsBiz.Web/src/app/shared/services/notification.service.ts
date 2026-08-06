import { Injectable, signal } from '@angular/core';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  createdAt: Date;
  read: boolean;
  type: 'info' | 'success' | 'warning' | 'danger';
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  readonly notifications = signal<AppNotification[]>([]);

  add(notification: Omit<AppNotification, 'id' | 'createdAt' | 'read'>): void {
    this.notifications.update((items) => [{ ...notification, id: crypto.randomUUID(), createdAt: new Date(), read: false }, ...items]);
  }

  markAllRead(): void { this.notifications.update((items) => items.map((item) => ({ ...item, read: true }))); }
  remove(id: string): void { this.notifications.update((items) => items.filter((item) => item.id !== id)); }
}
