import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface PublicProductCard {
  readonly name: string;
  readonly slug: string;
  readonly tagline: string;
  readonly category: string;
  readonly categorySlug: string;
  readonly isFeatured: boolean;
  readonly fromPrice: number | null;
  readonly currency: string | null;
  readonly hasFreeTier: boolean;
  readonly hasLiveDemo: boolean;
}

export interface PublicCategory {
  readonly name: string;
  readonly slug: string;
  readonly description: string | null;
  readonly iconKey: string | null;
  readonly productCount: number;
}

export interface PublicPlanCell {
  readonly feature: string;
  readonly availability: 'Included' | 'Limited' | 'NotIncluded';
  readonly limitValue: string | null;
}

export interface PublicPlan {
  readonly name: string;
  readonly price: number;
  readonly currency: string;
  readonly billingPeriod: string;
  readonly includedSeats: number;
  readonly isFreeTier: boolean;
  readonly isRecommended: boolean;
  readonly cells: readonly PublicPlanCell[];
}

export interface PublicProductPage {
  readonly name: string;
  readonly slug: string;
  readonly tagline: string;
  readonly summary: string;
  readonly body: string | null;
  readonly category: string;
  readonly categorySlug: string;
  readonly metaTitle: string;
  readonly metaDescription: string | null;
  readonly features: readonly { name: string; description: string | null; groupName: string | null }[];
  readonly screenshots: readonly { url: string; caption: string | null; altText: string | null }[];
  readonly plans: readonly PublicPlan[];
  readonly faqs: readonly { question: string; answer: string }[];
  readonly demo: { url: string; username: string | null; password: string | null } | null;
}

/**
 * Reads the published catalogue. Every call is anonymous and cacheable, and the API returns
 * published products only, so nothing on this path can show a visitor a draft.
 */
@Injectable({ providedIn: 'root' })
export class PublicCatalogService {
  private readonly http = inject(HttpClient);

  products(filter: { category?: string | null; search?: string | null } = {}): Observable<PublicProductCard[]> {
    let params = new HttpParams();
    if (filter.category) {
      params = params.set('category', filter.category);
    }
    if (filter.search) {
      params = params.set('search', filter.search);
    }

    return this.http.get<PublicProductCard[]>('/api/v1/public/products', { params });
  }

  categories(): Observable<PublicCategory[]> {
    return this.http.get<PublicCategory[]>('/api/v1/public/catalog/categories');
  }

  product(slug: string): Observable<PublicProductPage> {
    return this.http.get<PublicProductPage>(`/api/v1/public/products/${encodeURIComponent(slug)}`);
  }
}

/**
 * Formats a price the way an Indian buyer reads one: grouped in lakhs, with the currency symbol and
 * no decimal places on a whole number. `Intl` does the grouping, because doing it by hand is how a
 * site ends up showing 4,999,00.
 */
export function formatPrice(amount: number, currency: string): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency,
    maximumFractionDigits: Number.isInteger(amount) ? 0 : 2,
  }).format(amount);
}
