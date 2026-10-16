import { Component } from '@angular/core';
import { StorefrontNotificationSettingsComponent } from './storefront-notification-settings.component';
import { StorefrontSettingsComponent } from './storefront-settings.component';

@Component({
  standalone: true,
  selector: 'app-storefront-administration-settings',
  imports: [StorefrontSettingsComponent, StorefrontNotificationSettingsComponent],
  template: '<app-storefront-settings></app-storefront-settings><app-storefront-notification-settings></app-storefront-notification-settings>',
})
export class StorefrontAdministrationSettingsComponent {}
