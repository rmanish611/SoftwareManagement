import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface PublicProjectCard {
  readonly title: string;
  readonly slug: string;
  readonly industry: string;
  readonly summary: string;
  /** Either the client's name or the anonymised label. The server decides which (BR-PRJ-01). */
  readonly client: string;
  readonly startedOn: string;
  readonly completedOn: string | null;
  readonly isFeatured: boolean;
}

export interface OutcomeMetric {
  readonly label: string;
  readonly value: number;
  readonly unit: string;
}

export interface PublicTestimonial {
  readonly quote: string;
  readonly authorName: string;
  readonly authorRole: string;
  readonly organisationName: string | null;
}

export interface PublicCaseStudy {
  readonly title: string;
  readonly slug: string;
  readonly industry: string;
  readonly summary: string;
  readonly client: string;
  readonly startedOn: string;
  readonly completedOn: string | null;
  readonly product: string | null;
  readonly problem: string | null;
  readonly approach: string | null;
  readonly outcome: string | null;
  readonly metrics: readonly OutcomeMetric[];
  readonly testimonial: PublicTestimonial | null;
}

export interface PublicApiVersion {
  readonly versionLabel: string;
  readonly status: 'Beta' | 'Stable' | 'Deprecated';
  readonly releasedOn: string;
  readonly sunsetDate: string | null;
  readonly isCurrent: boolean;
  readonly changelogUrl: string | null;
}

export interface PublicApiEntry {
  readonly name: string;
  readonly slug: string;
  readonly purpose: string;
  readonly baseUrl: string | null;
  readonly authScheme: string;
  readonly docsUrl: string | null;
  readonly openApiUrl: string | null;
  readonly hasSandbox: boolean;
  readonly product: string | null;
  readonly currentVersion: string | null;
  readonly versions: readonly PublicApiVersion[];
}

export interface PublicClientLogo {
  readonly name: string;
  readonly imageUrl: string;
  readonly altText: string;
}

/**
 * Reads the portfolio and the developer directory.
 *
 * Every response here has already been through the publishing gate, and the client label in
 * particular is computed on the server: nothing in this file decides whether a client may be
 * named, because that promise is not one a browser should be trusted with (BR-PRJ-01).
 */
@Injectable({ providedIn: 'root' })
export class PortfolioService {
  private readonly http = inject(HttpClient);

  projects(industry?: string | null): Observable<PublicProjectCard[]> {
    let params = new HttpParams();

    if (industry) {
      params = params.set('industry', industry);
    }

    return this.http.get<PublicProjectCard[]>('/api/v1/public/projects', { params });
  }

  industries(): Observable<string[]> {
    return this.http.get<string[]>('/api/v1/public/projects/industries');
  }

  project(slug: string): Observable<PublicCaseStudy> {
    return this.http.get<PublicCaseStudy>(`/api/v1/public/projects/${encodeURIComponent(slug)}`);
  }

  apis(): Observable<PublicApiEntry[]> {
    return this.http.get<PublicApiEntry[]>('/api/v1/public/apis');
  }

  clientLogos(): Observable<PublicClientLogo[]> {
    return this.http.get<PublicClientLogo[]>('/api/v1/public/client-logos');
  }
}
