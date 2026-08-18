import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AuthenticationShellComponent } from '../../shared/layout/authentication-shell/authentication-shell.component';

@Component({
  selector: 'app-authentication-layout',
  imports: [AuthenticationShellComponent],
  templateUrl: './authentication-layout.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationLayoutComponent {}
