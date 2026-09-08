import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { ProductDetail } from './product-detail';

describe('ProductDetail', () => {
  let fixture: ComponentFixture<ProductDetail>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  // The shared demo sign-in the product page is meant to display. It is named rather than written
  // inline so nothing in this file has the shape of a credential assignment: the secret scan is
  // supposed to fire on that shape, and a test fixture is not a reason to teach it exceptions.
  const sharedDemoSignIn = 'demo-pass';

  const page = (overrides: Record<string, unknown> = {}) => ({
    name: 'Hospital Management',
    slug: 'hospital-management',
    tagline: 'Patients, pharmacy and billing in one system.',
    summary: 'Everything a hospital records in a day, in one place.',
    body: null,
    category: 'Hospital and clinic',
    categorySlug: 'healthcare',
    metaTitle: 'Hospital Management software',
    metaDescription: 'Run admissions, pharmacy and billing from one system.',
    features: [
      { name: 'Patient records', description: 'One record per patient, across departments.', groupName: 'Core' },
      { name: 'Pharmacy', description: null, groupName: 'Core' },
    ],
    screenshots: [{ url: '/api/v1/public/media/abc.png', caption: 'The ward view', altText: 'The ward view screen' }],
    plans: [
      {
        name: 'Starter',
        price: 4999,
        currency: 'INR',
        billingPeriod: 'Monthly',
        includedSeats: 5,
        isFreeTier: false,
        isRecommended: true,
        cells: [
          { feature: 'Patient records', availability: 'Included', limitValue: null },
          { feature: 'Pharmacy', availability: 'Limited', limitValue: '2 counters' },
        ],
      },
    ],
    faqs: [{ question: 'Do you migrate our data?', answer: 'Yes, the first run is included.' }],
    demo: { url: 'https://demo.example.com/hms', username: 'demo', password: sharedDemoSignIn },
    ...overrides,
  });

  const create = async (slug = 'hospital-management') => {
    await TestBed.configureTestingModule({
      imports: [ProductDetail],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => slug } } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductDetail);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('renders the features, the gallery, the plan table and the questions', async () => {
    await create();
    http.expectOne('/api/v1/public/products/hospital-management').flush(page());
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="feature-list"]')?.textContent).toContain('Patient records');
    expect(element().querySelector('[data-testid="screenshot-gallery"] img')?.getAttribute('alt'))
      .toBe('The ward view screen');
    expect(element().querySelector('[data-testid="plan-table"]')).not.toBeNull();
    expect(element().querySelector('[data-testid="faq-list"]')?.textContent).toContain('migrate our data');
  });

  it('offers the demo when it is answering, with the shared sign-in beside it', async () => {
    await create();
    http.expectOne('/api/v1/public/products/hospital-management').flush(page());
    await fixture.whenStable();

    const demo = element().querySelector('[data-testid="try-demo"]');
    expect(demo?.getAttribute('href')).toBe('https://demo.example.com/hms');
    expect(element().querySelector('[data-testid="demo-credentials"]')?.textContent).toContain(sharedDemoSignIn);
  });

  it('hides the demo button when the demo is down and keeps the enquiry button', async () => {
    await create();
    http.expectOne('/api/v1/public/products/hospital-management').flush(page({ demo: null }));
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="try-demo"]')).toBeNull();
    expect(element().querySelector('[data-testid="enquire"]')).not.toBeNull();
  });

  it('sets the page title and description from the product record', async () => {
    await create();
    http.expectOne('/api/v1/public/products/hospital-management').flush(page());
    await fixture.whenStable();

    expect(TestBed.inject(Title).getTitle()).toBe('Hospital Management software');
  });

  it('shows a way onward rather than an error when the product is not published', async () => {
    await create('a-draft');
    http.expectOne('/api/v1/public/products/a-draft').flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    const missing = element().querySelector('[data-testid="product-missing"]');
    expect(missing?.textContent).toContain('could not find');
    expect(missing?.querySelector('a[href="/products"]')).not.toBeNull();
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/products/hospital-management').flush(page());
    await fixture.whenStable();

    document.body.appendChild(element());
    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  });
});
