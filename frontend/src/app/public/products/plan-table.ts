import { Component, input } from '@angular/core';
import { PublicPlan, formatPrice } from '../../core/catalog/public-catalog.service';

/**
 * The plan comparison, as a real table.
 *
 * It is a `<table>` with a header row and row headers rather than a grid of divs, because a buyer
 * using a screen reader has to be able to ask "what does the Growth plan give me for branches" and
 * get an answer. A visual grid cannot answer that question (NFR-ACC-01).
 */
@Component({
  selector: 'app-plan-table',
  template: `
    @if (plans().length) {
      <div class="scroll">
        <table data-testid="plan-table">
          <caption>
            What each plan includes. Prices are per month unless the plan says otherwise, and exclude
            18% GST.
          </caption>
          <thead>
            <tr>
              <th scope="col">Feature</th>
              @for (plan of plans(); track plan.name) {
                <th scope="col" [attr.data-testid]="'plan-head-' + plan.name">
                  <span class="plan__name">{{ plan.name }}</span>
                  <span class="plan__price">{{ priceOf(plan) }}</span>
                  @if (plan.isRecommended) {
                    <span class="plan__flag" data-testid="recommended">Most chosen</span>
                  }
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (feature of featureNames(); track feature) {
              <tr>
                <th scope="row">{{ feature }}</th>
                @for (plan of plans(); track plan.name) {
                  <td [attr.data-testid]="'cell-' + plan.name + '-' + feature">{{ cell(plan, feature) }}</td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else {
      <p data-testid="plan-table-empty">Pricing for this product is quoted on enquiry.</p>
    }
  `,
  styles: `
    /* A wide table scrolls inside its own box rather than pushing the page sideways. */
    .scroll {
      overflow-x: auto;
    }

    table {
      border-collapse: collapse;
      min-width: 32rem;
      width: 100%;
    }

    caption {
      color: var(--colour-text-muted);
      padding-bottom: var(--space-3);
      text-align: left;
    }

    th,
    td {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-2) var(--space-3);
      text-align: left;
      vertical-align: top;
    }

    thead th {
      border-bottom-width: 2px;
    }

    .plan__name {
      display: block;
      font-size: 1.05rem;
    }

    .plan__price {
      color: var(--colour-text-muted);
      display: block;
      font-weight: 400;
    }

    .plan__flag {
      background: var(--colour-surface);
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      display: inline-block;
      font-size: 0.75rem;
      font-weight: 400;
      margin-top: var(--space-1);
      padding: 0 var(--space-2);
    }
  `,
})
export class PlanTable {
  readonly plans = input.required<readonly PublicPlan[]>();

  /** The feature rows, taken from the first plan: the server fills every plan with every feature. */
  protected featureNames(): string[] {
    const first = this.plans()[0];
    return first ? first.cells.map((cell) => cell.feature) : [];
  }

  protected priceOf(plan: PublicPlan): string {
    if (plan.isFreeTier || plan.price === 0) {
      return 'Free';
    }

    const period = plan.billingPeriod === 'Monthly' ? 'a month' : `per ${plan.billingPeriod.toLowerCase()}`;
    return `${formatPrice(plan.price, plan.currency)} ${period}`;
  }

  /**
   * Every cell says something. "Not included" is an answer; an empty cell is a buyer wondering
   * whether we forgot or whether we are hiding it.
   */
  protected cell(plan: PublicPlan, feature: string): string {
    const found = plan.cells.find((c) => c.feature === feature);

    if (!found) {
      return 'Not included';
    }

    switch (found.availability) {
      case 'Included':
        return 'Included';
      case 'Limited':
        return found.limitValue ?? 'Limited';
      default:
        return 'Not included';
    }
  }
}
