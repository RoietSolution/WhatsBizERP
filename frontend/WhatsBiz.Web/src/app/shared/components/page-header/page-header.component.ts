import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-page-header', template: '<header class="page-header"><div><p class="page-header__eyebrow">{{ eyebrow() }}</p><h1>{{ title() }}</h1>@if (description()) { <p class="page-header__description">{{ description() }}</p> }</div><div class="page-header__actions"><ng-content /></div></header>', styleUrl: './page-header.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class PageHeaderComponent { readonly title = input.required<string>(); readonly description = input(''); readonly eyebrow = input(''); }
