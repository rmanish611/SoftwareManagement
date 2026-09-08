import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { convertToParamMap } from '@angular/router';
import { describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { ProductCatalog } from './product-catalog';

describe('ProductCatalog', () => {
  let fixture: ComponentFixture<ProductCatalog>;
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

  const create = async (query: Record<string, string> = {}) => {
    await TestBed.configureTestingModule({
      imports: [ProductCatalog],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: of(convertToParamMap(query)) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductCatalog);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('lists the published products with the industry each one serves', async () => {
    await create();
    http.expectOne('/api/v1/public/catalog/categories').flush([
      { name: 'Enterprise resource planning', slug: 'erp', description: null, iconKey: null, productCount: 2 },
    ]);
    http.expectOne('/api/v1/public/products').flush([card('Ledger'), card('Clinic')]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="card-ledger"]')).not.toBeNull();
    expect(element().querySelector('[data-testid="card-clinic"]')).not.toBeNull();
    expect(element().textContent).toContain('Enterprise resource planning');
  });

  it('shows the starting price in rupees rather than a bare number', async () => {
    await create();
    http.expectOne('/api/v1/public/catalog/categories').flush([]);
    http.expectOne('/api/v1/public/products').flush([card('Ledger', { fromPrice: 4999, currency: 'INR' })]);
    await fixture.whenStable();

    const price = element().querySelector('[data-testid="from-price"]')?.textContent ?? '';
    expect(price).toContain('4,999');
    expect(price).toContain('₹');
  });

  it('says a product is free to start when that is all it has', async () => {
    await create();
    http.expectOne('/api/v1/public/catalog/categories').flush([]);
    http.expectOne('/api/v1/public/products').flush([card('Starter', { fromPrice: null, currency: null, hasFreeTier: true })]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="from-price"]')?.textContent).toContain('Free to start');
  });

  it('passes the industry filter from the address bar to the API', async () => {
    await create({ category: 'healthcare' });
    http.expectOne('/api/v1/public/catalog/categories').flush([]);

    const request = http.expectOne((r) => r.url === '/api/v1/public/products');
    expect(request.request.params.get('category')).toBe('healthcare');
    request.flush([]);
    await fixture.whenStable();
  });

  it('invites contact instead of showing a blank page when a search matches nothing', async () => {
    await create({ search: 'nothing at all' });
    http.expectOne('/api/v1/public/catalog/categories').flush([]);
    http.expectOne((r) => r.url === '/api/v1/public/products').flush([]);
    await fixture.whenStable();

    const empty = element().querySelector('[data-testid="catalog-empty"]');
    expect(empty?.textContent).toContain('Nothing here matches');
    expect(empty?.querySelector('a[href="/contact"]')).not.toBeNull();
  });

  it('does the same when the request fails, without showing a status code', async () => {
    await create();
    http.expectOne('/api/v1/public/catalog/categories').flush([]);
    http.expectOne('/api/v1/public/products').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const text = element().textContent ?? '';
    expect(text).toContain('Nothing here matches');
    expect(text).not.toContain('500');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/catalog/categories').flush([
      { name: 'Enterprise resource planning', slug: 'erp', description: null, iconKey: null, productCount: 1 },
    ]);
    http.expectOne('/api/v1/public/products').flush([card('Ledger')]);
    await fixture.whenStable();

    document.body.appendChild(element());
    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  });
});
