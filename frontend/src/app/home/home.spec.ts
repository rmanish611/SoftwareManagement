import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../testing/accessibility';
import { Home } from './home';

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const card = (name: string, overrides: Record<string, unknown> = {}) => ({
    name,
    slug: name.toLowerCase().replace(/\s+/g, '-'),
    tagline: `${name} does the job.`,
    category: 'Enterprise resource planning',
    categorySlug: 'erp',
    isFeatured: false,
    fromPrice: 4999,
    currency: 'INR',
    hasFreeTier: false,
    hasLiveDemo: false,
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Home);
    http = TestBed.inject(HttpTestingController);
  };

  /** The two calls the page makes on arrival, answered in the order the component issues them. */
  const answer = async (
    products: ReturnType<typeof card>[],
    categories: Record<string, unknown>[] = [],
  ) => {
    http.expectOne('/api/v1/public/products').flush(products);
    http.expectOne('/api/v1/public/catalog/categories').flush(categories);
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('states what the company builds, in one heading', async () => {
    await create();
    await answer([]);

    const heading = element().querySelector('h1');

    expect(heading?.textContent).toContain('Software that runs the business');
  });

  it('names the product families a visitor is looking for', async () => {
    await create();
    await answer([]);

    const text = element().textContent ?? '';

    expect(text).toContain('hospital management');
    expect(text).toContain('billing');
    expect(text).toContain('school and college');
  });

  it('shows the real catalogue rather than a written-out list of products', async () => {
    await create();
    await answer([card('Ledger'), card('Clinic')]);

    const text = element().textContent ?? '';

    // Publishing a product has to put it here without a deploy, which is only true while these
    // names come from the API.
    expect(text).toContain('Ledger');
    expect(text).toContain('Clinic');
    expect(element().querySelector('a[href="/products/ledger"]')).not.toBeNull();
  });

  it('keeps the page down to a sample and sends the rest to the catalogue', async () => {
    await create();
    await answer(['One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven'].map((n) => card(n)));

    const names = [...element().querySelectorAll('.cards .card h3')].map((h) => h.textContent);

    expect(names).toHaveLength(6);
    expect(names.join(' ')).not.toContain('Seven');
    expect(element().querySelector('.catalogue__head a[href="/products"]')).not.toBeNull();
  });

  it('offers the industries as filters, and leaves out the ones with nothing in them', async () => {
    await create();
    await answer(
      [],
      [
        { name: 'Healthcare', slug: 'healthcare', description: null, iconKey: null, productCount: 2 },
        { name: 'Education', slug: 'education', description: null, iconKey: null, productCount: 0 },
      ],
    );

    const text = element().textContent ?? '';

    expect(text).toContain('Healthcare');
    expect(text).not.toContain('Education');
  });

  it('still invites a conversation when the catalogue cannot be reached', async () => {
    await create();
    http.expectOne('/api/v1/public/products').flush(null, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/v1/public/catalog/categories').flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();

    const text = element().textContent ?? '';

    // A failed catalogue call must degrade the page, not replace it. The heading and the way to
    // get in touch are the two things that have to survive.
    expect(text).toContain('Software that runs the business');
    expect(element().querySelector('a[href="/contact"]')).not.toBeNull();
    expect(text).not.toContain('503');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    await answer(
      [card('Ledger'), card('Clinic', { hasLiveDemo: true, fromPrice: null, currency: null })],
      [{ name: 'Healthcare', slug: 'healthcare', description: null, iconKey: null, productCount: 1 }],
    );

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  }, AxeTimeout);
});
