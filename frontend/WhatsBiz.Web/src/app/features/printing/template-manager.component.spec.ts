import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PrintApiService, PrintTemplate } from './print-api.service';
import { TemplateManagerComponent } from './template-manager.component';

describe('TemplateManagerComponent', () => {
  const template: PrintTemplate = {
    id: 'template-a',
    code: 'GST-INVOICE-58MM',
    name: 'GST Invoice - 58mm',
    documentType: 'SALES_INVOICE',
    paperType: '58MM',
    isDefault: true,
    content: '<main></main>',
  };

  function create(response = of([template])): ComponentFixture<TemplateManagerComponent> {
    TestBed.configureTestingModule({
      imports: [TemplateManagerComponent],
      providers: [{ provide: PrintApiService, useValue: { templates: () => response } }],
    });
    return TestBed.createComponent(TemplateManagerComponent);
  }

  it('renders templates returned by the tenant-aware API', () => {
    const fixture = create();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('GST Invoice - 58mm');
    expect(fixture.nativeElement.textContent).toContain('Selected/default');
  });

  it('renders an explicit empty state', () => {
    const fixture = create(of([]));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No receipt or invoice templates');
  });

  it('renders a load error instead of a blank page', () => {
    const fixture = create(throwError(() => new Error('failed')));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'could not be loaded',
    );
  });
});
