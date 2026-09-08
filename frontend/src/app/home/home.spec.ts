import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Home } from './home';

describe('Home', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();
  });

  it('states what the company builds, in one heading', async () => {
    const fixture = TestBed.createComponent(Home);
    await fixture.whenStable();

    const heading = (fixture.nativeElement as HTMLElement).querySelector('h1');

    expect(heading?.textContent).toContain('Software that runs the business');
  });

  it('names the product families a visitor is looking for', async () => {
    const fixture = TestBed.createComponent(Home);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('hospital management');
    expect(text).toContain('billing');
    expect(text).toContain('school and college');
  });
});
