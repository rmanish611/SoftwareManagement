import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface OrganisationSummary {
  readonly id: string;
  readonly legalName: string;
  readonly displayName: string;
  readonly gstin: string | null;
  readonly city: string | null;
  readonly state: string | null;
  readonly status: string;
  readonly contactCount: number;
}

export interface NewOrganisation {
  readonly legalName: string;
  readonly displayName?: string | null;
  readonly gstin?: string | null;
  readonly city?: string | null;
  readonly state?: string | null;
  readonly postalCode?: string | null;
}

export interface QuoteSummary {
  readonly id: string;
  readonly quoteNumber: string;
  readonly organisation: string;
  readonly status: string;
  readonly currency: string;
  readonly grandTotal: number;
  readonly issuedOn: string | null;
  readonly validUntil: string | null;
  readonly lineCount: number;
}

export interface QuoteLine {
  readonly id: string;
  readonly productId: string;
  readonly description: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly discountAmount: number;
  readonly taxRatePercent: number;
  readonly lineTotal: number;
  readonly taxAmount: number;
  readonly sortOrder: number;
}

export interface QuoteDetail {
  readonly id: string;
  readonly quoteNumber: string;
  readonly organisation: string;
  readonly contactName: string;
  readonly contactEmail: string;
  readonly status: string;
  readonly currency: string;
  readonly subTotal: number;
  readonly discountTotal: number;
  readonly taxTotal: number;
  readonly grandTotal: number;
  readonly issuedOn: string | null;
  readonly validUntil: string | null;
  readonly notes: string | null;
  readonly rejectReason: string | null;
  readonly isEditable: boolean;
  readonly lines: readonly QuoteLine[];
}

/**
 * The problem body the API returns when it refuses something. Typed because two screens read the
 * `field` and the `detail` off it rather than showing a generic failure.
 */
export interface ApiProblem {
  readonly detail?: string;
  readonly code?: string;
  readonly field?: string;
  readonly expectedPattern?: string;
}

@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);

  organisations(search?: string): Observable<OrganisationSummary[]> {
    let params = new HttpParams();

    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<OrganisationSummary[]>('/api/v1/organisations', { params });
  }

  createOrganisation(body: NewOrganisation): Observable<OrganisationSummary> {
    return this.http.post<OrganisationSummary>('/api/v1/organisations', body);
  }

  quotes(status?: string): Observable<QuoteSummary[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<QuoteSummary[]>('/api/v1/quotes', { params });
  }

  quote(id: string): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`/api/v1/quotes/${id}`);
  }

  addLine(
    id: string,
    line: {
      productId: string;
      description: string;
      quantity: number;
      unitPrice: number;
      discountAmount: number;
      taxRatePercent: number;
    },
  ): Observable<void> {
    return this.http.post<void>(`/api/v1/quotes/${id}/lines`, line);
  }

  removeLine(id: string, lineId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/quotes/${id}/lines/${lineId}`);
  }

  send(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/quotes/${id}/send`, {});
  }

  revise(id: string): Observable<{ id: string; quoteNumber: string }> {
    return this.http.post<{ id: string; quoteNumber: string }>(`/api/v1/quotes/${id}/revise`, null);
  }
}

/**
 * Money as the customer will read it. Rupees with the grouping Indian readers expect, which is not
 * the grouping a plain thousands separator gives: 12,34,567 rather than 1,234,567.
 */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount);
}
