import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthenticationShellComponent } from './authentication-shell.component';

describe('AuthenticationShellComponent', () => {
  it('does not render language or theme controls', async () => {
    await TestBed.configureTestingModule({
      imports: [AuthenticationShellComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(AuthenticationShellComponent);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[aria-label="Select language"]')).toBeNull();
    expect(element.querySelector('[title="Theme switch coming soon"]')).toBeNull();
  });
});
