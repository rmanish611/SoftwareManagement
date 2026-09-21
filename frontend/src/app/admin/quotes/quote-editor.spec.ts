import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { QuoteEditor } from './quote-editor';

describe('QuoteEditor', () => {
  let fixture: ComponentFixture<QuoteEditor>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const line = (overrides: Record<string, unknown> = {}) => ({
    id: 'line-1',
    productId: 'p1',
    pricingPlanId: null,
    description: 'One year of the ERP, supported',
    quantity: 1,
    unitPrice: 14997,
    discountAmount: 0,
    taxRatePercent: 18,
    lineTotal: 14997,
    taxAmount: 2699.46,
    sortOrder: 1,
    ...overrides,
  });

  const quote = (overrides: Record<string, unknown> = {}) => ({
    id: 'quote-1',
    quoteNumber: 'Q/2026-27/00007',
    organisationId: 'org-1',
    organisation: 'Northwind',
    contactId: 'c-1',
    contactName: 'Priya Sharma',
    contactEmail: 'priya@northwind.test',
    status: 'Draft',
    currency: 'INR',
    subTotal: 14997,
    discountTotal: 0,
    taxTotal: 2699.46,
    grandTotal: 17696.46,
    issuedOn: null,
    validUntil: null,
    notes: null,
    rejectReason: null,
    revisionOfQuoteId: null,
    isEditable: true,
    lines: [line()],
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteEditor],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'quote-1' })) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuoteEditor);
    http = TestBed.inject(HttpTestingController);
  };

  const answer = async (body = quote()) => {
    http.expectOne('/api/v1/quotes/quote-1').flush(body);
    await fixture.whenStable();
  };

  const type = async (selector: string, value: string) => {
    const input = element().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('shows the quote number, the customer and the totals the server computed', async () => {
    await create();
    await answer();

    const text = element().textContent ?? '';

    expect(text).toContain('Q/2026-27/00007');
    expect(text).toContain('Northwind');

    // The server's totals, not a sum done again here: a screen that recomputes them is how it
    // comes to show a figure the invoice will not.
    expect(element().querySelector('[data-testid="grand-total"]')?.textContent).toContain('17,696.46');
  });

  it('groups rupees the way an Indian reader expects', async () => {
    await create();
    await answer(quote({ grandTotal: 1234567.89 }));

    // 12,34,567.89 rather than 1,234,567.89.
    expect(element().querySelector('[data-testid="grand-total"]')?.textContent).toContain('12,34,567.89');
  });

  it('warns about a discount that will need approval before the request is sent', async () => {
    await create();
    await answer();

    await type('#line-quantity', '1');
    await type('#line-price', '10000');
    await type('#line-discount', '2000');

    // The server decides, but a screen that lets somebody type a figure it knows will be refused
    // wastes their time (BR-SALE-04).
    expect(element().querySelector('[data-testid="discount-warning"]')).not.toBeNull();

    await type('#line-discount', '1000');
    expect(element().querySelector('[data-testid="discount-warning"]')).toBeNull();
  });

  it('repeats the server’s refusal when a discount is sent anyway', async () => {
    await create();
    await answer();

    await type('#line-description', 'Discounted year');
    await type('#line-price', '10000');
    await type('#line-discount', '5000');

    (element().querySelector('[data-testid="add-line"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/quotes/quote-1/lines').flush(
      { detail: "A discount above 15% of the line needs the owner's approval.", code: 'APPROVAL_REQUIRED' },
      { status: 403, statusText: 'Forbidden' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="quote-error"]')?.textContent).toContain("owner's approval");
  });

  it('refuses a line with no description without troubling the server', async () => {
    await create();
    await answer();

    (element().querySelector('[data-testid="add-line"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="quote-error"]')?.textContent).toContain('description');
    http.expectNone((r) => r.method === 'POST');
  });

  it('hides the editing form on a sent quote and offers a revision instead', async () => {
    await create();
    await answer(quote({ status: 'Sent', isEditable: false, issuedOn: '2026-09-20', validUntil: '2026-10-05' }));

    expect(element().querySelector('[data-testid="add-line"]')).toBeNull();
    expect(element().querySelector('[data-testid="quote-locked"]')?.textContent).toContain('Revise');
    expect(element().querySelector('[data-testid="revise-quote"]')).not.toBeNull();
  });

  it('offers no revision on an accepted quote, because that is a deal rather than an offer', async () => {
    await create();
    await answer(quote({ status: 'Accepted', isEditable: false }));

    expect(element().querySelector('[data-testid="revise-quote"]')).toBeNull();
  });

  it('shows the remove button only while the quote is a draft', async () => {
    await create();
    await answer();

    expect(element().querySelector('[data-testid="remove-line-1"]')).not.toBeNull();
  });

  it('says so plainly when the quote is not there', async () => {
    await create();
    http.expectOne('/api/v1/quotes/quote-1').flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="quote-missing"]')).not.toBeNull();
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    await answer(quote({ lines: [line(), line({ id: 'line-2', description: 'Onboarding', discountAmount: 500 })] }));

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  }, AxeTimeout);
});
