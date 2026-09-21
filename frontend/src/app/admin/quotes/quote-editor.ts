import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiProblem, QuoteDetail, SalesService, formatMoney } from '../../core/sales/sales.service';
import { QuoteLines } from './quote-lines';

/**
 * One quote: its lines, its totals, and the two things that can be done to it.
 *
 * While it is a draft the lines can be changed. Once it has been sent they cannot, and the screen
 * says so and offers a revision instead of leaving a form that will be refused (BR-SALE-05).
 */
@Component({
  selector: 'app-quote-editor',
  imports: [DatePipe, FormsModule, RouterLink, QuoteLines],
  template: `
    @if (quote(); as item) {
      <section data-testid="quote-editor">
        <p class="crumb"><a routerLink="/admin/quotes">Quotes</a></p>

        <header class="head">
          <div>
            <h1>{{ item.quoteNumber }}</h1>
            <p class="muted">
              {{ item.organisation }} · {{ item.contactName }}
              @if (item.issuedOn) {
                · issued {{ item.issuedOn | date: 'd MMM y' }}
              }
              @if (item.validUntil) {
                · valid until {{ item.validUntil | date: 'd MMM y' }}
              }
            </p>
          </div>

          <p class="status" data-testid="quote-status">{{ item.status }}</p>
        </header>

        @if (!item.isEditable) {
          <p class="locked" data-testid="quote-locked">
            This quote has been {{ item.status.toLowerCase() }} and its figures are fixed. Revise it to
            change anything.
          </p>
        }

        @if (error(); as message) {
          <p class="error" role="alert" data-testid="quote-error">{{ message }}</p>
        }

        <app-quote-lines
          [lines]="item.lines"
          [currency]="item.currency"
          [subTotal]="item.subTotal"
          [taxTotal]="item.taxTotal"
          [grandTotal]="item.grandTotal"
          [editable]="item.isEditable"
          (remove)="removeLine(item.id, $event)"
        />

        @if (item.isEditable) {
          <form class="new-line" (ngSubmit)="addLine(item.id)">
            <h2>Add a line</h2>

            <div class="field">
              <label for="line-description">What is being sold</label>
              <input id="line-description" name="description" [(ngModel)]="description" />
            </div>

            <div class="row">
              <div class="field">
                <label for="line-quantity">Quantity</label>
                <input id="line-quantity" name="quantity" type="number" min="1" [(ngModel)]="quantity" />
              </div>

              <div class="field">
                <label for="line-price">Unit price</label>
                <input id="line-price" name="unitPrice" type="number" min="0" step="0.01" [(ngModel)]="unitPrice" />
              </div>

              <div class="field">
                <label for="line-discount">Discount</label>
                <input
                  id="line-discount"
                  name="discountAmount"
                  type="number"
                  min="0"
                  step="0.01"
                  [(ngModel)]="discountAmount"
                />
              </div>
            </div>

            @if (discountNeedsApproval()) {
              <!--
                Said before the request rather than after the refusal. The server decides, but a
                screen that lets somebody type a figure it knows will be refused wastes their time
                (BR-SALE-04).
              -->
              <p class="warn" data-testid="discount-warning">
                A discount above 15% of the line needs the owner's approval.
              </p>
            }

            <button type="submit" class="button button--primary" data-testid="add-line">Add line</button>
          </form>

          <div class="actions">
            <button type="button" class="button button--primary" data-testid="send-quote" (click)="send(item.id)">
              Send this quote
            </button>
          </div>
        } @else if (item.status !== 'Accepted') {
          <div class="actions">
            <button type="button" class="button" data-testid="revise-quote" (click)="revise(item.id)">
              Revise it
            </button>
          </div>
        }

        @if (item.rejectReason) {
          <p class="muted" data-testid="reject-reason">Rejected: {{ item.rejectReason }}</p>
        }
      </section>
    } @else if (missing()) {
      <p data-testid="quote-missing">That quote is not here.</p>
    } @else {
      <p data-testid="quote-loading">Loading the quote.</p>
    }
  `,
  styles: `
    .crumb {
      margin: 0 0 var(--space-3);
    }

    .head {
      align-items: baseline;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      justify-content: space-between;
    }

    h1 {
      font-variant-numeric: tabular-nums;
      margin: 0;
    }

    .muted {
      color: var(--colour-text-muted);
      margin: var(--space-1) 0 0;
    }

    .status {
      font-weight: 600;
      margin: 0;
    }

    .locked,
    .warn {
      background: var(--colour-surface);
      border-radius: var(--radius-sm);
      margin: var(--space-4) 0;
      padding: var(--space-3);
    }

    .error {
      color: var(--colour-danger);
    }

    .new-line {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md);
      display: grid;
      gap: var(--space-3);
      margin: var(--space-5) 0;
      max-width: 42rem;
      padding: var(--space-4);
    }

    .new-line h2 {
      font-size: 1.05rem;
      margin: 0;
    }

    .row {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr));
    }

    .field {
      display: grid;
      gap: var(--space-1);
    }

    .new-line button,
    .actions {
      justify-self: start;
    }

    .actions {
      display: flex;
      gap: var(--space-3);
      margin: var(--space-4) 0;
    }
  `,
})
export class QuoteEditor {
  protected description = '';
  protected quantity = 1;
  protected unitPrice = 0;
  protected discountAmount = 0;

