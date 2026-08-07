import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { GstApiService, GstSettings } from './gst-api.service';
@Component({
  imports: [FormsModule, MatButtonModule, MatSnackBarModule],
  templateUrl: './gst-configuration.component.html',
  styles: [
    `
      form {
        max-width: 700px;
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 1rem;
        background: #fff;
        padding: 1.25rem;
      }
      label {
        display: grid;
        gap: 0.3rem;
      }
      input,
      select {
        padding: 0.65rem;
      }
      .check {
        display: flex;
      }
      @media (max-width: 650px) {
        form {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class GstConfigurationComponent {
  readonly model = signal<GstSettings | null>(null);
  constructor(
    private api: GstApiService,
    private snack: MatSnackBar,
  ) {
    api
      .settings()
      .subscribe((x) =>
        this.model.set({ ...x, gstEffectiveDate: x.gstEffectiveDate?.slice(0, 10) }),
      );
  }
  save() {
    this.api
      .saveSettings(this.model()!)
      .subscribe(() => this.snack.open('GST configuration saved', 'Close', { duration: 2500 }));
  }
}
