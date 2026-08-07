import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { PageContainerComponent } from '../page-container/page-container.component';
import { PageHeaderComponent } from '../page-header/page-header.component';
import { SummaryCardComponent } from '../summary-card/summary-card.component';
import { ActionToolbarComponent } from '../action-toolbar/action-toolbar.component';
import { MasterSummary } from '../../master/master.models';
@Component({
  selector: 'app-operations-workspace',
  imports: [
    RouterLink,
    MatButtonModule,
    PageContainerComponent,
    PageHeaderComponent,
    SummaryCardComponent,
    ActionToolbarComponent,
  ],
  templateUrl: './operations-workspace.component.html',
  styleUrl: './operations-workspace.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationsWorkspaceComponent {
  readonly eyebrow = input('Operations');
  readonly title = input.required<string>();
  readonly description = input('');
  readonly summaries = input<MasterSummary[]>([]);
  readonly statusText = input('Ready');
  readonly lastRefreshed = input('Just now');
}
