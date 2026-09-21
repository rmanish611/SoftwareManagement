import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { LeadInbox } from './lead-inbox';

describe('LeadInbox', () => {
  let fixture: ComponentFixture<LeadInbox>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const lead = (name: string, overrides: Record<string, unknown> = {}) => ({
    id: name.toLowerCase().replace(/\s+/g, '-'),
    fullName: name,
    email: `${name.toLowerCase().replace(/\s+/g, '.')}@example.test`,
    phone: null,
    companyName: 'Northwind',
    stage: 'New',
    source: 'Direct',
    product: null,
    createdAtUtc: '2026-09-18T09:00:00Z',
    slaDueAtUtc: '2026-09-19T09:00:00Z',
    firstResponseAtUtc: null,
    isBreached: false,
    minutesToSlaDue: 240,
    isStale: false,
    ...overrides,
  });

  const create = async (query: Record<string, string> = {}) => {
    await TestBed.configureTestingModule({
      imports: [LeadInbox],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(query)) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LeadInbox);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('lists the enquiries in the order the server sent them', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/leads').flush([lead('Waiting Long'), lead('Answered Already')]);
    await fixture.whenStable();

    const names = [...element().querySelectorAll('tbody tr td:first-child a')].map((a) => a.textContent?.trim());

    // Not re-sorted here. The server puts the enquiry the company is currently failing at the top,
    // and a client that re-sorts by arrival would undo exactly that (REQ-LEAD-009).
    expect(names).toEqual(['Waiting Long', 'Answered Already']);
  });

  it('says how long is left to reply, and says "late" in words when it has run out', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/leads').flush([
      lead('On Time', { minutesToSlaDue: 240 }),
      lead('Overdue', { minutesToSlaDue: -2880, isBreached: true }),
    ]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="sla-on-time"]')?.textContent).toContain('4h left');

    // The word, not just a colour: somebody who cannot tell red from grey still has to be able to
    // read which one is late (NFR-ACC-03).
    expect(element().querySelector('[data-testid="sla-overdue"]')?.textContent).toContain('2d late');
  });

  it('flags a conversation that has gone quiet', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/leads').flush([lead('Gone Quiet', { stage: 'Contacted', isStale: true })]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="stale-flag"]')?.textContent).toContain('Stale');
  });

  it('asks the server for the saved view named in the address bar', async () => {
    await create({ view: 'breached' });

    const request = http.expectOne((r) => r.url === '/api/v1/leads');
    expect(request.request.params.get('view')).toBe('breached');
    request.flush([]);
    await fixture.whenStable();

    // And says which view is showing, so a bookmarked link is not ambiguous.
    expect(element().querySelector('[data-testid="view-breached"]')?.getAttribute('aria-current')).toBe('page');
  });

  it('passes a search straight through rather than filtering in the browser', async () => {
    await create({ search: 'northwind' });

    const request = http.expectOne((r) => r.url === '/api/v1/leads');
    expect(request.request.params.get('search')).toBe('northwind');
    request.flush([]);
    await fixture.whenStable();
  });

  it('says what to try next when a view is empty rather than showing a blank table', async () => {
    await create({ view: 'stale' });
    http.expectOne((r) => r.url === '/api/v1/leads').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="leads-empty"]')?.textContent).toContain('Nothing matches');
  });

  it('keeps the screen usable when the request fails, without showing a status code', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/leads').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const text = element().textContent ?? '';
    expect(element().querySelector('[data-testid="leads-empty"]')).not.toBeNull();
    expect(text).not.toContain('500');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/leads').flush([
      lead('Waiting Long', { isStale: true, stage: 'Contacted' }),
      lead('Overdue', { minutesToSlaDue: -60, isBreached: true }),
    ]);
    await fixture.whenStable();

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  });
});
