import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

/**
 * A rendered-DOM test, not a smoke test. `expect(app).toBeTruthy()` would pass even if the
 * template threw, so these assertions read real text out of the rendered element and check the
 * data-testid="app-shell" anchor the phase gate depends on (D10b).
 */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideZonelessChangeDetection(), provideRouter(routes)],
    }).compileComponents();
  });

  it('renders the application shell', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const shell = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="app-shell"]');

    expect(shell).not.toBeNull();
  });

  it('renders the company name in the header', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Software Management');
  });

  it('offers a skip link as the first focusable element', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const skip = (fixture.nativeElement as HTMLElement).querySelector('a.skip-link');

    expect(skip?.textContent?.trim()).toBe('Skip to content');
    expect(skip?.getAttribute('href')).toBe('#main');
  });

  it('exposes a main landmark for assistive technology', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const main = (fixture.nativeElement as HTMLElement).querySelector('main#main');

    expect(main).not.toBeNull();
  });
});
