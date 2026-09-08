import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface ProductSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly category: string;
  readonly status: string;
  readonly featureCount: number;
  readonly screenshotCount: number;
  readonly planCount: number;
}

export interface ProductFeature {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly groupName: string | null;
  readonly sortOrder: number;
  readonly isHighlighted: boolean;
}

export interface PricingPlan {
  readonly id: string;
  readonly name: string;
  readonly price: number;
  readonly currency: string;
  readonly billingPeriod: string;
  readonly includedSeats: number;
  readonly isFreeTier: boolean;
  readonly isRecommended: boolean;
  readonly isPublished: boolean;
  readonly sortOrder: number;
}

export interface ProductDetail {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly tagline: string;
  readonly summary: string;
  readonly category: string;
  readonly categorySlug: string;
  readonly status: string;
  readonly features: readonly ProductFeature[];
  readonly screenshots: readonly { id: string; caption: string | null; sortOrder: number }[];
  readonly plans: readonly PricingPlan[];
  readonly demo: { url: string; isEnabled: boolean; healthState: string; hasCredentials: boolean } | null;
}

export interface ProductCategory {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly productCount: number;
}

export interface NewProduct {
  readonly name: string;
  readonly slug: string | null;
  readonly tagline: string;
  readonly summary: string;
  readonly categorySlug: string;
}

export interface NewPlan {
  readonly name: string;
  readonly price: number;
  readonly currency: string;
  readonly billingPeriod: string;
  readonly includedSeats: number;
  readonly isFreeTier: boolean;
  readonly isRecommended: boolean;
  readonly setupFee: number;
  readonly isPublished: boolean;
}

/**
 * The catalogue as an editor works with it. Every write here is refused by the server unless the
 * caller holds the matching permission; the screens hide what a user cannot do, and the server is
 * what actually enforces it.
 */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  products(): Observable<ProductSummary[]> {
    return this.http.get<ProductSummary[]>('/api/v1/admin/products');
  }

  product(id: string): Observable<ProductDetail> {
    return this.http.get<ProductDetail>(`/api/v1/admin/products/${encodeURIComponent(id)}`);
  }

  categories(): Observable<ProductCategory[]> {
    return this.http.get<ProductCategory[]>('/api/v1/admin/product-categories');
  }

  createProduct(body: NewProduct): Observable<ProductDetail> {
    return this.http.post<ProductDetail>('/api/v1/admin/products', body);
  }

  addFeature(productId: string, name: string, description: string | null): Observable<ProductFeature> {
    return this.http.post<ProductFeature>(`/api/v1/admin/products/${encodeURIComponent(productId)}/features`, {
      name,
      description,
      groupName: null,
      isHighlighted: false,
    });
  }

  reorderFeatures(productId: string, orderedIds: readonly string[]): Observable<ProductFeature[]> {
    return this.http.put<ProductFeature[]>(
      `/api/v1/admin/products/${encodeURIComponent(productId)}/features/order`,
      { orderedIds },
    );
  }

  addPlan(productId: string, body: NewPlan): Observable<PricingPlan> {
    return this.http.post<PricingPlan>(`/api/v1/admin/products/${encodeURIComponent(productId)}/plans`, body);
  }

  updatePlan(productId: string, planId: string, body: NewPlan): Observable<PricingPlan> {
    return this.http.put<PricingPlan>(
      `/api/v1/admin/products/${encodeURIComponent(productId)}/plans/${encodeURIComponent(planId)}`,
      body,
    );
  }
}

/**
 * Turns a problem-details response into the sentence to put on the screen. The server sends a
 * machine-readable code and a sentence written for a person; this prefers the sentence and falls
 * back to something honest rather than printing a status number at an editor.
 */
export function problemMessage(error: unknown): string {
  const problem = (error as { error?: { detail?: string; title?: string; code?: string } } | null)?.error;

  return problem?.detail ?? problem?.title ?? 'That could not be saved. Check the entries and try again.';
}
