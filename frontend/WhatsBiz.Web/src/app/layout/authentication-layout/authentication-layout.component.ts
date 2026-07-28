import { ChangeDetectionStrategy, Component } from '@angular/core'; import { RouterOutlet } from '@angular/router';
@Component({ selector: 'app-authentication-layout', imports: [RouterOutlet], template: '<main class="authentication"><router-outlet /></main>', styles: ['.authentication { min-height: 100vh; display: grid; place-items: center; }'], changeDetection: ChangeDetectionStrategy.OnPush })
export class AuthenticationLayoutComponent {}