  protected readonly quote = signal<QuoteDetail | null>(null);
  protected readonly missing = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly sales = inject(SalesService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  constructor() {
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');

      if (id) {
        this.load(id);
      }
    });
  }

  /** The same 15% the server applies, so the warning and the refusal agree. */
  protected discountNeedsApproval(): boolean {
    const subtotal = this.quantity * this.unitPrice;
    return subtotal > 0 && this.discountAmount > subtotal * 0.15;
  }

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected addLine(id: string): void {
    if (!this.description.trim()) {
      this.error.set('A line needs a description; the customer reads it, not the product id.');
      return;
    }

    this.error.set(null);

    this.sales
      .addLine(id, {
        productId: '00000000-0000-0000-0000-000000000000',
        description: this.description.trim(),
        quantity: this.quantity,
        unitPrice: this.unitPrice,
        discountAmount: this.discountAmount,
        taxRatePercent: 18,
      })
      .subscribe({
        next: () => {
          this.description = '';
          this.quantity = 1;
          this.unitPrice = 0;
          this.discountAmount = 0;
          this.load(id);
        },
        error: (response: { error?: ApiProblem }) =>
          this.error.set(response.error?.detail ?? 'That line could not be added.'),
      });
  }

  protected removeLine(id: string, lineId: string): void {
    this.sales.removeLine(id, lineId).subscribe({
      next: () => this.load(id),
      error: (response: { error?: ApiProblem }) =>
        this.error.set(response.error?.detail ?? 'That line could not be removed.'),
    });
  }

  protected send(id: string): void {
    this.error.set(null);

    this.sales.send(id).subscribe({
      next: () => this.load(id),
      error: (response: { error?: ApiProblem }) =>
        this.error.set(response.error?.detail ?? 'That quote could not be sent.'),
    });
  }

  protected revise(id: string): void {
    this.error.set(null);

    this.sales.revise(id).subscribe({
      // The revision is a different quote with a different number, so the address changes with it.
      next: (revision) => void this.router.navigate(['/admin/quotes', revision.id]),
      error: (response: { error?: ApiProblem }) =>
        this.error.set(response.error?.detail ?? 'That quote could not be revised.'),
    });
  }

  private load(id: string): void {
    this.sales.quote(id).subscribe({
      next: (quote) => {
        this.quote.set(quote);
        this.missing.set(false);
      },
      error: () => {
        this.quote.set(null);
        this.missing.set(true);
      },
    });
  }
}
