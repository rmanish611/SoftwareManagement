import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PublicPlan } from '../../core/catalog/public-catalog.service';
import { PlanTable } from './plan-table';

describe('PlanTable', () => {
  let fixture: ComponentFixture<PlanTable>;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const plan = (name: string, overrides: Partial<PublicPlan> = {}): PublicPlan => ({
    name,
    price: 4999,
    currency: 'INR',
    billingPeriod: 'Monthly',
    includedSeats: 5,
    isFreeTier: false,
    isRecommended: false,
    cells: [
      { feature: 'Users', availability: 'Included', limitValue: null },
      { feature: 'Branches', availability: 'Limited', limitValue: '2 branches' },
      { feature: 'Priority support', availability: 'NotIncluded', limitValue: null },
    ],
    ...overrides,
  });

  const create = async (plans: PublicPlan[]) => {
    await TestBed.configureTestingModule({
      imports: [PlanTable],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(PlanTable);
    fixture.componentRef.setInput('plans', plans);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  it('gives every cell an answer, including the ones that are a no', async () => {
    await create([plan('Starter')]);

    expect(element().querySelector('[data-testid="cell-Starter-Users"]')?.textContent?.trim()).toBe('Included');
    expect(element().querySelector('[data-testid="cell-Starter-Branches"]')?.textContent?.trim()).toBe('2 branches');
    expect(element().querySelector('[data-testid="cell-Starter-Priority support"]')?.textContent?.trim())
      .toBe('Not included');
  });

  it('marks exactly one plan as the recommended one', async () => {
    await create([plan('Starter'), plan('Growth', { isRecommended: true, price: 9999 })]);

    expect(element().querySelectorAll('[data-testid="recommended"]').length).toBe(1);
    expect(element().querySelector('[data-testid="plan-head-Growth"]')?.textContent).toContain('Most chosen');
  });

  it('says Free rather than a price of zero', async () => {
    await create([plan('Free tier', { price: 0, isFreeTier: true })]);

    expect(element().querySelector('[data-testid="plan-head-Free tier"]')?.textContent).toContain('Free');
  });

  it('uses row headers so a screen reader can answer a question about one cell', async () => {
    await create([plan('Starter')]);

    const rowHeaders = Array.from(element().querySelectorAll('tbody th[scope="row"]')).map((th) => th.textContent?.trim());
    expect(rowHeaders).toEqual(['Users', 'Branches', 'Priority support']);
    expect(element().querySelectorAll('thead th[scope="col"]').length).toBe(2);
  });

  it('says pricing is on enquiry rather than rendering an empty table', async () => {
    await create([]);

    expect(element().querySelector('[data-testid="plan-table"]')).toBeNull();
    expect(element().querySelector('[data-testid="plan-table-empty"]')?.textContent).toContain('on enquiry');
  });
});
