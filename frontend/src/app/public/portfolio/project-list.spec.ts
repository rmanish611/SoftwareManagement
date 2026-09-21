import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { ProjectList } from './project-list';

describe('ProjectList', () => {
  let fixture: ComponentFixture<ProjectList>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const card = (title: string, overrides: Record<string, unknown> = {}) => ({
    title,
    slug: title.toLowerCase().replace(/\s+/g, '-'),
    industry: 'Healthcare',
    summary: `${title} replaced three systems with one.`,
    client: 'a leading healthcare company',
    startedOn: '2025-02-01',
    completedOn: '2025-11-01',
    isFeatured: false,
    ...overrides,
  });

  const create = async (query: Record<string, string> = {}) => {
    await TestBed.configureTestingModule({
      imports: [ProjectList],
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

    fixture = TestBed.createComponent(ProjectList);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('lists published work with the label the client agreed to', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/industries').flush(['Healthcare', 'Education']);
    http.expectOne('/api/v1/public/projects').flush([card('Ward Board'), card('Fee Ledger')]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().querySelector('[data-testid="project-ward-board"]')).not.toBeNull();
    expect(element().querySelector('[data-testid="client-ward-board"]')?.textContent).toContain(
      'a leading healthcare company',
    );
  });

  it('asks the API for one industry when the query string names one', async () => {
    await create({ industry: 'Healthcare' });
    http.expectOne('/api/v1/public/projects/industries').flush(['Healthcare']);

    // The filter is in the query string rather than in component state, so a filtered view is a
    // link somebody can send (REQ-PRJ-004).
    const request = http.expectOne((r) => r.url === '/api/v1/public/projects');
    expect(request.request.params.get('industry')).toBe('Healthcare');
    request.flush([card('Ward Board')]);
    await fixture.whenStable();
  });

  it('shows an empty state with a way to make contact when nothing matches', async () => {
    await create({ industry: 'Speleology' });
    http.expectOne('/api/v1/public/projects/industries').flush([]);
    http.expectOne((r) => r.url === '/api/v1/public/projects').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const empty = element().querySelector('[data-testid="projects-empty"]');
    expect(empty).not.toBeNull();
    expect(empty?.querySelector('a[href="/contact"]')).not.toBeNull();
  });

  it('shows the same invitation when the request fails as when it returns nothing', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/industries').flush([], { status: 500, statusText: 'Server Error' });
    http.expectOne((r) => r.url === '/api/v1/public/projects')
      .flush([], { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    // A visitor cannot act on a status code, and a blank page reads as a broken site.
    expect(element().querySelector('[data-testid="projects-empty"]')).not.toBeNull();
  });

  it('has no serious accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/projects/industries').flush(['Healthcare']);
    http.expectOne((r) => r.url === '/api/v1/public/projects').flush([card('Ward Board')]);
    await fixture.whenStable();
    fixture.detectChanges();

    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  }, AxeTimeout);
});
