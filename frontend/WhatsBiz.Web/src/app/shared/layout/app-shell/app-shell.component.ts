import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatSidenavModule } from '@angular/material/sidenav';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { LayoutStateService } from '../../services/layout-state.service';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { BreadcrumbComponent } from '../../components/breadcrumb/breadcrumb.component';

@Component({
  selector: 'app-shell',
  imports: [MatSidenavModule, SidebarComponent, TopbarComponent, BreadcrumbComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShellComponent {
  readonly layout = inject(LayoutStateService);
  private readonly breakpoint = inject(BreakpointObserver);
  private readonly router = inject(Router);
  readonly mobile = toSignal(
    this.breakpoint.observe('(max-width: 1199px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  readonly posSale = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => this.isPosSale(event.urlAfterRedirects)),
      startWith(this.isPosSale(this.router.url)),
    ),
    { initialValue: this.isPosSale(this.router.url) },
  );
  readonly year = new Date().getFullYear();

  private isPosSale(url: string): boolean {
    return url.split('?')[0].split('#')[0] === '/pos';
  }
}
