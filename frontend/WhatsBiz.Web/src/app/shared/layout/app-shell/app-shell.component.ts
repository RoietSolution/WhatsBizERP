import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { MatSidenavModule } from '@angular/material/sidenav';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { LayoutStateService } from '../../services/layout-state.service';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { BreadcrumbComponent } from '../../components/breadcrumb/breadcrumb.component';

@Component({ selector: 'app-shell', imports: [MatSidenavModule, SidebarComponent, TopbarComponent, BreadcrumbComponent], template: '<mat-sidenav-container><mat-sidenav [mode]="mobile() ? `over` : `side`" [opened]="mobile() ? layout.mobileOpen() : true" [disableClose]="!mobile()" (closedStart)="layout.closeMobile()"><app-sidebar [collapsed]="!mobile() && layout.sidebarCollapsed()" (collapseToggle)="layout.toggleSidebar()" (navigate)="mobile() && layout.closeMobile()" /></mat-sidenav><mat-sidenav-content><app-topbar (menuToggle)="layout.toggleMobile()" /><div class="content-frame"><app-breadcrumb /><main id="main-content" tabindex="-1"><ng-content /></main><footer><span>© {{ year }} KhataDhari ERP</span><span>Secure enterprise workspace</span></footer></div></mat-sidenav-content></mat-sidenav-container>', styleUrl: './app-shell.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class AppShellComponent { readonly layout = inject(LayoutStateService); private readonly breakpoint = inject(BreakpointObserver); readonly mobile = toSignal(this.breakpoint.observe('(max-width: 1199px)').pipe(map((state) => state.matches)), { initialValue: false }); readonly year = new Date().getFullYear(); }
