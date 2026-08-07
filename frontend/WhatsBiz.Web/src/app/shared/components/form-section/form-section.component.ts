import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-form-section',
  templateUrl: './form-section.component.html',
  styleUrl: './form-section.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormSectionComponent {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly icon = input('edit_note');
}
