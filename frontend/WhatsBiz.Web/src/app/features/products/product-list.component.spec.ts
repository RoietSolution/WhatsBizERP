import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { ProductListComponent } from './product-list.component';
import { ProductImportTemplatesDialogComponent } from './product-import-templates-dialog.component';

describe('ProductListComponent', () => {
  it('opens the import template chooser from the toolbar action', () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', [
      'search',
      'template',
    ]);
    api.search.and.returnValue(
      of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }),
    );
    const snack = jasmine.createSpyObj('MatSnackBar', ['open']);
    const dialog = jasmine.createSpyObj('MatDialog', ['open']);
    const router = jasmine.createSpyObj('Router', ['navigate']);
    const component = TestBed.runInInjectionContext(
      () => new ProductListComponent(api, snack, dialog, router),
    );
    expect(api.search).toHaveBeenCalledWith(
      jasmine.objectContaining({ sortBy: 'createdOn', descending: true, pageNumber: 1 }),
    );
    component.handle({ action: 'template' });

    expect(component.config.templateEnabled).toBeTrue();
    expect(dialog.open).toHaveBeenCalledWith(
      ProductImportTemplatesDialogComponent,
      jasmine.objectContaining({ width: '620px', maxWidth: 'calc(100vw - 24px)' }),
    );
    expect(api.template).not.toHaveBeenCalled();
  });
});
