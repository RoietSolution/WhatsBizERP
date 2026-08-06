import { booleanAttribute, ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({ selector: 'app-action-toolbar', template: '<div class="toolbar" [class.toolbar--sticky]="sticky()"><div class="toolbar__start"><ng-content select="[toolbar-start]" /></div><div class="toolbar__end"><ng-content /></div></div>', styleUrl: './action-toolbar.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class ActionToolbarComponent { readonly sticky = input(false, { transform: booleanAttribute }); }
