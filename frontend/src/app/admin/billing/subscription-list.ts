import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { BillingService, SubscriptionSummary, dueIn } from '../../core/sales/billing.service';
import { formatMoney } from '../../core/sales/sales.service';

/**
 * What is running, for whom, and when it renews.
 *
 * Ordered by renewal date, soonest first, because the screen exists to stop a customer lapsing by
 * accident (REQ-SALE-008). The order is the server's and is not re-sorted here.
 */
@Component({
  selector: 'app-subscription-list',
  imports: [DatePipe],
  template: `
    <section data-testid="subscription-list">
      <h1>Subscriptions</h1>

      @if (loading()) {
        <p data-testid="subscriptions-loading">Loading the subscriptions.</p>
      } @else if (subscriptions().length) {
        <table>
          <caption class="visually-hidden">
            Subscriptions with their tenant, product, plan, status and renewal date
          </caption>
          <thead>
            <tr>
              <th scope="col">Tenant</th>
              <th scope="col">Customer</th>
              <th scope="col">Product</th>
              <th scope="col">Plan</th>
              <th scope="col">Status</th>
              <th scope="col" class="number">Per period</th>
              <th scope="col">Renews</th>
            </tr>
          </thead>
          <tbody>
            @for (subscription of subscriptions(); track subscription.id) {
              <tr [attr.data-testid]="'subscription-' + subscription.id">
                <td>{{ subscription.tenant }}</td>
                <td>{{ subscription.organisation }}</td>
                <td>{{ subscription.product }}</td>
                <td>{{ subscription.plan }}</td>
                <td>
                  <span [class.warn]="isChasing(subscription)">{{ subscription.status }}</span>
                  @if (subscription.trialEndsOn) {
                    <span class="muted" [attr.data-testid]="'trial-' + subscription.id">
                      trial ends {{ subscription.trialEndsOn | date: 'd MMM' }}
                    </span>
                  }
                </td>
                <td class="number">{{ money(subscription) }}</td>
                <td>
                  {{ subscription.currentPeriodEndsOn | date: 'd MMM y' }}
                  <span class="muted" [attr.data-testid]="'renews-' + subscription.id">
                    {{ renews(subscription) }}
                  </span>
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p data-testid="subscriptions-empty">
          Nothing is running yet. A subscription appears here once an accepted quote is provisioned.
        </p>
      }
    </section>
  `,
  styles: `
    table {
      border-collapse: collapse;
      width: 100%;
    }

    th,
    td {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-2) var(--space-3);
      text-align: left;
      vertical-align: top;
    }

    .number {
      font-variant-numeric: tabular-nums;
      text-align: right;
    }

    .muted {
      color: var(--colour-text-muted);
      display: block;
      font-size: 0.85rem;
    }

    /* The word carries the meaning; the colour only reinforces it (NFR-ACC-03). */
    .warn {
      color: var(--colour-danger);
      font-weight: 600;
    }
  `,
})
export class SubscriptionList {
  protected readonly subscriptions = signal<readonly SubscriptionSummary[]>([]);
  protected readonly loading = signal(true);

  private readonly billing = inject(BillingService);

  constructor() {
    this.billing.subscriptions().subscribe({
      next: (rows) => {
        this.subscriptions.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.subscriptions.set([]);
        this.loading.set(false);
      },
    });
  }

  protected money(subscription: SubscriptionSummary): string {
    return formatMoney(subscription.seats * subscription.unitPrice, subscription.currency);
  }

  protected renews(subscription: SubscriptionSummary): string {
    return dueIn(subscription.currentPeriodEndsOn);
  }

  protected isChasing(subscription: SubscriptionSummary): boolean {
    return subscription.status === 'PastDue' || subscription.status === 'Suspended';
  }
}
