import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthenticationShellComponent } from '../../shared/layout/authentication-shell/authentication-shell.component';

@Component({
  selector: 'app-authentication-layout',
  imports: [AuthenticationShellComponent, RouterOutlet],
  templateUrl: './authentication-layout.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationLayoutComponent {}
