import { ChangeDetectionStrategy, Component } from '@angular/core'; import { RouterLink } from '@angular/router';
@Component({ imports: [RouterLink], template: '<h1>Page not found</h1><a routerLink="/">Return to dashboard</a>', changeDetection: ChangeDetectionStrategy.OnPush })
export class NotFoundComponent {}
