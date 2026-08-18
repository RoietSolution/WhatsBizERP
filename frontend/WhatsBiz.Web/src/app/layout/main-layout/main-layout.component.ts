import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppShellComponent } from '../../shared/layout/app-shell/app-shell.component';

@Component({
  selector: 'app-main-layout',
  imports: [AppShellComponent, RouterOutlet],
  templateUrl: './main-layout.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainLayoutComponent {}
