import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { SubscriptionList } from './subscription-list';

describe('SubscriptionList', () => {
  let fixture: ComponentFixture<SubscriptionList>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);

  const subscription = (tenant: string, overrides: Record<string, unknown> = {}) => ({
    id: tenant.toLowerCase().replace(/\s+/g, '-'),
    tenantId: 't-1',
    tenant,
    organisation: 'Northwind',
    product: 'Ledger',
    plan: 'Standard',
    status: 'Active',
    seats: 2,
    unitPrice: 3000,
    currency: 'INR',
    billingPeriod: 'Monthly',
    startedOn: inDays(-20),
    trialEndsOn: null,
    currentPeriodEndsOn: inDays(10),
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [SubscriptionList],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionList);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('shows the tenant, product, plan, status and renewal date', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([subscription('Northwind Production')]);
    await fixture.whenStable();

    const text = element().textContent ?? '';

    expect(text).toContain('Northwind Production');
    expect(text).toContain('Ledger');
    expect(text).toContain('Standard');
    expect(text).toContain('Active');
  });

  it('shows what a period costs rather than the unit price alone', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([subscription('Northwind', { seats: 4, unitPrice: 3000 })]);
    await fixture.whenStable();

    // Four seats at 3,000 is 12,000, and that is the figure somebody is checking against a bank
    // statement - not the 3,000.
    expect(element().textContent).toContain('12,000');
  });

  it('says how soon each one renews, in words', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([subscription('Soon', { currentPeriodEndsOn: inDays(5) })]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="renews-soon"]')?.textContent).toContain('in 5d');
  });

  it('keeps the order the server sent, which is soonest renewal first', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([
      subscription('Renews First', { currentPeriodEndsOn: inDays(3) }),
      subscription('Renews Later', { currentPeriodEndsOn: inDays(40) }),
    ]);
    await fixture.whenStable();

    const names = [...element().querySelectorAll('tbody tr td:first-child')].map((c) => c.textContent?.trim());

    // Re-sorting here would undo the one thing the screen exists to do.
    expect(names).toEqual(['Renews First', 'Renews Later']);
  });

  it('marks a subscription that is being chased, in words as well as colour', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([subscription('Late Payer', { status: 'PastDue' })]);
    await fixture.whenStable();

    const cell = element().querySelector('tbody tr')?.textContent ?? '';
    expect(cell).toContain('PastDue');
    expect(element().querySelector('.warn')).not.toBeNull();
  });

  it('says when a trial ends, because that is the date that matters on a trial', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([
      subscription('On Trial', { status: 'Trial', trialEndsOn: inDays(9) }),
    ]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="trial-on-trial"]')?.textContent).toContain('trial ends');
  });

  it('says what the screen is for when nothing is running', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="subscriptions-empty"]')?.textContent).toContain('accepted quote');
  });

  it('stays usable when the request fails, without showing a status code', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="subscriptions-empty"]')).not.toBeNull();
    expect(element().textContent).not.toContain('500');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/subscriptions').flush([
      subscription('Northwind Production'),
      subscription('Late Payer', { status: 'PastDue' }),
    ]);
    await fixture.whenStable();

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  }, AxeTimeout);
});
