import { booleanAttribute, ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-page-container', template: '<section class="page-container" [class.page-container--wide]="wide()"><ng-content /></section>', styleUrl: './page-container.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class PageContainerComponent { readonly wide = input(false, { transform: booleanAttribute }); }
