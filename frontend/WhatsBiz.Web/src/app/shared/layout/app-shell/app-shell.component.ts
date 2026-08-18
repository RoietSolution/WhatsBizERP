import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { MatSidenavModule } from '@angular/material/sidenav';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
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
  readonly mobile = toSignal(
    this.breakpoint.observe('(max-width: 1199px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  readonly year = new Date().getFullYear();
}
