import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ProductList } from './product-list';

describe('ProductList', () => {
  let fixture: ComponentFixture<ProductList>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const row = (name: string, overrides: Partial<Record<string, unknown>> = {}) => ({
    id: `id-${name}`,
    name,
    slug: name.toLowerCase(),
    category: 'Enterprise resource planning',
    status: 'Draft',
    featureCount: 3,
    screenshotCount: 1,
    planCount: 2,
    ...overrides,
  });

  const create = async (permissions: string[] = ['catalog.product.read', 'catalog.product.write']) => {
    await TestBed.configureTestingModule({
      imports: [ProductList],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { can: (permission: string) => permissions.includes(permission) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductList);
    http = TestBed.inject(HttpTestingController);
  };

  afterEach(() => http.verify());

  it('lists each product with the counts that decide whether it can be published', async () => {
    await create();
    http.expectOne('/api/v1/admin/products').flush([row('Ledger')]);
    await fixture.whenStable();

    const listed = element().querySelector('[data-testid="product-ledger"]');
    expect(listed?.textContent).toContain('Ledger');
    expect(listed?.textContent).toContain('Enterprise resource planning');
    expect(listed?.textContent).toContain('Draft');
  });

  it('marks a product that is short of the publishing thresholds in words, not only in colour', async () => {
    await create();
    http.expectOne('/api/v1/admin/products').flush([row('Half', { featureCount: 2, screenshotCount: 0 })]);
    await fixture.whenStable();

    const listed = element().querySelector('[data-testid="product-half"]');
    expect(listed?.textContent).toContain('(short)');
  });

  it('offers to add a product only to someone allowed to write one', async () => {
    await create(['catalog.product.read']);
    http.expectOne('/api/v1/admin/products').flush([row('Ledger')]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="new-product"]')).toBeNull();
  });

  it('explains an empty catalogue rather than showing a bare table', async () => {
    await create();
    http.expectOne('/api/v1/admin/products').flush([]);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="products-empty"]')?.textContent).toContain('No products yet');
  });

  it('stops showing the loading line when the request fails', async () => {
    await create();
    http.expectOne('/api/v1/admin/products').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="products-loading"]')).toBeNull();
    expect(element().textContent).not.toContain('500');
  });
});
