import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-statistics-card', template: '<article><header><div><p>{{ title() }}</p>@if (subtitle()) { <small>{{ subtitle() }}</small> }</div><ng-content select="[card-actions]" /></header><div class="statistics-card__content"><ng-content /></div></article>', styleUrl: './statistics-card.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class StatisticsCardComponent { readonly title = input.required<string>(); readonly subtitle = input(''); }
