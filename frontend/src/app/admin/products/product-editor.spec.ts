import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ProductEditor } from './product-editor';

describe('ProductEditor', () => {
  let fixture: ComponentFixture<ProductEditor>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const product = (overrides: Record<string, unknown> = {}) => ({
    id: 'product-1',
    name: 'Ledger',
    slug: 'ledger',
    tagline: 'Runs the business.',
    summary: 'A summary.',
    category: 'Enterprise resource planning',
    categorySlug: 'erp',
    status: 'Draft',
    features: [],
    screenshots: [],
    plans: [],
    demo: null,
    ...overrides,
  });

  const create = async (routeId: string | null) => {
    await TestBed.configureTestingModule({
      imports: [ProductEditor],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => routeId } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductEditor);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('offers the industries a product can be filed under', async () => {
    await create(null);
    http.expectOne('/api/v1/admin/product-categories').flush([
      { id: 'c1', name: 'Enterprise resource planning', slug: 'erp', productCount: 0 },
      { id: 'c2', name: 'Hospital and clinic', slug: 'healthcare', productCount: 0 },
    ]);
    await fixture.whenStable();

    const options = element().querySelectorAll('#product-category option');
    expect(options.length).toBe(2);
    expect(options[1].textContent).toContain('Hospital and clinic');
  });

  it('shows the address the server suggests when the one asked for is taken', async () => {
    await create(null);
    http.expectOne('/api/v1/admin/product-categories').flush([
      { id: 'c1', name: 'Enterprise resource planning', slug: 'erp', productCount: 0 },
    ]);
    await fixture.whenStable();

    (element().querySelector('#product-name') as HTMLInputElement).value = 'Ledger';
    element().querySelector('form')?.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/admin/products').flush(
      {
        title: 'Slug is taken',
        detail: 'That address is already in use. Try "ledger-2".',
        status: 409,
        code: 'SLUG_TAKEN',
        suggestedSlug: 'ledger-2',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="product-error"]')?.textContent).toContain('ledger-2');
  });

  it('says exactly what is still missing before a draft can be published', async () => {
    await create('product-1');
    http.expectOne('/api/v1/admin/products/product-1').flush(product());
    await fixture.whenStable();

    const readiness = element().querySelector('[data-testid="readiness"]')?.textContent ?? '';
    expect(readiness).toContain('3 more features');
    expect(readiness).toContain('a screenshot');
    expect(readiness).toContain('a pricing plan');
  });

  it('says a product is ready once it clears every threshold', async () => {
    await create('product-1');
    http.expectOne('/api/v1/admin/products/product-1').flush(
      product({
        features: [
          { id: 'f1', name: 'One', description: null, groupName: null, sortOrder: 1, isHighlighted: false },
          { id: 'f2', name: 'Two', description: null, groupName: null, sortOrder: 2, isHighlighted: false },
          { id: 'f3', name: 'Three', description: null, groupName: null, sortOrder: 3, isHighlighted: false },
        ],
        screenshots: [{ id: 's1', caption: null, sortOrder: 1 }],
        plans: [
          {
            id: 'p1',
            name: 'Starter',
            price: 999,
            currency: 'INR',
            billingPeriod: 'Monthly',
            includedSeats: 5,
            isFreeTier: false,
            isRecommended: false,
            isPublished: true,
            sortOrder: 1,
          },
        ],
      }),
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="readiness"]')?.textContent).toContain(
      'everything it needs to be published',
    );
  });

  it('reports a duplicate feature in the words the server used', async () => {
    await create('product-1');
    http.expectOne('/api/v1/admin/products/product-1').flush(product());
    await fixture.whenStable();

    element().querySelector('[data-testid="feature-form"]')?.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/admin/products/product-1/features').flush(
      {
        title: 'That feature is already listed',
        detail: '"GST invoicing" is already a feature of this product.',
        status: 409,
        code: 'DUPLICATE_FEATURE',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="feature-error"]')?.textContent).toContain('already a feature');
  });
});
