import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { EnquiryForm } from './enquiry-form';

describe('EnquiryForm', () => {
  let fixture: ComponentFixture<EnquiryForm>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const definition = (overrides: Record<string, unknown> = {}) => ({
    key: 'contact',
    title: 'Talk to us',
    intro: 'Tell us what you are trying to run better.',
    submitLabel: 'Send enquiry',
    consentText: 'I agree that Software Management may store these details and contact me.',
    consentVersion: 1,
    isEnabled: true,
    honeypotField: 'website',
    fields: [
      { name: 'fullName', label: 'Your name', fieldType: 'Text', isRequired: true, maxLength: 150, options: [] },
      { name: 'email', label: 'Email', fieldType: 'Email', isRequired: false, maxLength: 256, options: [] },
      { name: 'message', label: 'What do you need?', fieldType: 'TextArea', isRequired: false, maxLength: 4000, options: [] },
    ],
    products: [],
    ...overrides,
  });

  const create = async (key = 'contact', query: Record<string, string> = {}) => {
    await TestBed.configureTestingModule({
      imports: [EnquiryForm],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EnquiryForm);
    fixture.componentRef.setInput('formKey', key);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('renders exactly the fields the server says the form asks', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    expect(element().querySelector('#field-fullName')).not.toBeNull();
    expect(element().querySelector('#field-email')?.getAttribute('type')).toBe('email');
    expect(element().querySelector('textarea#field-message')).not.toBeNull();
    expect(element().querySelector('label[for="field-fullName"]')?.textContent).toContain('required');
  });

  it('shows the consent text itself and leaves the box unticked', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    const box = element().querySelector<HTMLInputElement>('#consent');

    // A pre-ticked box is an assumption, not consent.
    expect(box?.checked).toBe(false);
    expect(element().querySelector('[data-testid="consent-text"]')?.textContent).toContain('may store these details');
  });

  it('keeps the hidden field out of the tab order and out of the accessibility tree', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    const honeypot = element().querySelector('#field-website');
    expect(honeypot).not.toBeNull();
    expect(honeypot?.getAttribute('tabindex')).toBe('-1');
    expect(honeypot?.closest('[aria-hidden="true"]')).not.toBeNull();
  });

  it('sends the answers, the consent and the campaign parameters from the address bar', async () => {
    await create('contact', { utm_source: 'google', utm_medium: 'cpc' });
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    const name = element().querySelector<HTMLInputElement>('#field-fullName')!;
    name.value = 'Anita';
    name.dispatchEvent(new Event('input'));

    element().querySelector<HTMLInputElement>('#consent')!.click();
    await fixture.whenStable();

    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const request = http.expectOne('/api/v1/public/forms/contact/submit');
    expect(request.request.body.answers.fullName).toBe('Anita');
    expect(request.request.body.consent).toBe(true);
    expect(request.request.body.utm.utm_source).toBe('google');
    request.flush({ reference: 'ENQ-ABC12345', message: 'Thank you.' });
    await fixture.whenStable();
  });

  it('pre-selects the product when the page was opened from a product', async () => {
    await create('request-demo', { product: 'hospital-management' });
    http.expectOne('/api/v1/public/forms/request-demo').flush(
      definition({
        key: 'request-demo',
        fields: [
          { name: 'fullName', label: 'Your name', fieldType: 'Text', isRequired: true, maxLength: 150, options: [] },
          { name: 'product', label: 'Which product?', fieldType: 'ProductPicker', isRequired: false, maxLength: null, options: [] },
        ],
        products: [
          { slug: 'hospital-management', name: 'Hospital Management' },
          { slug: 'ledger', name: 'Ledger' },
        ],
      }),
    );
    await fixture.whenStable();

    const picker = element().querySelector<HTMLSelectElement>('#field-product');
    expect(picker?.value).toBe('hospital-management');
  });

  it('replaces the form with the reference once the enquiry is accepted', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/public/forms/contact/submit').flush({
      reference: 'ENQ-ABC12345',
      message: 'Thank you. We will reply within one working day.',
    });
    await fixture.whenStable();

    // A cleared form looks like nothing happened. The reference is the proof.
    expect(element().querySelector('[data-testid="enquiry-reference"]')?.textContent).toBe('ENQ-ABC12345');
    expect(element().querySelector('form')).toBeNull();
  });

  it('says what to do when the connection has sent too many enquiries', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/public/forms/contact/submit').flush(
      { title: 'Too many', detail: 'Slow down.', status: 429, code: 'RATE_LIMITED' },
      { status: 429, statusText: 'Too Many Requests' },
    );
    await fixture.whenStable();

    const message = element().querySelector('[data-testid="enquiry-error"]')?.textContent ?? '';
    expect(message).toContain('wait a few minutes');
    expect(message).not.toContain('429');
  });

  it('points at the consent box when that is what was missing', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/public/forms/contact/submit').flush(
      { title: 'Consent is needed', detail: 'Tick the box.', status: 422, code: 'CONSENT_REQUIRED' },
      { status: 422, statusText: 'Unprocessable Content' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="consent-error"]')?.textContent).toContain('tick the box');
    expect(element().querySelector('#consent')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('names the field the server rejected so the person knows where to look', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/public/forms/contact/submit').flush(
      { title: 'Something is missing', detail: 'Your name is needed.', status: 422, code: 'INVALID_SUBMISSION', field: 'fullName' },
      { status: 422, statusText: 'Unprocessable Content' },
    );
    await fixture.whenStable();

    expect(element().querySelector('#field-fullName')?.getAttribute('aria-invalid')).toBe('true');
    expect(element().querySelector('[data-testid="enquiry-error"]')?.textContent).toContain('Your name is needed');
  });

  it('says a closed form is closed rather than offering a button that cannot work', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition({ isEnabled: false }));
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="form-closed"]')).not.toBeNull();
    expect(element().querySelector('form')).toBeNull();
  });

  it('has no serious or critical accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/forms/contact').flush(definition());
    await fixture.whenStable();

    document.body.appendChild(element());
    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  }, AxeTimeout);
});
