import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { OrganisationList } from './organisation-list';

describe('OrganisationList', () => {
  let fixture: ComponentFixture<OrganisationList>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const organisation = (name: string, overrides: Record<string, unknown> = {}) => ({
    id: name.toLowerCase().replace(/\s+/g, '-'),
    legalName: `${name} Private Limited`,
    displayName: name,
    gstin: '29ABCDE1234F1Z5',
    city: 'Bengaluru',
    state: 'Karnataka',
    status: 'Prospect',
    contactCount: 2,
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [OrganisationList],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(OrganisationList);
    http = TestBed.inject(HttpTestingController);
  };

  const type = async (selector: string, value: string) => {
    const input = element().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('lists the customers with their GSTIN and where they are', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([organisation('Northwind')]);
    await fixture.whenStable();

    const text = element().textContent ?? '';

    expect(text).toContain('Northwind');
    expect(text).toContain('29ABCDE1234F1Z5');
    expect(text).toContain('Bengaluru');
  });

  it('shows the server’s GSTIN refusal against the GSTIN field itself', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    await type('#legal-name', 'Acme Private Limited');
    await type('#gstin', '29ABCDE1234F1Z');

    (element().querySelector('[data-testid="add-customer"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/organisations').flush(
      {
        detail: 'A GSTIN is 15 characters: two digits of state code, a ten-character PAN, an entity digit, the letter Z, and a check character.',
        code: 'GSTIN_INVALID',
        field: 'gstin',
      },
      { status: 422, statusText: 'Unprocessable Content' },
    );
    await fixture.whenStable();

    // Against the field, not as a banner: the person is looking at the box they just typed in.
    const error = element().querySelector('[data-testid="gstin-error"]');
    expect(error?.textContent).toContain('15 characters');

    expect(element().querySelector('#gstin')?.getAttribute('aria-invalid')).toBe('true');
    expect(element().querySelector('#gstin')?.getAttribute('aria-describedby')).toBe('gstin-error');
  });

  it('repeats the server’s words when the number is already on file', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    await type('#legal-name', 'Second Acme');
    await type('#gstin', '29ABCDE1234F1Z5');

    (element().querySelector('[data-testid="add-customer"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST').flush(
      { detail: 'Northwind already uses that number.', code: 'GSTIN_DUPLICATE', field: 'gstin' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    // Naming the company saves the person going to look for it.
    expect(element().querySelector('[data-testid="gstin-error"]')?.textContent).toContain('Northwind');
  });

  it('refuses a customer with no name without troubling the server', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    (element().querySelector('[data-testid="add-customer"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="customer-error"]')?.textContent).toContain('needs a name');
    http.expectNone((r) => r.method === 'POST');
  });

  it('reloads the list after a customer is added, rather than guessing at the new row', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    await type('#legal-name', 'Fabrikam');

    (element().querySelector('[data-testid="add-customer"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne((r) => r.method === 'POST').flush(organisation('Fabrikam'));
    await fixture.whenStable();

    // The server decides the display name and the status, so the screen asks it rather than
    // inventing a row that might differ.
    http.expectOne((r) => r.method === 'GET').flush([organisation('Fabrikam')]);
    await fixture.whenStable();

    expect(element().textContent).toContain('Fabrikam');
  });

  it('passes a search to the server instead of filtering in the browser', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    await type('#customer-search', 'northwind');

    (element().querySelector('[data-testid="customer-search-submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    const request = http.expectOne((r) => r.url === '/api/v1/organisations');
    expect(request.request.params.get('search')).toBe('northwind');
    request.flush([]);
    await fixture.whenStable();
  });

  it('says what the screen is for when there is nothing on it', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="customers-empty"]')?.textContent).toContain('converted lead');
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne((r) => r.url === '/api/v1/organisations').flush([organisation('Northwind')]);
    await fixture.whenStable();

    const violations = await findSeriousAccessibilityViolations(element());

    expect(violations, describeViolations(violations)).toHaveLength(0);
  }, AxeTimeout);
});
