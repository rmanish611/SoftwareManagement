import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuoteSummary, SalesService, formatMoney } from '../../core/sales/sales.service';

/**
 * Every quote, newest first, with what it is worth and whether it is still live.
 */
@Component({
  selector: 'app-quote-list',
  imports: [DatePipe, RouterLink],
  template: `
    <section data-testid="quote-list">
      <h1>Quotes</h1>

      @if (loading()) {
        <p data-testid="quotes-loading">Loading the quotes.</p>
      } @else if (quotes().length) {
        <table>
          <caption class="visually-hidden">Quotes with their customer, value and validity</caption>
          <thead>
            <tr>
              <th scope="col">Number</th>
              <th scope="col">Customer</th>
              <th scope="col">Status</th>
              <th scope="col" class="number">Value</th>
              <th scope="col">Valid until</th>
            </tr>
          </thead>
          <tbody>
            @for (quote of quotes(); track quote.id) {
              <tr [attr.data-testid]="'quote-' + quote.id">
                <td><a [routerLink]="['/admin/quotes', quote.id]">{{ quote.quoteNumber }}</a></td>
                <td>{{ quote.organisation }}</td>
                <td>{{ quote.status }}</td>
                <td class="number">{{ money(quote) }}</td>
                <td>{{ quote.validUntil ? (quote.validUntil | date: 'd MMM y') : '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p data-testid="quotes-empty">No quotes yet. One is raised against a customer record.</p>
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
      font-variant-numeric: tabular-nums;
      text-align: right;
    }
  `,
})
export class QuoteList {
  protected readonly quotes = signal<readonly QuoteSummary[]>([]);
  protected readonly loading = signal(true);

  private readonly sales = inject(SalesService);

  constructor() {
    this.sales.quotes().subscribe({
      next: (rows) => {
        this.quotes.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.quotes.set([]);
        this.loading.set(false);
      },
    });
  }

  protected money(quote: QuoteSummary): string {
    return formatMoney(quote.grandTotal, quote.currency);
  }
}
