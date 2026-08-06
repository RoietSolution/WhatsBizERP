import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthenticationShellComponent } from '../../shared/layout/authentication-shell/authentication-shell.component';

@Component({ selector: 'app-authentication-layout', imports: [AuthenticationShellComponent, RouterOutlet], template: '<app-authentication-shell><router-outlet /></app-authentication-shell>', changeDetection: ChangeDetectionStrategy.OnPush })
export class AuthenticationLayoutComponent {}
