import { booleanAttribute, ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-action-toolbar',
  templateUrl: './action-toolbar.component.html',
  styleUrl: './action-toolbar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionToolbarComponent {
  readonly sticky = input(false, { transform: booleanAttribute });
}
