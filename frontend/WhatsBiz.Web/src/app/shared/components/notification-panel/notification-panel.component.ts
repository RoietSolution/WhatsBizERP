import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { NotificationService } from '../../services/notification.service';

@Component({
  selector: 'app-notification-panel',
  imports: [DatePipe, MatButtonModule],
  templateUrl: './notification-panel.component.html',
  styleUrl: './notification-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationPanelComponent {
  readonly notifications = inject(NotificationService);
  readonly unread = computed(
    () => this.notifications.notifications().filter((item) => !item.read).length,
  );
}
