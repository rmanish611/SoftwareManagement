import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface PublicPage {
  readonly title: string;
  readonly slug: string;
  readonly body: string | null;
  readonly metaTitle: string;
  readonly metaDescription: string | null;
  readonly canonicalUrl: string | null;
  readonly noIndex: boolean;
  readonly sections: readonly { sectionType: string; heading: string | null; body: string | null }[];
}

export interface PublicNavigationItem {
  readonly menu: 'Header' | 'Footer';
  readonly label: string;
  readonly href: string;
  readonly opensInNewTab: boolean;
}

export interface PublicService {
  readonly name: string;
  readonly slug: string;
  readonly summary: string;
  readonly technologies: readonly string[];
}

export interface PublicCompany {
  readonly name: string;
  readonly city: string;
  readonly team: readonly { fullName: string; roleTitle: string; bio: string | null }[];
  readonly testimonials: readonly { quote: string; authorName: string; authorRole: string; organisationName: string | null }[];
  readonly announcement: { message: string; linkUrl: string | null; severity: string } | null;
}

/**
 * Reads the published site. Every call here is anonymous and cacheable, and the API only ever
 * returns published content, so nothing on this path can leak a draft.
 */
@Injectable({ providedIn: 'root' })
export class ContentService {
  private readonly http = inject(HttpClient);

  page(slug: string): Observable<PublicPage> {
    return this.http.get<PublicPage>(`/api/v1/public/pages/${encodeURIComponent(slug)}`);
  }

  navigation(): Observable<PublicNavigationItem[]> {
    return this.http.get<PublicNavigationItem[]>('/api/v1/public/navigation');
  }

  services(): Observable<PublicService[]> {
    return this.http.get<PublicService[]>('/api/v1/public/services');
  }

  company(): Observable<PublicCompany> {
    return this.http.get<PublicCompany>('/api/v1/public/company');
  }
}
