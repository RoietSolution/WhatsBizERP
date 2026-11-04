import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatSidenavModule } from '@angular/material/sidenav';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { FeatureService } from '../../../core/services/feature.service';
import { PermissionService } from '../../../core/services/permission.service';
import { mobileQuickAccessLinks, visibleQuickAccessLinks } from '../../services/quick-access-links';
import { LayoutStateService } from '../../services/layout-state.service';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { BreadcrumbComponent } from '../../components/breadcrumb/breadcrumb.component';

@Component({
  selector: 'app-shell',
  imports: [MatSidenavModule, RouterLink, RouterLinkActive, SidebarComponent, TopbarComponent, BreadcrumbComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShellComponent {
  readonly layout = inject(LayoutStateService);
  private readonly permissions = inject(PermissionService);
  private readonly features = inject(FeatureService);
  private readonly breakpoint = inject(BreakpointObserver);
  private readonly router = inject(Router);
  readonly mobile = toSignal(
    this.breakpoint.observe('(max-width: 1199px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  readonly compactMobile = toSignal(
    this.breakpoint.observe('(max-width: 899px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  readonly mobileDestinations = computed(() =>
    visibleQuickAccessLinks(
      mobileQuickAccessLinks,
      (permission) => this.permissions.has(permission),
      (feature) => this.features.hasFeature(feature),
    ),
  );
  readonly posSale = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => this.isPosSale(event.urlAfterRedirects)),
      startWith(this.isPosSale(this.router.url)),
    ),
    { initialValue: this.isPosSale(this.router.url) },
  );
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );
  readonly moreActive = computed(() => {
    const path = this.currentUrl().split(/[?#]/, 1)[0];
    return !this.mobileDestinations().some((item) => path === item.route || path.startsWith(`${item.route}/`));
  });
  readonly year = new Date().getFullYear();

  private isPosSale(url: string): boolean {
    return url.split('?')[0].split('#')[0] === '/pos';
  }
}
