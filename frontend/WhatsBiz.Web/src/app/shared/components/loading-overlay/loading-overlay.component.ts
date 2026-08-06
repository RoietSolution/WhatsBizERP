import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({ selector: 'app-loading-overlay', imports: [MatProgressSpinnerModule], template: '@if (visible()) { <div class="overlay" role="status" aria-live="polite"><mat-spinner diameter="36" /><span>{{ message() }}</span></div> }', styleUrl: './loading-overlay.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class LoadingOverlayComponent { readonly visible = input(true); readonly message = input('Loading…'); }
