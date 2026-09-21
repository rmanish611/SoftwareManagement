import { Component, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { PortfolioService, PublicApiEntry } from '../../core/portfolio/portfolio.service';

/**
 * The developer directory.
 *
 * The reader is deciding whether to build against us, so the three things they need - where it is,
 * how it authenticates and which version to use - are on the card itself rather than behind a
 * contact form (REQ-API-004). A version being withdrawn says so, with the date (BR-API-03).
 */
@Component({
  selector: 'app-api-directory',
  imports: [RouterLink],
  template: `
    <section data-testid="api-directory">
      <h1>APIs for developers</h1>
      <p class="lead">
        Everything below is live, documented and versioned. Deprecations are announced at least 90
        days before the date they stop answering.
      </p>

      @if (loading()) {
        <p data-testid="apis-loading">Loading the directory.</p>
      } @else if (entries().length) {
        <ul class="entries">
          @for (entry of entries(); track entry.slug) {
            <li class="entry" [attr.data-testid]="'api-' + entry.slug">
              <h2>{{ entry.name }}</h2>
              <p>{{ entry.purpose }}</p>

              <dl class="facts">
                @if (entry.baseUrl) {
                  <div>
                    <dt>Base URL</dt>
                    <dd><code [attr.data-testid]="'base-url-' + entry.slug">{{ entry.baseUrl }}</code></dd>
                  </div>
                }
                <div>
                  <dt>Authentication</dt>
                  <dd [attr.data-testid]="'auth-' + entry.slug">{{ authScheme(entry) }}</dd>
                </div>
                <div>
                  <dt>Current version</dt>
                  <dd [attr.data-testid]="'current-' + entry.slug">
                    {{ entry.currentVersion ?? 'Not yet published' }}
                  </dd>
                </div>
                @if (entry.product) {
                  <div>
                    <dt>Part of</dt>
                    <dd>{{ entry.product }}</dd>
                  </div>
                }
              </dl>

              <p class="links">
                @if (entry.docsUrl) {
                  <a [href]="entry.docsUrl" rel="noopener" [attr.data-testid]="'docs-' + entry.slug">
                    Documentation
                  </a>
                }
                @if (entry.openApiUrl) {
                  <a [href]="entry.openApiUrl" rel="noopener" [attr.data-testid]="'openapi-' + entry.slug">
                    OpenAPI description
                  </a>
                }
                @if (entry.hasSandbox) {
                  <span class="sandbox" [attr.data-testid]="'sandbox-' + entry.slug">Sandbox available</span>
                }
              </p>

              <h3 class="versions__heading">Versions</h3>
              <ul class="versions" [attr.data-testid]="'versions-' + entry.slug">
                @for (version of entry.versions; track version.versionLabel) {
                  <li>
                    <span class="versions__label">{{ version.versionLabel }}</span>
                    <span class="versions__status" [class.versions__status--going]="version.status === 'Deprecated'">
                      {{ version.status }}
                    </span>
                    @if (version.sunsetDate) {
                      <span [attr.data-testid]="'sunset-' + entry.slug + '-' + version.versionLabel">
                        Stops answering {{ version.sunsetDate }}
                      </span>
                    }
                    @if (version.changelogUrl) {
                      <a [href]="version.changelogUrl" rel="noopener">What changed</a>
                    }
                  </li>
                }
              </ul>
            </li>
          }
        </ul>
      } @else {
        <div class="empty" data-testid="apis-empty">
          <p>No APIs are published yet.</p>
          <p>
            We build integrations to order.
            <a routerLink="/contact">Tell us what you need to connect</a>.
          </p>
        </div>
      }
    </section>
  `,
  styles: `
    .lead {
      color: var(--colour-text-muted);
      max-width: 60ch;
    }

    .entries {
      display: grid;
      gap: var(--space-4);
      list-style: none;
      padding: 0;
    }

    .entry {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md, 8px);
      padding: var(--space-4);
    }

    .entry h2 {
      font-size: 1.15rem;
      margin: 0 0 var(--space-2);
    }

    .entry p {
      max-width: 65ch;
    }

    .facts {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 14rem), 1fr));
      margin: var(--space-3) 0;
    }

    .facts dt {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
    }

    .facts dd {
      margin: 0;
    }

    .facts code {
      overflow-wrap: anywhere;
    }

    .links {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
    }

    .sandbox {
      color: var(--colour-text-muted);
    }

    .versions__heading {
      font-size: 0.95rem;
      margin: var(--space-4) 0 var(--space-2);
    }

    .versions {
      display: grid;
      gap: var(--space-2);
      list-style: none;
      padding: 0;
    }

    .versions li {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
    }

    .versions__label {
      font-weight: 600;
    }

    .versions__status {
      color: var(--colour-text-muted);
    }

    .versions__status--going {
      color: var(--colour-warning);
      font-weight: 600;
    }

    .empty {
      border: 1px dashed var(--colour-border);
      border-radius: var(--radius-md, 8px);
      max-width: 60ch;
      padding: var(--space-5);
    }
  `,
})
export class ApiDirectory {
  private readonly portfolio = inject(PortfolioService);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly entries = signal<PublicApiEntry[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.title.setTitle('APIs for developers - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Public APIs with base URLs, authentication, current versions and deprecation dates.',
    });

    this.portfolio.apis().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: () => {
        this.entries.set([]);
        this.loading.set(false);
      },
    });
  }

  /** The stored enum spelled the way a developer writes it in a header. */
  protected authScheme(entry: PublicApiEntry): string {
    switch (entry.authScheme) {
      case 'ApiKey':
        return 'API key';
      case 'OAuth2':
        return 'OAuth 2.0';
      case 'JwtBearer':
        return 'JWT bearer token';
      default:
        return 'None - open';
    }
  }
}
