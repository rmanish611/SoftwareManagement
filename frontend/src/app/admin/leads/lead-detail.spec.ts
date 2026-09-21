import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { LeadDetail } from './lead-detail';

describe('LeadDetail', () => {
  let fixture: ComponentFixture<LeadDetail>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const activity = (overrides: Record<string, unknown> = {}) => ({
    id: 'a1',
    activityType: 'Note',
    direction: 'Internal',
    body: 'Rang them, no answer.',
    occurredAtUtc: '2026-09-18T10:00:00Z',
    fromStage: null,
    toStage: null,
    dueAtUtc: null,
    isCompleted: false,
    actor: 'sales@softwaremanagement.test',
    ...overrides,
  });

  const detail = (overrides: Record<string, unknown> = {}) => ({
    id: 'lead-1',
    fullName: 'Priya Sharma',
    email: 'priya@northwind.test',
    phone: null,
    companyName: 'Northwind',
    message: 'We run four clinics and need one system.',
    stage: 'New',
    source: 'Direct',
    product: null,
    createdAtUtc: '2026-09-18T09:00:00Z',
    slaDueAtUtc: '2026-09-19T09:00:00Z',
    firstResponseAtUtc: null,
    firstResponseBusinessMinutes: null,
    disqualifyReason: null,
    organisationId: null,
    organisationName: null,
    timeline: [activity()],
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [LeadDetail],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'lead-1' })) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LeadDetail);
    http = TestBed.inject(HttpTestingController);
  };

  /** The two calls the screen makes on arrival. */
  const answer = async (lead = detail(), duplicates: Record<string, unknown>[] = []) => {
    http.expectOne('/api/v1/leads/lead-1').flush(lead);
    http.expectOne('/api/v1/leads/lead-1/duplicates').flush(duplicates);
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('shows who wrote in, what they asked and where the enquiry stands', async () => {
    await create();
    await answer();

    const text = element().textContent ?? '';

    expect(text).toContain('Priya Sharma');
    expect(element().querySelector('[data-testid="lead-message"]')?.textContent).toContain('four clinics');
    expect(element().querySelector('[data-testid="lead-stage"]')?.textContent).toContain('New');
  });

  it('reports a first reply in working time rather than in hours on the wall', async () => {
    await create();
    await answer(detail({ firstResponseAtUtc: '2026-09-19T10:00:00Z', firstResponseBusinessMinutes: 60 }));

    // An enquiry that arrived on Saturday night and was answered on Monday morning took one
    // working hour, and a screen that said thirty-four would be reporting the weekend (BR-LEAD-10).
    expect(element().querySelector('[data-testid="response-time"]')?.textContent).toContain('working time');
  });

  it('renders the history as a narrative, naming who did each thing', async () => {
    await create();
    await answer(
      detail({
        timeline: [
          activity({ id: 'a2', activityType: 'StageChange', fromStage: 'New', toStage: 'Contacted', body: null }),
          activity(),
        ],
      }),
    );

    const timeline = element().querySelector('[data-testid="lead-timeline"]');

    expect(timeline?.textContent).toContain('Moved from New to Contacted');
    expect(timeline?.textContent).toContain('sales@softwaremanagement.test');
  });

  it('warns before disqualifying that a reason is needed and how long it must be', async () => {
    await create();
    await answer();

    const select = element().querySelector('#stage') as HTMLSelectElement;
    select.value = 'Disqualified';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    // Asked for in the same submit as the stage. A screen that discovers the rule after the fact
    // makes the person type the reason twice.
    expect(element().querySelector('[data-testid="reason-hint"]')?.textContent).toContain('10 characters');
  });

  it('repeats the server’s own words when a move is refused', async () => {
    await create();
    await answer();

    (element().querySelector('[data-testid="change-stage"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne('/api/v1/leads/lead-1/stage').flush(
      { detail: 'Say why in at least 10 characters.', code: 'STAGE_NOT_ALLOWED' },
      { status: 422, statusText: 'Unprocessable Content' },
    );
    await fixture.whenStable();

    // The server knows whether the reason was too short or the move was not allowed. A generic
    // "something went wrong" throws that away.
    expect(element().querySelector('[data-testid="lead-error"]')?.textContent).toContain('10 characters');
  });

  it('refuses to send an empty note without troubling the server', async () => {
    await create();
    await answer();

    (element().querySelector('[data-testid="log-activity"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="lead-error"]')?.textContent).toContain('empty note');
    http.expectNone('/api/v1/leads/lead-1/activities');
  });

  it('offers possible duplicates with a reason, and merges nothing on its own', async () => {
    await create();
    await answer(detail(), [
      {
        id: 'lead-2',
        fullName: 'Sunita Rao',
        email: 'sunita@northwind.test',
        companyName: 'Northwind',
        stage: 'New',
        createdAtUtc: '2026-09-17T09:00:00Z',
        reason: 'Another address at northwind.test.',
      },
    ]);

    const panel = element().querySelector('[data-testid="duplicate-suggestions"]');

    expect(panel?.textContent).toContain('Sunita Rao');
    expect(panel?.textContent).toContain('northwind.test');
    expect(panel?.textContent).toContain('Nothing is merged until you say so');
  });

  it('says so plainly when the enquiry is not there', async () => {
    await create();
    http.expectOne('/api/v1/leads/lead-1').flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/v1/leads/lead-1/duplicates').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="lead-missing"]')?.textContent).toContain('merged');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    await answer(
      detail({
        timeline: [
          activity({ id: 'a3', dueAtUtc: '2026-09-20T09:00:00Z' }),
          activity({ id: 'a4', activityType: 'StageChange', fromStage: 'New', toStage: 'Contacted', body: null }),
        ],
      }),
    );

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  });
});
