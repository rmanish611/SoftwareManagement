import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { About } from './about';

describe('About', () => {
  let fixture: ComponentFixture<About>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const company = {
    name: 'Software Management',
    city: 'Noida, India',
    team: [{ fullName: 'Manish Kumar', roleTitle: 'Founder', bio: 'Builds the products.' }],
    testimonials: [
      { quote: 'It replaced three spreadsheets.', authorName: 'A Customer', authorRole: 'Director', organisationName: 'Client Ltd' },
    ],
    announcement: null,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [About],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(About);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders the company name and city', async () => {
    http.expectOne('/api/v1/public/company').flush(company);
    await fixture.whenStable();

    expect(element().querySelector('h1')?.textContent).toContain('Software Management');
    expect(element().textContent).toContain('Noida, India');
  });

  it('lists the team', async () => {
    http.expectOne('/api/v1/public/company').flush(company);
    await fixture.whenStable();

    const team = element().querySelector('[data-testid="about-team"]');
    expect(team?.textContent).toContain('Manish Kumar');
    expect(team?.textContent).toContain('Founder');
  });

  it('shows the testimonials the API returned', async () => {
    http.expectOne('/api/v1/public/company').flush(company);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="about-testimonials"]')?.textContent)
      .toContain('It replaced three spreadsheets');
  });

  it('sets the page title for search engines and browser tabs', async () => {
    http.expectOne('/api/v1/public/company').flush(company);
    await fixture.whenStable();

    expect(TestBed.inject(Title).getTitle()).toBe('About Software Management');
  });

  it('offers a way to get in touch when the page cannot load', async () => {
    http.expectOne('/api/v1/public/company').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="about-error"]')?.textContent).toContain('Email us');
  });
});
