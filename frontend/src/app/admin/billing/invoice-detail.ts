import { DatePipe } from '@angular/common';
import { Component, ViewChild, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BillingService, InvoiceDetail as InvoiceDetailModel, dueIn } from '../../core/sales/billing.service';
import { ApiProblem, formatMoney } from '../../core/sales/sales.service';
import { PaymentForm, PaymentToRecord } from './payment-form';

/**
 * One invoice, what has been paid against it, and the form for recording the next payment.
 */
@Component({
  selector: 'app-invoice-detail',
  imports: [DatePipe, RouterLink, PaymentForm],
  template: `
    @if (invoice(); as item) {
      <section data-testid="invoice-detail">
        <p class="crumb"><a routerLink="/admin/invoices">Invoices</a></p>

        <header class="head">
          <div>
            <h1>{{ item.invoiceNumber }}</h1>
            <p class="muted">
              {{ item.organisation }}
              @if (item.gstin) {
                · GSTIN {{ item.gstin }}
              }
            </p>
            <p class="muted">{{ item.description }}</p>
          </div>

          <dl class="facts">
            <div>
              <dt>Status</dt>
              <dd data-testid="invoice-status">{{ item.status }}</dd>
            </div>
            <div>
              <dt>Issued</dt>
              <dd>{{ item.issuedOn ? (item.issuedOn | date: 'd MMM y') : '—' }}</dd>
            </div>
            <div>
              <dt>Due</dt>
              <dd data-testid="invoice-due">
                {{ item.dueDate ? (item.dueDate | date: 'd MMM y') : '—' }}
                <span class="muted" [class.late]="isLate(item)">{{ due(item) }}</span>
              </dd>
            </div>
          </dl>
        </header>

        <table class="totals">
          <caption class="visually-hidden">What this invoice is for and what has been paid</caption>
          <tbody>
            <tr>
              <th scope="row">
                Period {{ item.periodStart | date: 'd MMM' }} to {{ item.periodEnd | date: 'd MMM y' }}
              </th>
              <td class="number" data-testid="invoice-subtotal">{{ money(item.subTotal, item) }}</td>
            </tr>
            <tr>
              <th scope="row">Tax at {{ item.taxRatePercent }}%</th>
              <td class="number" data-testid="invoice-tax">{{ money(item.taxTotal, item) }}</td>
            </tr>
            <tr class="grand">
              <th scope="row">Total</th>
              <td class="number" data-testid="invoice-total">{{ money(item.grandTotal, item) }}</td>
            </tr>
            <tr>
              <th scope="row">Paid</th>
              <td class="number" data-testid="invoice-paid">{{ money(item.amountPaid, item) }}</td>
            </tr>
            <tr class="grand">
              <th scope="row">Outstanding</th>
              <td class="number" data-testid="invoice-outstanding">{{ money(item.outstanding, item) }}</td>
            </tr>
          </tbody>
        </table>

        @if (item.payments.length) {
          <section class="payments" aria-labelledby="payments-heading">
            <h2 id="payments-heading">Payments</h2>
            <table>
              <caption class="visually-hidden">Payments received against this invoice</caption>
              <thead>
                <tr>
                  <th scope="col">Received</th>
                  <th scope="col">How</th>
                  <th scope="col">Reference</th>
                  <th scope="col" class="number">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (payment of item.payments; track payment.id) {
                  <tr [attr.data-testid]="'payment-' + payment.id">
                    <td>{{ payment.receivedOn | date: 'd MMM y' }}</td>
                    <td>{{ payment.mode }}</td>
                    <td>{{ payment.referenceNumber }}</td>
                    <td class="number">{{ money(payment.amount, item) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }

        @if (item.outstanding > 0) {
          <app-payment-form
            [outstanding]="item.outstanding"
            [currency]="item.currency"
            (record)="record(item.id, $event)"
          />
        } @else {
          <p data-testid="invoice-settled">This invoice is settled in full.</p>
        }
      </section>
    } @else if (missing()) {
      <p data-testid="invoice-missing">That invoice is not here.</p>
    } @else {
      <p data-testid="invoice-loading">Loading the invoice.</p>
    }
  `,
  styles: `
    .crumb {
      margin: 0 0 var(--space-3);
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-5);
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

    .late {
      color: var(--colour-danger);
      font-weight: 600;
    }

    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-5);
      margin: 0;
    }

    .facts dt {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
    }

    .facts dd {
      font-weight: 600;
      margin: 0;
    }

    table {
      border-collapse: collapse;
      margin: var(--space-5) 0;
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

    .totals th {
      font-weight: 400;
    }

    .grand th,
    .grand td {
      font-weight: 700;
    }

    .payments h2 {
      font-size: 1.05rem;
      margin: 0;
    }
  `,
})
export class InvoiceDetail {
  protected readonly invoice = signal<InvoiceDetailModel | null>(null);
  protected readonly missing = signal(false);

  @ViewChild(PaymentForm) private paymentForm?: PaymentForm;

  private readonly billing = inject(BillingService);
  private readonly route = inject(ActivatedRoute);

  constructor() {
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');

      if (id) {
        this.load(id);
      }
    });
  }

  protected money(amount: number, invoice: InvoiceDetailModel): string {
    return formatMoney(amount, invoice.currency);
  }

  protected due(invoice: InvoiceDetailModel): string {
    return dueIn(invoice.dueDate);
  }

  protected isLate(invoice: InvoiceDetailModel): boolean {
    return invoice.outstanding > 0 && invoice.dueDate !== null && Date.parse(invoice.dueDate) < Date.now();
  }

  protected record(id: string, payment: PaymentToRecord): void {
    this.billing.recordPayment(id, payment).subscribe({
      next: () => this.load(id),

      // The server knows whether it was an overpayment, a duplicate reference or a closed invoice,
      // and names the outstanding figure when it is the first. A generic message throws that away.
      error: (response: { error?: ApiProblem }) =>
        this.paymentForm?.fail(response.error?.detail ?? 'That payment could not be recorded.'),
    });
  }

  private load(id: string): void {
    this.billing.invoice(id).subscribe({
      next: (invoice) => {
        this.invoice.set(invoice);
        this.missing.set(false);
      },
      error: () => {
        this.invoice.set(null);
        this.missing.set(true);
      },
    });
  }
}
