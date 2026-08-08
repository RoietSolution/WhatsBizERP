import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './confirm-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialogComponent {
  readonly data = inject<{ title: string; message: string }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<ConfirmDialogComponent>);
  close(): void {
    this.ref.close(true);
  }
}
