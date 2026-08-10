import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PrintApiService, Printer } from './print-api.service';
import { DEFAULT_PAPER_SIZE, PAPER_SIZES } from './paper-size';
@Component({
  imports: [FormsModule, MatButtonModule, MatSnackBarModule],
  templateUrl: './printer-configuration.component.html',
  styles: [
    `
      form {
        display: flex;
        gap: 0.7rem;
        flex-wrap: wrap;
        background: #fff;
        padding: 1rem;
      }
      input,
      select {
        padding: 0.6rem;
      }
      table {
        margin-top: 1rem;
        width: 100%;
        border-collapse: collapse;
      }
      th,
      td {
        padding: 0.7rem;
        border-bottom: 1px solid #ddd;
        text-align: left;
      }
    `,
  ],
})
export class PrinterConfigurationComponent {
  readonly paperSizes = PAPER_SIZES;
  printers = signal<Printer[]>([]);
  model = {
    printerName: '',
    displayName: '',
    printerType: 'THERMAL',
    paperSize: DEFAULT_PAPER_SIZE,
    isDefault: true,
    autoCut: true,
    isActive: true,
  };
  constructor(
    private api: PrintApiService,
    private snack: MatSnackBar,
  ) {
    this.load();
  }
  load() {
    this.api.printers().subscribe((x) => this.printers.set(x));
  }
  save() {
    this.api.savePrinter(this.model).subscribe(() => {
      this.snack.open('Printer saved', 'Close', { duration: 2000 });
      this.load();
    });
  }
}
