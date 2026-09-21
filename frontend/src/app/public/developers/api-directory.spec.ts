import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AxeTimeout, describeViolations, findSeriousAccessibilityViolations } from '../../../testing/accessibility';
import { ApiDirectory } from './api-directory';

describe('ApiDirectory', () => {
  let fixture: ComponentFixture<ApiDirectory>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const entry = (overrides: Record<string, unknown> = {}) => ({
    name: 'Billing API',
    slug: 'billing-api',
    purpose: 'Raise invoices and record payments from your own systems.',
    baseUrl: 'https://api.example.test/billing',
    authScheme: 'OAuth2',
    docsUrl: 'https://docs.example.test/billing',
    openApiUrl: 'https://docs.example.test/billing/openapi.json',
    hasSandbox: true,
    product: 'Ledger',
    currentVersion: '2.0',
    versions: [
      {
        versionLabel: '2.0',
        status: 'Stable',
        releasedOn: '2026-01-01',
        sunsetDate: null,
        isCurrent: true,
        changelogUrl: 'https://docs.example.test/billing/changelog',
      },
    ],
    ...overrides,
  });

  const create = async () => {
    await TestBed.configureTestingModule({
      imports: [ApiDirectory],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApiDirectory);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('puts the base URL, the auth scheme and the current version on the card', async () => {
    await create();
    http.expectOne('/api/v1/public/apis').flush([entry()]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().querySelector('[data-testid="base-url-billing-api"]')?.textContent).toContain(
      'https://api.example.test/billing',
    );
    expect(element().querySelector('[data-testid="auth-billing-api"]')?.textContent).toContain('OAuth 2.0');
    expect(element().querySelector('[data-testid="current-billing-api"]')?.textContent).toContain('2.0');
    expect(element().querySelector('[data-testid="docs-billing-api"]')).not.toBeNull();
  });

  it('says when a version is being withdrawn and on what date', async () => {
    await create();
    http.expectOne('/api/v1/public/apis').flush([
      entry({
        versions: [
          {
            versionLabel: '2.0',
            status: 'Stable',
            releasedOn: '2026-01-01',
            sunsetDate: null,
            isCurrent: true,
            changelogUrl: null,
          },
          {
            versionLabel: '1.0',
            status: 'Deprecated',
            releasedOn: '2024-01-01',
            sunsetDate: '2027-01-19',
            isCurrent: false,
            changelogUrl: null,
          },
        ],
      }),
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    // The date is the part a developer already on the old version needs (BR-API-03).
    const sunset = element().querySelector('[data-testid="sunset-billing-api-1.0"]');
    expect(sunset?.textContent).toContain('2027-01-19');
  });

  it('offers no sandbox link for an entry that has no sandbox', async () => {
    await create();
    http.expectOne('/api/v1/public/apis').flush([entry({ hasSandbox: false, openApiUrl: null })]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().querySelector('[data-testid="sandbox-billing-api"]')).toBeNull();
    expect(element().querySelector('[data-testid="openapi-billing-api"]')).toBeNull();
  });

  it('shows an empty state rather than a bare heading when nothing is published', async () => {
    await create();
    http.expectOne('/api/v1/public/apis').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const empty = element().querySelector('[data-testid="apis-empty"]');
    expect(empty).not.toBeNull();
    expect(empty?.querySelector('a[href="/contact"]')).not.toBeNull();
  });

  it('has no serious accessibility violations', async () => {
    await create();
    http.expectOne('/api/v1/public/apis').flush([entry()]);
    await fixture.whenStable();
    fixture.detectChanges();

    const violations = await findSeriousAccessibilityViolations(element());
    expect(describeViolations(violations)).toBe('');
  }, AxeTimeout);
});
