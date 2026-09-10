import { Component, signal } from '@angular/core';
import { PrintApiService, PrintTemplate } from './print-api.service';
@Component({
  templateUrl: './template-manager.component.html',
  styles: [
    `
      .grid {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
      }
      article {
        padding: 1rem;
        background: #fff;
        border: 1px solid #ddd;
        border-radius: 0.5rem;
      }
      span {
        float: right;
      }
      @media (max-width: 700px) {
        .grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class TemplateManagerComponent {
  templates = signal<PrintTemplate[]>([]);
  loading = signal(true);
  error = signal('');
  constructor(api: PrintApiService) {
    api.templates().subscribe({
      next: (x) => {
        this.templates.set(x);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Receipt and invoice templates could not be loaded.');
        this.loading.set(false);
      },
    });
  }
}
