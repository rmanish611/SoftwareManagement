import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PortfolioService, PublicCaseStudy } from '../../core/portfolio/portfolio.service';

/**
 * One piece of work, told as problem, approach and outcome.
 *
 * The page has to stand on its own for somebody arriving from a search result, so the whole
 * narrative and every metric come down in the first response rather than after a second call
 * (REQ-PRJ-009), and the structured data goes into the head so the result itself carries the
 * headline and the date (NFR-SEO-04).
 */
@Component({
  selector: 'app-case-study',
  imports: [RouterLink],
  template: `
    @if (notFound()) {
      <section data-testid="case-study-missing">
        <h1>We could not find that project</h1>
        <p>
          It may not be published. <a routerLink="/projects">See the work we have delivered</a>, or
          <a routerLink="/contact">tell us what you need</a>.
        </p>
      </section>
    } @else if (study(); as item) {
      <article data-testid="case-study">
        <header>
          <p class="crumb">
            <a routerLink="/projects">Work</a> /
            <a [routerLink]="['/projects']" [queryParams]="{ industry: item.industry }">{{ item.industry }}</a>
          </p>
          <h1>{{ item.title }}</h1>
          <p class="client" data-testid="case-study-client">{{ item.client }}</p>
          <p class="summary">{{ item.summary }}</p>

          @if (item.product) {
            <p class="product" data-testid="case-study-product">Built on {{ item.product }}.</p>
          }
        </header>

        @if (item.metrics.length) {
          <section aria-labelledby="outcomes">
            <h2 id="outcomes">What changed</h2>
            <ul class="metrics" data-testid="case-study-metrics">
              @for (metric of item.metrics; track metric.label) {
                <li>
                  <span class="metrics__value">{{ metric.value }}</span>
                  <span class="metrics__unit">{{ metric.unit }}</span>
                  <span class="metrics__label">{{ metric.label }}</span>
                </li>
              }
            </ul>
          </section>
        }

        @if (item.problem) {
          <section>
            <h2>The problem</h2>
            <p>{{ item.problem }}</p>
          </section>
        }

        @if (item.approach) {
          <section>
            <h2>What we did</h2>
            <p>{{ item.approach }}</p>
          </section>
        }

        @if (item.outcome) {
          <section>
            <h2>Where it got to</h2>
            <p>{{ item.outcome }}</p>
          </section>
        }

        @if (item.testimonial; as quote) {
          <figure class="quote" data-testid="case-study-testimonial">
            <blockquote>{{ quote.quote }}</blockquote>
            <figcaption>
              {{ quote.authorName }}, {{ quote.authorRole }}@if (quote.organisationName) {
                <span>, {{ quote.organisationName }}</span>
              }
            </figcaption>
          </figure>
        }

        <p class="next">
          <a class="button button--primary" routerLink="/contact" data-testid="case-study-contact">
            Talk to us about work like this
          </a>
        </p>
      </article>
    } @else {
      <p data-testid="case-study-loading">Loading this project.</p>
    }
  `,
  styles: `
    .crumb,
    .product {
      color: var(--colour-text-muted);
    }

    .client {
      font-size: 1.15rem;
      font-weight: 600;
      margin: 0 0 var(--space-2);
    }

    .summary,
    article p {
      max-width: 65ch;
    }

    .metrics {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 14rem), 1fr));
      list-style: none;
      padding: 0;
    }

    .metrics li {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md, 8px);
      display: grid;
      gap: var(--space-1);
      padding: var(--space-4);
    }

    .metrics__value {
      font-size: 2rem;
      font-weight: 700;
      line-height: 1;
    }

    .metrics__unit {
      color: var(--colour-text-muted);
    }

    .metrics__label {
      font-weight: 600;
    }

    .quote {
      border-left: 3px solid var(--colour-accent, var(--colour-border));
      margin: var(--space-5) 0;
      padding-left: var(--space-4);
    }

    .quote blockquote {
      font-size: 1.1rem;
      margin: 0 0 var(--space-2);
      max-width: 60ch;
    }

    .quote figcaption {
      color: var(--colour-text-muted);
    }

    .next {
      margin-top: var(--space-5);
    }
  `,
})
export class CaseStudy {
  private readonly portfolio = inject(PortfolioService);
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  protected readonly study = signal<PublicCaseStudy | null>(null);
  protected readonly notFound = signal(false);

  private script: HTMLScriptElement | null = null;

  constructor() {
    const slug = this.route.snapshot.paramMap.get('slug') ?? '';

    inject(DestroyRef).onDestroy(() => this.script?.remove());

    this.portfolio.project(slug).subscribe({
      next: (study) => {
        this.study.set(study);
        this.title.setTitle(`${study.title} - Software Management`);
        this.meta.updateTag({ name: 'description', content: study.summary });
        this.writeStructuredData(study);
      },

      // A draft and an address nobody ever used are the same page to a visitor.
      error: () => this.notFound.set(true),
    });
  }

  /**
   * The case study as `Article` structured data.
   *
   * It is written into the head with the DOM rather than put in the template, because Angular drops
   * `<script>` elements out of templates - the tag would simply not be in the rendered HTML, and
   * the page would look right while telling a crawler nothing.
   */
  private writeStructuredData(study: PublicCaseStudy): void {
    const document = this.document;

    const data = {
      '@context': 'https://schema.org',
      '@type': 'Article',
      headline: study.title,
      description: study.summary,
      about: study.industry,
      datePublished: study.completedOn ?? study.startedOn,
      publisher: {
        '@type': 'Organization',
        name: 'Software Management',
      },
      ...(study.testimonial
        ? {
            review: {
              '@type': 'Review',
              reviewBody: study.testimonial.quote,
              author: { '@type': 'Person', name: study.testimonial.authorName },
            },
          }
        : {}),
    };

    this.script?.remove();
    this.script = document.createElement('script');
    this.script.type = 'application/ld+json';
    this.script.setAttribute('data-testid', 'case-study-jsonld');
    this.script.textContent = JSON.stringify(data);
    document.head.appendChild(this.script);
  }
}
