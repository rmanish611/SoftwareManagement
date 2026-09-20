import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describeViolations, findSeriousAccessibilityViolations } from '../../testing/accessibility';
import { Shell } from './shell';

/**
 * The chrome every page shares.
 *
 * The navigation is the part of this site most easily got wrong without anyone noticing: a link
 * that is missing costs a visitor the page, and a menu control that says nothing costs a screen
 * reader user the whole navigation.
 */
describe('Shell', () => {
  let fixture: ComponentFixture<Shell>;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const toggle = (): HTMLButtonElement =>
    element().querySelector('[data-testid="nav-toggle"]') as HTMLButtonElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideZonelessChangeDetection(),

        // A catch-all rather than no routes: one test follows a link, and a router with nothing
        // configured refuses the navigation before the component ever sees the click.
        provideRouter([{ path: '**', children: [] }]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
  });

  it('reaches every public section from the header', async () => {
    const hrefs = [...element().querySelectorAll('.shell__nav a')].map((a) => a.getAttribute('href'));

    // Each of these is a route a visitor has no other way of finding. A header that lists three of
    // the five is how a catalogue ends up with no traffic.
    expect(hrefs).toContain('/');
    expect(hrefs).toContain('/products');
    expect(hrefs).toContain('/services');
    expect(hrefs).toContain('/about');
    expect(hrefs).toContain('/contact');
  });

  it('offers both enquiry routes in the footer', async () => {
    const hrefs = [...element().querySelectorAll('.shell__footer-nav a')].map((a) =>
      a.getAttribute('href'),
    );

    expect(hrefs).toContain('/request-demo');
    expect(hrefs).toContain('/request-quote');
  });

  it('starts with the menu closed and says so', async () => {
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(toggle().getAttribute('aria-controls')).toBe('primary-nav');
    expect(element().querySelector('#primary-nav')).not.toBeNull();
  });

  it('opens and closes the menu, and reports the state each time', async () => {
    toggle().click();
    await fixture.whenStable();

    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(element().querySelector('.shell__nav--open')).not.toBeNull();

    toggle().click();
    await fixture.whenStable();

    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(element().querySelector('.shell__nav--open')).toBeNull();
  });

  it('closes the menu once a link inside it is followed', async () => {
    toggle().click();
    await fixture.whenStable();

    (element().querySelector('.shell__nav a[href="/products"]') as HTMLAnchorElement).click();
    await fixture.whenStable();

    // Otherwise the menu stays over the page the visitor just asked for.
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
  });

  it('names the menu control for a screen reader, in both states', async () => {
    expect(toggle().textContent).toContain('Menu');

    toggle().click();
    await fixture.whenStable();

    expect(toggle().textContent).toContain('Close menu');
  });

  it('has no serious or critical accessibility violations with the menu open', async () => {
    toggle().click();
    await fixture.whenStable();

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  });
});
