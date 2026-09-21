import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatMoney } from '../../core/sales/sales.service';

export interface PaymentToRecord {
  readonly amount: number;
  readonly mode: string;
  readonly referenceNumber: string;
  readonly receivedOn: string | null;
  readonly notes: string | null;
}

/**
 * Recording money against an invoice.
 *
 * The outstanding figure is shown beside the amount box and the button fills it in, because the
 * commonest entry by far is "they paid the lot" and typing it out is where a digit goes missing.
 * The server still refuses an overpayment; this only saves the person from making one.
 */
@Component({
  selector: 'app-payment-form',
  imports: [FormsModule],
  template: `
    <form class="payment" (ngSubmit)="submit()" data-testid="payment-form">
      <h2>Record a payment</h2>

      <p class="outstanding">
        Outstanding: <strong data-testid="payment-outstanding">{{ outstandingText() }}</strong>
      </p>

      <div class="field">
        <label for="payment-amount">Amount</label>
        <input id="payment-amount" name="amount" type="number" min="0" step="0.01" [(ngModel)]="amount" />
        <button type="button" class="button" data-testid="pay-in-full" (click)="payInFull()">
          Pay in full
        </button>
      </div>

      <div class="field">
        <label for="payment-mode">How it arrived</label>
        <select id="payment-mode" name="mode" [(ngModel)]="mode">
          <option value="NeftRtgs">NEFT / RTGS</option>
          <option value="Upi">UPI</option>
          <option value="Cheque">Cheque</option>
          <option value="Cash">Cash</option>
          <option value="CardLink">Card link</option>
          <option value="Other">Other</option>
        </select>
      </div>

      <div class="field">
        <label for="payment-reference">Bank reference</label>
        <input
          id="payment-reference"
          name="referenceNumber"
          [(ngModel)]="referenceNumber"
          [attr.aria-invalid]="error() ? 'true' : null"
        />
      </div>

      <div class="field">
        <label for="payment-received">Received on</label>
        <input id="payment-received" name="receivedOn" type="date" [(ngModel)]="receivedOn" />
      </div>

      @if (error(); as message) {
        <p class="error" role="alert" data-testid="payment-error">{{ message }}</p>
      }

      <button type="submit" class="button button--primary" data-testid="record-payment">Record it</button>
    </form>
  `,
  styles: `
    .payment {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md);
      display: grid;
      gap: var(--space-3);
      max-width: 32rem;
      padding: var(--space-4);
    }

    .payment h2 {
      font-size: 1.05rem;
      margin: 0;
    }

    .outstanding {
      color: var(--colour-text-muted);
      margin: 0;
    }

    .field {
      display: grid;
      gap: var(--space-1);
    }

    /* The amount box and its shortcut sit on one line; the rest of the fields stack. */
    .field:has(#payment-amount) {
      align-items: end;
      grid-template-columns: 1fr auto;
    }

    .field:has(#payment-amount) label {
      grid-column: 1 / -1;
    }

    .error {
      color: var(--colour-danger);
      margin: 0;
    }

    .payment > button[type='submit'] {
      justify-self: start;
    }
  `,
})
export class PaymentForm {
  readonly outstanding = input.required<number>();
  readonly currency = input('INR');

  readonly record = output<PaymentToRecord>();

  protected amount = 0;
  protected mode = 'NeftRtgs';
  protected referenceNumber = '';
  protected receivedOn = '';

  protected readonly error = signal<string | null>(null);

  /** Set from outside when the server refuses, so its words are shown rather than a generic line. */
  fail(message: string): void {
    this.error.set(message);
  }

  protected outstandingText(): string {
    return formatMoney(this.outstanding(), this.currency());
  }

  protected payInFull(): void {
    this.amount = this.outstanding();
  }

  protected submit(): void {
    if (this.amount <= 0) {
      this.error.set('A payment is an amount greater than nothing.');
      return;
    }

    if (!this.referenceNumber.trim()) {
      // Without it the payment cannot be reconciled against a bank statement, which is the only
      // reason it is being recorded.
      this.error.set('A payment needs the bank reference, so it can be reconciled.');
      return;
    }

    if (this.amount > this.outstanding()) {
      this.error.set('That is more than the outstanding amount on this invoice.');
      return;
    }

    this.error.set(null);

    this.record.emit({
      amount: this.amount,
      mode: this.mode,
      referenceNumber: this.referenceNumber.trim(),
      receivedOn: this.receivedOn || null,
      notes: null,
    });
  }
}
