import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface SubscriptionSummary {
  readonly id: string;
  readonly tenantId: string;
  readonly tenant: string;
  readonly organisation: string;
  readonly product: string;
  readonly plan: string;
  readonly status: string;
  readonly seats: number;
  readonly unitPrice: number;
  readonly currency: string;
  readonly billingPeriod: string;
  readonly startedOn: string;
  readonly trialEndsOn: string | null;
  readonly currentPeriodEndsOn: string;
}

export interface InvoiceSummary {
  readonly id: string;
  readonly invoiceNumber: string;
  readonly status: string;
  readonly currency: string;
  readonly grandTotal: number;
  readonly amountPaid: number;
  readonly issuedOn: string | null;
  readonly dueDate: string | null;
}

export interface PaymentRow {
  readonly id: string;
  readonly amount: number;
  readonly mode: string;
  readonly referenceNumber: string;
  readonly receivedOn: string;
  readonly isRefund: boolean;
  readonly notes: string | null;
}

export interface InvoiceDetail {
  readonly id: string;
  readonly invoiceNumber: string;
  readonly organisation: string;
  readonly gstin: string | null;
  readonly status: string;
  readonly description: string;
  readonly currency: string;
  readonly taxRatePercent: number;
  readonly subTotal: number;
  readonly taxTotal: number;
  readonly grandTotal: number;
  readonly amountPaid: number;
  readonly outstanding: number;
  readonly issuedOn: string | null;
  readonly dueDate: string | null;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly payments: readonly PaymentRow[];
}

export interface InvoiceRegisterRow {
  readonly id: string;
  readonly invoiceNumber: string;
  readonly organisation: string;
  readonly status: string;
  readonly currency: string;
  readonly grandTotal: number;
  readonly amountPaid: number;
  readonly outstanding: number;
  readonly issuedOn: string | null;
  readonly dueDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);

  subscriptions(status?: string): Observable<SubscriptionSummary[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<SubscriptionSummary[]>('/api/v1/subscriptions', { params });
  }

  invoices(status?: string): Observable<InvoiceRegisterRow[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<InvoiceRegisterRow[]>('/api/v1/invoices', { params });
  }

  invoice(id: string): Observable<InvoiceDetail> {
    return this.http.get<InvoiceDetail>(`/api/v1/invoices/${id}`);
  }

  recordPayment(
    id: string,
    payment: { amount: number; mode: string; referenceNumber: string; receivedOn: string | null; notes: string | null },
  ): Observable<{ invoiceNumber: string; outstanding: number }> {
    return this.http.post<{ invoiceNumber: string; outstanding: number }>(`/api/v1/invoices/${id}/payments`, payment);
  }
}

/**
 * How many days until a date, as a phrase. Negative days read as "late", which is the word the
 * person scanning an ageing list is looking for.
 */
export function dueIn(date: string | null): string {
  if (!date) {
    return '—';
  }

  const days = Math.round((Date.parse(date) - Date.now()) / 86_400_000);

  if (days === 0) {
    return 'today';
  }

  return days > 0 ? `in ${days}d` : `${Math.abs(days)}d late`;
}
