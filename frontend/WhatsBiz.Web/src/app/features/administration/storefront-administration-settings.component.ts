import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FeatureService } from '../../core/services/feature.service';
import { StorefrontNotificationSettingsComponent } from './storefront-notification-settings.component';
import { StorefrontSettingsComponent } from './storefront-settings.component';
import { StorefrontAvailabilitySchedulesComponent } from './storefront-availability-schedules.component';
import { StorefrontCampaignsComponent } from './storefront-campaigns.component';
import { StoreDirectorySettingsComponent } from './store-directory-settings.component';
import { PermissionService } from '../../core/services/permission.service';

@Component({
  standalone: true,
  selector: 'app-storefront-administration-settings',
  imports: [RouterLink,StorefrontSettingsComponent, StorefrontNotificationSettingsComponent, StorefrontAvailabilitySchedulesComponent, StorefrontCampaignsComponent, StoreDirectorySettingsComponent],
  template: `<app-storefront-settings></app-storefront-settings><nav class="market-links" aria-label="Marketplace settings">@if(features.hasFeature('CITY_MARKETPLACE')){<a routerLink="/admin/storefront/cities">City and service areas</a>}@if(features.hasFeature('PARTNER_SELLER_MANAGEMENT')){<a routerLink="/admin/storefront/partner-sellers">Partner sellers</a>}@if(features.hasFeature('PARTNER_SETTLEMENTS') && permissions.has('supplier.outstanding.view')){<a routerLink="/admin/storefront/partner-settlements">Partner settlements</a>}</nav><app-store-directory-settings/>
    @if(features.hasFeature('STOREFRONT_SCHEDULED_SELLING')){<app-storefront-availability-schedules></app-storefront-availability-schedules>}
    @if(features.hasFeature('STOREFRONT_POPUP_CAMPAIGNS')){<app-storefront-campaigns></app-storefront-campaigns>}
    @if(features.hasFeature('STOREFRONT_NOTIFICATIONS')){<app-storefront-notification-settings></app-storefront-notification-settings>}`,
  styles:[`.market-links{display:flex;flex-wrap:wrap;gap:.7rem;margin:1rem 0}.market-links a{display:inline-flex;align-items:center;min-height:42px;padding:.45rem .8rem;border-radius:9px;border:1px solid var(--wb-border);background:var(--wb-surface);color:var(--wb-primary);font-weight:650;text-decoration:none}.market-links a:hover,.market-links a:focus-visible{box-shadow:var(--wb-shadow-md);outline:2px solid var(--wb-primary);outline-offset:2px}`]
})
export class StorefrontAdministrationSettingsComponent { readonly features=inject(FeatureService); readonly permissions=inject(PermissionService); constructor(){this.features.refresh().subscribe();} }
