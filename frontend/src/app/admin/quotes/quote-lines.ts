import { Component, input, output } from '@angular/core';
import { QuoteLine, formatMoney } from '../../core/sales/sales.service';

/**
 * The priced lines of a quote, and the totals under them.
 *
 * The totals are the ones the server computed, not a sum done again here. Recomputing them in the
 * browser is how a screen comes to show a figure the invoice will not (BR-SALE-03).
 */
@Component({
  selector: 'app-quote-lines',
  template: `
    <section data-testid="quote-lines">
      @if (lines().length) {
        <table>
          <caption class="visually-hidden">The lines of this quote with their tax and totals</caption>
          <thead>
            <tr>
              <th scope="col">What</th>
              <th scope="col" class="number">Qty</th>
              <th scope="col" class="number">Unit</th>
              <th scope="col" class="number">Discount</th>
              <th scope="col" class="number">Line</th>
              <th scope="col" class="number">Tax</th>
              @if (editable()) {
                <th scope="col"><span class="visually-hidden">Remove</span></th>
              }
            </tr>
          </thead>
          <tbody>
            @for (line of lines(); track line.id) {
              <tr [attr.data-testid]="'line-' + line.id">
                <td>{{ line.description }}</td>
                <td class="number">{{ line.quantity }}</td>
                <td class="number">{{ money(line.unitPrice) }}</td>
                <td class="number">{{ line.discountAmount > 0 ? money(line.discountAmount) : '—' }}</td>
                <td class="number">{{ money(line.lineTotal) }}</td>
                <td class="number">{{ money(line.taxAmount) }}</td>
                @if (editable()) {
                  <td>
                    <button
                      type="button"
                      class="button"
                      [attr.data-testid]="'remove-' + line.id"
                      (click)="remove.emit(line.id)"
                    >
                      Remove<span class="visually-hidden"> {{ line.description }}</span>
                    </button>
                  </td>
                }
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <th scope="row" [attr.colspan]="editable() ? 5 : 4">Before tax</th>
              <td class="number" data-testid="sub-total">{{ money(subTotal()) }}</td>
              @if (editable()) {
                <td></td>
              }
            </tr>
            <tr>
              <th scope="row" [attr.colspan]="editable() ? 5 : 4">Tax</th>
              <td class="number" data-testid="tax-total">{{ money(taxTotal()) }}</td>
              @if (editable()) {
                <td></td>
              }
            </tr>
            <tr class="grand">
              <th scope="row" [attr.colspan]="editable() ? 5 : 4">Total</th>
              <td class="number" data-testid="grand-total">{{ money(grandTotal()) }}</td>
              @if (editable()) {
                <td></td>
              }
            </tr>
          </tfoot>
        </table>
      } @else {
        <p data-testid="quote-lines-empty">Nothing on this quote yet. Add a line to price it.</p>
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
    }

    .number {
      text-align: right;
      /* Figures line up under one another, which is what makes a column of money addable by eye. */
      font-variant-numeric: tabular-nums;
    }

    tfoot th {
      text-align: right;
    }

    .grand td,
    .grand th {
      font-weight: 700;
    }

    .button {
      min-height: 2.25rem;
      padding: 0 var(--space-3);
    }
  `,
})
export class QuoteLines {
  readonly lines = input.required<readonly QuoteLine[]>();
  readonly currency = input('INR');
  readonly subTotal = input(0);
  readonly taxTotal = input(0);
  readonly grandTotal = input(0);
  readonly editable = input(false);

  readonly remove = output<string>();

  protected money(amount: number): string {
    return formatMoney(amount, this.currency());
  }
}
