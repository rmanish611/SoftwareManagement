import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BillingService, InvoiceRegisterRow, dueIn } from '../../core/sales/billing.service';
import { formatMoney } from '../../core/sales/sales.service';

/**
 * The invoice register.
 *
 * In number order, which is how an auditor reads it: a gap in the sequence is the thing they are
 * looking for, and a date ordering hides it (REQ-RPT-007). The order is the server's.
 */
@Component({
  selector: 'app-invoice-list',
  imports: [DatePipe, RouterLink],
  template: `
    <section data-testid="invoice-list">
      <h1>Invoices</h1>

      @if (loading()) {
        <p data-testid="invoices-loading">Loading the invoices.</p>
      } @else if (invoices().length) {
        <table>
          <caption class="visually-hidden">
            Invoices in number order, with what is outstanding on each
          </caption>
          <thead>
            <tr>
              <th scope="col">Number</th>
              <th scope="col">Customer</th>
              <th scope="col">Status</th>
              <th scope="col" class="number">Total</th>
              <th scope="col" class="number">Outstanding</th>
              <th scope="col">Due</th>
            </tr>
          </thead>
          <tbody>
            @for (invoice of invoices(); track invoice.id) {
              <tr [attr.data-testid]="'invoice-' + invoice.id">
                <td><a [routerLink]="['/admin/invoices', invoice.id]">{{ invoice.invoiceNumber }}</a></td>
                <td>{{ invoice.organisation }}</td>
                <td>{{ invoice.status }}</td>
                <td class="number">{{ money(invoice.grandTotal, invoice) }}</td>
                <td class="number">{{ money(invoice.outstanding, invoice) }}</td>
                <td>
                  {{ invoice.dueDate ? (invoice.dueDate | date: 'd MMM y') : '—' }}
                  <span class="muted" [class.late]="isLate(invoice)" [attr.data-testid]="'due-' + invoice.id">
                    {{ due(invoice) }}
                  </span>
                </td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <th scope="row" colspan="4">Outstanding in total</th>
              <td class="number" data-testid="outstanding-total">{{ outstandingTotal() }}</td>
              <td></td>
            </tr>
          </tfoot>
        </table>
      } @else {
        <p data-testid="invoices-empty">No invoices yet. One is raised against a subscription.</p>
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

    .late {
      color: var(--colour-danger);
      font-weight: 600;
    }

    tfoot th {
      text-align: right;
    }

    tfoot th,
    tfoot td {
      font-weight: 700;
    }
  `,
})
export class InvoiceList {
  protected readonly invoices = signal<readonly InvoiceRegisterRow[]>([]);
  protected readonly loading = signal(true);

  private readonly billing = inject(BillingService);

  constructor() {
    this.billing.invoices().subscribe({
      next: (rows) => {
        this.invoices.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.invoices.set([]);
        this.loading.set(false);
      },
    });
  }

  protected money(amount: number, invoice: InvoiceRegisterRow): string {
    return formatMoney(amount, invoice.currency);
  }

  protected due(invoice: InvoiceRegisterRow): string {
    return invoice.outstanding > 0 ? dueIn(invoice.dueDate) : 'settled';
  }

  protected isLate(invoice: InvoiceRegisterRow): boolean {
    return invoice.outstanding > 0 && invoice.dueDate !== null && Date.parse(invoice.dueDate) < Date.now();
  }

  /** The number the owner actually came to this screen for. */
  protected outstandingTotal(): string {
    const rows = this.invoices();
    const total = rows.reduce((sum, invoice) => sum + invoice.outstanding, 0);

    return formatMoney(total, rows[0]?.currency ?? 'INR');
  }
}
