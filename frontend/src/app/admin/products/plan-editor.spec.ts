import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PricingPlan } from '../../core/catalog/catalog.service';
import { PlanEditor } from './plan-editor';

describe('PlanEditor', () => {
  let fixture: ComponentFixture<PlanEditor>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const plan = (overrides: Partial<PricingPlan> = {}): PricingPlan => ({
    id: 'plan-1',
    name: 'Starter',
    price: 1000,
    currency: 'INR',
    billingPeriod: 'Monthly',
    includedSeats: 5,
    isFreeTier: false,
    isRecommended: false,
    isPublished: true,
    sortOrder: 1,
    ...overrides,
  });

  const create = async (plans: PricingPlan[]) => {
    await TestBed.configureTestingModule({
      imports: [PlanEditor],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(PlanEditor);
    fixture.componentRef.setInput('productId', 'product-1');
    fixture.componentRef.setInput('initialPlans', plans);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  afterEach(() => http.verify());

  it('shows which plan is recommended and offers the flag on the others', async () => {
    await create([plan(), plan({ id: 'plan-2', name: 'Growth', price: 3000, isRecommended: true })]);

    expect(element().querySelector('[data-testid="recommend-plan-1"]')).not.toBeNull();
    expect(element().querySelector('[data-testid="recommend-plan-2"]')).toBeNull();
    expect(element().querySelector('[data-testid="recommended-flag"]')?.textContent).toContain('Recommended');
  });

  it('moves the recommendation to one plan only, matching what the server stores', async () => {
    await create([plan({ isRecommended: true }), plan({ id: 'plan-2', name: 'Growth', price: 3000 })]);

    (element().querySelector('[data-testid="recommend-plan-2"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    http.expectOne('/api/v1/admin/products/product-1/plans/plan-2').flush(
      plan({ id: 'plan-2', name: 'Growth', price: 3000, isRecommended: true }),
    );
    await fixture.whenStable();

    expect(element().querySelectorAll('[data-testid="recommended-flag"]').length).toBe(1);
    expect(element().querySelector('[data-testid="recommend-plan-1"]')).not.toBeNull();
  });

  it('warns before saving when a yearly price beats twelve monthly payments', async () => {
    await create([plan({ price: 1000 })]);

    fixture.componentInstance['billingPeriod'].set('Yearly');
    fixture.componentInstance['price'].set(15000);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="yearly-warning"]')?.textContent).toContain('twelve monthly payments');
  });

  it('keeps quiet about a yearly price that is cheaper than paying monthly', async () => {
    await create([plan({ price: 1000 })]);

    fixture.componentInstance['billingPeriod'].set('Yearly');
    fixture.componentInstance['price'].set(10000);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="yearly-warning"]')).toBeNull();
  });

  it('repeats the server refusal instead of pretending a plan was saved', async () => {
    await create([]);

    element().querySelector('form')?.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne('/api/v1/admin/products/product-1/plans').flush(
      {
        title: 'A plan priced at zero must say it is free',
        detail: 'Tick the free-tier box, so an unfinished price is never published as though the plan cost nothing.',
        status: 422,
        code: 'INVALID_PRICE',
      },
      { status: 422, statusText: 'Unprocessable Content' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="plan-error"]')?.textContent).toContain('free-tier box');
    expect(element().querySelector('[data-testid="plans-empty"]')).not.toBeNull();
  });
});
