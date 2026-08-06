import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-empty-state', template: '<section><span class="material-symbols-rounded" aria-hidden="true">{{ icon() }}</span><h2>{{ title() }}</h2><p>{{ description() }}</p><div><ng-content /></div></section>', styleUrl: './empty-state.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class EmptyStateComponent { readonly title = input('Nothing here yet'); readonly description = input('There is no information to display.'); readonly icon = input('inbox'); }
