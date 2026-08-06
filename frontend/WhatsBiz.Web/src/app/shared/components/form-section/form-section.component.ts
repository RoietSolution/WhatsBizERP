import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-form-section', template: '<section><header><span class="material-symbols-rounded">{{ icon() }}</span><div><h2>{{ title() }}</h2>@if (description()) { <p>{{ description() }}</p> }</div></header><div class="form-section__body"><ng-content /></div></section>', styleUrl: './form-section.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class FormSectionComponent { readonly title = input.required<string>(); readonly description = input(''); readonly icon = input('edit_note'); }
