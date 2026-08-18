import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { PageContainerComponent } from '../../components/page-container/page-container.component';
import { PageHeaderComponent } from '../../components/page-header/page-header.component';

@Component({
  selector: 'app-master-form-layout',
  imports: [MatTabsModule, PageContainerComponent, PageHeaderComponent],
  templateUrl: './master-form-layout.component.html',
  styleUrl: './master-form-layout.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterFormLayoutComponent {
  readonly title = input.required<string>();
  readonly description = input('');
}
