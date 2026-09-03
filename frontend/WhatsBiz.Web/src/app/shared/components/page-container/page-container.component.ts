import { booleanAttribute, ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-page-container',
  templateUrl: './page-container.component.html',
  styleUrl: './page-container.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageContainerComponent {
  readonly wide = input(false, { transform: booleanAttribute });
  readonly fill = input(false, { transform: booleanAttribute });
}
