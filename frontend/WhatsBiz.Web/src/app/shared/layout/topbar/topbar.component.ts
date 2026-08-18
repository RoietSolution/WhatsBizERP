import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { AuthenticationService } from '../../../core/services/authentication.service';
import { CurrentUserService } from '../../../core/services/current-user.service';
import { NotificationPanelComponent } from '../../components/notification-panel/notification-panel.component';
import { SearchBoxComponent } from '../../components/search-box/search-box.component';
import { LayoutStateService } from '../../services/layout-state.service';
import { NotificationService } from '../../services/notification.service';
import { ProfilePhotoService } from '../../services/profile-photo.service';
import { GlobalSearchService } from '../../services/global-search.service';

@Component({
  selector: 'app-topbar',
  imports: [
    MatButtonModule,
    MatMenuModule,
    RouterLink,
    SearchBoxComponent,
    NotificationPanelComponent,
  ],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TopbarComponent {
  readonly menuToggle = output<void>();
  readonly authentication = inject(AuthenticationService);
  readonly layout = inject(LayoutStateService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly notifications = inject(NotificationService);
  private readonly profilePhoto = inject(ProfilePhotoService);
  readonly globalSearch = inject(GlobalSearchService);
  readonly user = this.currentUser.user;
  readonly initials = computed(() => (this.user()?.username ?? 'U').slice(0, 2).toUpperCase());
  readonly photo = this.profilePhoto.photo;
  readonly unread = computed(
    () => this.notifications.notifications().filter((item) => !item.read).length,
  );
  onSearch(value: string): void { this.globalSearch.search(value); }
  clearSearch(): void { this.globalSearch.clear(); }
}
