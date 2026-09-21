import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { CaseStudy } from './case-study';

describe('CaseStudy', () => {
  let fixture: ComponentFixture<CaseStudy>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const study = (overrides: Record<string, unknown> = {}) => ({
    title: 'Ward Board',
    slug: 'ward-board',
    industry: 'Healthcare',
    summary: 'One board for every ward, replacing three whiteboards and a spreadsheet.',
    client: 'a leading healthcare company',
    startedOn: '2025-02-01',
    completedOn: '2025-11-01',
    product: 'Clinic',
    problem: 'Three systems that did not talk to each other.',
    approach: 'One schema, one import, and a month of double-running.',
    outcome: 'The month-end close went from nine days to two.',
    metrics: [{ label: 'Discharge round', value: 40, unit: 'percent faster' }],
    testimonial: null,
    ...overrides,
  });

  const create = async (slug = 'ward-board') => {
    await TestBed.configureTestingModule({
      imports: [CaseStudy],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ slug }) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CaseStudy);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => {
    http.verify();
    document.head.querySelector('[data-testid="case-study-jsonld"]')?.remove();
  });

  it('renders the whole narrative and the measured outcome in one page', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/ward-board').flush(study());
    await fixture.whenStable();
    fixture.detectChanges();

    const text = element().textContent ?? '';
    expect(text).toContain('Three systems that did not talk to each other.');
    expect(text).toContain('One schema, one import');
    expect(text).toContain('The month-end close went from nine days to two.');
    expect(element().querySelector('[data-testid="case-study-metrics"]')?.textContent).toContain('percent faster');
  });

  it('shows the client label the server decided on rather than deciding for itself', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/ward-board').flush(study({ client: 'a leading healthcare company' }));
    await fixture.whenStable();
    fixture.detectChanges();

    // The permission rule lives on the server. This page renders the answer it was given
    // (BR-PRJ-01).
    expect(element().querySelector('[data-testid="case-study-client"]')?.textContent?.trim()).toBe(
      'a leading healthcare company',
    );
  });

  it('emits JSON-LD that parses and carries the headline and the date', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/ward-board').flush(study());
    await fixture.whenStable();
    fixture.detectChanges();

    const script = document.head.querySelector('[data-testid="case-study-jsonld"]');
    expect(script).not.toBeNull();

    const data = JSON.parse(script?.textContent ?? '{}') as Record<string, unknown>;
    expect(data['@type']).toBe('Article');
    expect(data['headline']).toBe('Ward Board');
    expect(data['datePublished']).toBe('2025-11-01');
    expect(data['description']).toContain('One board for every ward');
  });

  it('renders a permitted quote with the name and the role behind it', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/ward-board').flush(
      study({
        testimonial: {
          quote: 'They understood the problem before writing anything.',
          authorName: 'Priya Sharma',
          authorRole: 'Operations Director',
          organisationName: 'Northwind Hospitals',
        },
      }),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const quote = element().querySelector('[data-testid="case-study-testimonial"]');
    expect(quote?.textContent).toContain('Priya Sharma');
    expect(quote?.textContent).toContain('Operations Director');
  });

  it('answers a draft or unknown address with the same page and a way onwards', async () => {
    await create('not-published');
    http.expectOne('/api/v1/public/projects/not-published')
      .flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().querySelector('[data-testid="case-study-missing"]')).not.toBeNull();
    expect(element().querySelector('a[href="/projects"]')).not.toBeNull();
  });

  it('has no serious accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/ward-board').flush(study());
    await fixture.whenStable();
    fixture.detectChanges();

    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  }, AxeTimeout);
});
