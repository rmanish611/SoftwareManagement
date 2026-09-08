import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ServicesPage } from './services-page';

describe('ServicesPage', () => {
  let fixture: ComponentFixture<ServicesPage>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServicesPage],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(ServicesPage);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists each published service with its technologies', async () => {
    http.expectOne('/api/v1/public/services').flush([
      { name: 'ERP implementation', slug: 'erp', summary: 'We fit it to how you work.', technologies: ['.NET', 'SQL Server'] },
    ]);
    await fixture.whenStable();

    expect(element().querySelector('h2')?.textContent).toContain('ERP implementation');
    expect(element().textContent).toContain('SQL Server');
  });

  it('labels the technology list for assistive technology', async () => {
    http.expectOne('/api/v1/public/services').flush([
      { name: 'Support', slug: 'support', summary: 'We keep it running.', technologies: ['Angular'] },
    ]);
    await fixture.whenStable();

    expect(element().querySelector('.stack')?.getAttribute('aria-label')).toBe('Technologies used for Support');
  });

  it('invites contact rather than showing an empty page when nothing is published', async () => {
    http.expectOne('/api/v1/public/services').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="services-empty"]')?.textContent).toContain('Tell us what you need');
  });

  it('does the same when the request fails, without showing an error code', async () => {
    http.expectOne('/api/v1/public/services').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const text = element().textContent ?? '';
    expect(text).toContain('Tell us what you need');
    expect(text).not.toContain('500');
  });
});
