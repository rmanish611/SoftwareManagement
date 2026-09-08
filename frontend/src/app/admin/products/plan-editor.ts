import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CatalogService, NewPlan, PricingPlan, problemMessage } from '../../core/catalog/catalog.service';

/**
 * The pricing plans of one product.
 *
 * Two server rules show up here as visible behaviour rather than as a surprise after saving: only
 * one plan may be recommended (BR-CAT-03), and a yearly price above twelve monthly payments is
 * refused as a probable typing mistake (BR-CAT-04). The screen shows the second as a warning before
 * the editor presses save; the server still decides.
 */
@Component({
  selector: 'app-plan-editor',
  imports: [FormsModule],
  template: `
    <section data-testid="plan-editor">
      <h2>Pricing plans</h2>

      @if (plans().length) {
        <ul class="plans">
          @for (plan of plans(); track plan.id) {
            <li [attr.data-testid]="'plan-' + plan.id">
              <span class="plan__name">{{ plan.name }}</span>
              <span class="plan__price">{{ plan.currency }} {{ plan.price }} / {{ plan.billingPeriod }}</span>
              @if (plan.isRecommended) {
                <span class="plan__flag" data-testid="recommended-flag">Recommended</span>
              } @else {
                <button
                  type="button"
                  [attr.data-testid]="'recommend-' + plan.id"
                  (click)="recommend(plan)"
                >
                  Recommend this plan
                </button>
              }
            </li>
          }
        </ul>
      } @else {
        <p data-testid="plans-empty">No plans yet. A product needs at least one before it can be published.</p>
      }

      <form (ngSubmit)="add()" #planForm="ngForm">
        <div class="field">
          <label for="plan-name">Plan name</label>
          <input id="plan-name" name="planName" [(ngModel)]="name" required />
        </div>

        <div class="field">
          <label for="plan-price">Price</label>
          <input id="plan-price" name="planPrice" type="number" min="0" step="0.01" [(ngModel)]="price" required />
        </div>

        <div class="field">
          <label for="plan-period">Billing period</label>
          <select id="plan-period" name="planPeriod" [(ngModel)]="billingPeriod">
            <option value="Monthly">Monthly</option>
            <option value="Quarterly">Quarterly</option>
            <option value="Yearly">Yearly</option>
            <option value="OneTime">One-off</option>
          </select>
        </div>

        <label class="check">
          <input type="checkbox" name="planFree" [(ngModel)]="isFreeTier" />
          This plan is free
        </label>

        <label class="check">
          <input type="checkbox" name="planRecommended" [(ngModel)]="isRecommended" />
          Recommend this plan
        </label>

        @if (yearlyLooksWrong()) {
          <p class="warning" data-testid="yearly-warning">
            A year at {{ price() }} costs more than twelve monthly payments of {{ lowestMonthly() }}.
            Check the figure before saving.
          </p>
        }

        @if (error()) {
          <p class="error" role="alert" data-testid="plan-error">{{ error() }}</p>
        }

        <button type="submit" data-testid="add-plan" [disabled]="saving()">Add plan</button>
      </form>
    </section>
  `,
  styles: `
    .plans {
      display: grid;
      gap: var(--space-2);
      list-style: none;
      padding: 0;
    }

    .plans li {
      align-items: center;
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      display: flex;
      gap: var(--space-3);
      padding: var(--space-2) var(--space-3);
    }

    .plan__name {
      font-weight: 600;
    }

    .plan__price {
      color: var(--colour-text-muted);
    }

    .plan__flag {
      background: var(--colour-surface);
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      font-size: 0.8rem;
      padding: 0 var(--space-2);
    }

    .field {
      display: grid;
      gap: var(--space-1);
      margin-top: var(--space-3);
      max-width: 24rem;
    }

    .check {
      display: flex;
      gap: var(--space-2);
      margin-top: var(--space-2);
    }

    .warning {
      color: var(--colour-warning, #8a5300);
      max-width: 60ch;
    }

    .error {
      color: var(--colour-danger, #a11);
      max-width: 60ch;
    }
  `,
})
export class PlanEditor {
  private readonly catalog = inject(CatalogService);

  readonly productId = input.required<string>();
  readonly initialPlans = input<readonly PricingPlan[]>([]);
  readonly changed = output<readonly PricingPlan[]>();

  // A writable copy of the plans this product arrived with. It re-seeds itself if the product is
  // replaced, and edits made here stay local until the server confirms them.
  protected readonly plans = linkedSignal<PricingPlan[]>(() => [...this.initialPlans()]);
  protected readonly name = signal('');
  protected readonly price = signal(0);
  protected readonly billingPeriod = signal('Monthly');
  protected readonly isFreeTier = signal(false);
  protected readonly isRecommended = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The cheapest monthly plan already saved, which is what a yearly price is judged against. */
  protected lowestMonthly(): number {
    const monthly = this.plans().filter((p) => p.billingPeriod === 'Monthly' && p.price > 0);
    return monthly.length ? Math.min(...monthly.map((p) => p.price)) : 0;
  }

  protected yearlyLooksWrong(): boolean {
    const monthly = this.lowestMonthly();
    return this.billingPeriod() === 'Yearly' && monthly > 0 && this.price() > monthly * 12;
  }

  protected add(): void {
    if (this.saving()) {
      return;
    }

    this.error.set(null);
    this.saving.set(true);

    this.catalog.addPlan(this.productId(), this.body()).subscribe({
      next: (plan) => {
        // A new recommended plan takes the flag from whichever plan held it, exactly as the server
        // just did, so the screen and the database agree without a second request.
        const existing = plan.isRecommended
          ? this.plans().map((p) => ({ ...p, isRecommended: false }))
          : this.plans();

        this.plans.set([...existing, plan]);
        this.changed.emit(this.plans());
        this.name.set('');
        this.price.set(0);
        this.isRecommended.set(false);
        this.saving.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(problemMessage(failure));
        this.saving.set(false);
      },
    });
  }

  protected recommend(plan: PricingPlan): void {
    this.error.set(null);

    const body: NewPlan = {
      name: plan.name,
      price: plan.price,
      currency: plan.currency,
      billingPeriod: plan.billingPeriod,
      includedSeats: plan.includedSeats,
      isFreeTier: plan.isFreeTier,
      isRecommended: true,
      setupFee: 0,
      isPublished: plan.isPublished,
    };

    this.catalog.updatePlan(this.productId(), plan.id, body).subscribe({
      next: (saved) => {
        this.plans.set(this.plans().map((p) => ({ ...p, isRecommended: p.id === saved.id })));
        this.changed.emit(this.plans());
      },
      error: (failure: unknown) => this.error.set(problemMessage(failure)),
    });
  }

  private body(): NewPlan {
    return {
      name: this.name(),
      price: Number(this.price()),
      currency: 'INR',
      billingPeriod: this.billingPeriod(),
      includedSeats: 1,
      isFreeTier: this.isFreeTier(),
      isRecommended: this.isRecommended(),
      setupFee: 0,
      isPublished: false,
    };
  }
}
