import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { InvoiceDetail } from './invoice-detail';

describe('InvoiceDetail', () => {
  let fixture: ComponentFixture<InvoiceDetail>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const invoice = (overrides: Record<string, unknown> = {}) => ({
    id: 'inv-1',
    invoiceNumber: 'INV/2026-27/00003',
    subscriptionId: 'sub-1',
    organisationId: 'org-1',
    organisation: 'Northwind',
    gstin: '29ABCDE1234F1Z5',
    status: 'Issued',
    description: 'Northwind — monthly subscription',
    currency: 'INR',
    taxRatePercent: 18,
    subTotal: 6000,
    taxTotal: 1080,
    grandTotal: 7080,
    amountPaid: 0,
    outstanding: 7080,
    issuedOn: '2026-09-01',
    dueDate: '2026-09-15',
    periodStart: '2026-09-01',
    periodEnd: '2026-10-01',
    notes: null,
    payments: [],
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [InvoiceDetail],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'inv-1' })) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InvoiceDetail);
    http = TestBed.inject(HttpTestingController);
  };

  const answer = async (body = invoice()) => {
    http.expectOne('/api/v1/invoices/inv-1').flush(body);
    await fixture.whenStable();
  };

  const type = async (selector: string, value: string) => {
    const input = element().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('shows the number, the customer, the period and the figures the server computed', async () => {
    await create();
    await answer();

    const text = element().textContent ?? '';

    expect(text).toContain('INV/2026-27/00003');
    expect(text).toContain('Northwind');
    expect(text).toContain('29ABCDE1234F1Z5');

    expect(element().querySelector('[data-testid="invoice-total"]')?.textContent).toContain('7,080');
    expect(element().querySelector('[data-testid="invoice-outstanding"]')?.textContent).toContain('7,080');
  });

  it('says how late an unpaid invoice is, in words', async () => {
    await create();

    // Due a fortnight ago and still unpaid.
    const dueDate = new Date(Date.now() - 14 * 86_400_000).toISOString().slice(0, 10);
    await answer(invoice({ dueDate, status: 'Overdue' }));

    // "14d late" rather than a red tint alone: somebody who cannot tell the colours apart still
    // has to know (NFR-ACC-03).
    expect(element().querySelector('[data-testid="invoice-due"]')?.textContent).toContain('late');
  });

  it('fills the amount with what is outstanding when asked to pay in full', async () => {
    await create();
    await answer();

    (element().querySelector('[data-testid="pay-in-full"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    // The commonest entry by far, and typing 7080 out is where a digit goes missing.
    expect((element().querySelector('#payment-amount') as HTMLInputElement).value).toBe('7080');
  });

  it('refuses a payment with no bank reference without troubling the server', async () => {
    await create();
    await answer();

    await type('#payment-amount', '1000');

    (element().querySelector('[data-testid="record-payment"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="payment-error"]')?.textContent).toContain('bank reference');
    http.expectNone((r) => r.method === 'POST');
  });

  it('refuses an overpayment before sending it, and the server still refuses one too', async () => {
    await create();
    await answer();

    await type('#payment-amount', '20000');
    await type('#payment-reference', 'UTR-123');

    (element().querySelector('[data-testid="record-payment"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="payment-error"]')?.textContent).toContain('more than the outstanding');
    http.expectNone((r) => r.method === 'POST');
  });

  it('repeats the server’s words when it refuses a payment', async () => {
    await create();
    await answer();

    await type('#payment-amount', '1000');
    await type('#payment-reference', 'UTR-123');

    (element().querySelector('[data-testid="record-payment"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/invoices/inv-1/payments').flush(
      { detail: 'Reference UTR-123 is already against this invoice.', code: 'PAYMENT_ALREADY_RECORDED' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="payment-error"]')?.textContent).toContain('already against this invoice');
  });

  it('reloads the invoice after a payment rather than guessing at the new balance', async () => {
    await create();
    await answer();

    await type('#payment-amount', '1000');
    await type('#payment-reference', 'UTR-456');

    (element().querySelector('[data-testid="record-payment"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST').flush({ invoiceNumber: 'INV/2026-27/00003', outstanding: 6080 });
    await fixture.whenStable();

    // The server decides what is outstanding and whether the invoice is now partly paid.
    http.expectOne('/api/v1/invoices/inv-1').flush(
      invoice({ amountPaid: 1000, outstanding: 6080, status: 'PartiallyPaid' }),
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="invoice-status"]')?.textContent).toContain('PartiallyPaid');
    expect(element().querySelector('[data-testid="invoice-outstanding"]')?.textContent).toContain('6,080');
  });

  it('hides the payment form once nothing is outstanding', async () => {
    await create();
    await answer(invoice({ amountPaid: 7080, outstanding: 0, status: 'Paid' }));

    expect(element().querySelector('[data-testid="payment-form"]')).toBeNull();
    expect(element().querySelector('[data-testid="invoice-settled"]')).not.toBeNull();
  });

  it('lists the payments already received with their bank references', async () => {
    await create();
    await answer(
      invoice({
        amountPaid: 1000,
        outstanding: 6080,
        status: 'PartiallyPaid',
        payments: [
          { id: 'p1', amount: 1000, mode: 'NeftRtgs', referenceNumber: 'UTR-456', receivedOn: '2026-09-05', isRefund: false, notes: null },
        ],
      }),
    );

    const row = element().querySelector('[data-testid="payment-p1"]');

    expect(row?.textContent).toContain('UTR-456');
    expect(row?.textContent).toContain('1,000');
  });

  it('says so plainly when the invoice is not there', async () => {
    await create();
    http.expectOne('/api/v1/invoices/inv-1').flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="invoice-missing"]')).not.toBeNull();
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    await answer(
      invoice({
        amountPaid: 1000,
        outstanding: 6080,
        status: 'PartiallyPaid',
        payments: [
          { id: 'p1', amount: 1000, mode: 'Upi', referenceNumber: 'UPI-1', receivedOn: '2026-09-05', isRefund: false, notes: null },
        ],
      }),
    );

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  }, AxeTimeout);
});
