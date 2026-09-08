import { Component, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PublicCatalogService, PublicProductPage } from '../../core/catalog/public-catalog.service';
import { PlanTable } from './plan-table';

/**
 * One product page: what it does, what it looks like, what it costs, and two ways to act on that.
 *
 * The demo button is present only when the demo is answering. A dead demo is worse than no demo,
 * because a visitor who clicks it concludes the software is broken rather than that a server is
 * down (BR-INT-04).
 */
@Component({
  selector: 'app-product-detail',
  imports: [RouterLink, PlanTable],
  template: `
    @if (notFound()) {
      <section data-testid="product-missing">
        <h1>We could not find that product</h1>
        <p>
          It may have been retired. <a routerLink="/products">See everything we build</a>, or
          <a routerLink="/contact">tell us what you need</a>.
        </p>
      </section>
    } @else if (product(); as item) {
      <article data-testid="product-detail">
        <header>
          <p class="crumb">
            <a routerLink="/products">Software</a> /
            <a [routerLink]="['/products']" [queryParams]="{ category: item.categorySlug }">{{ item.category }}</a>
          </p>
          <h1>{{ item.name }}</h1>
          <p class="tagline">{{ item.tagline }}</p>

          <div class="actions">
            <a class="action action--primary" routerLink="/contact" [queryParams]="{ product: item.slug }" data-testid="enquire">
              Ask about {{ item.name }}
            </a>

            @if (item.demo) {
              <a class="action" [href]="item.demo.url" rel="noopener" target="_blank" data-testid="try-demo">
                Try the live demo
              </a>
            }
          </div>

          @if (item.demo && item.demo.username) {
            <p class="demo-note" data-testid="demo-credentials">
              Sign in to the demo with <strong>{{ item.demo.username }}</strong>
              @if (item.demo.password) {
                and the password <strong>{{ item.demo.password }}</strong>
              }
              . It is a shared demo, so treat anything you enter as public.
            </p>
          }
        </header>

        <section>
          <h2>What it does</h2>
          <p class="summary">{{ item.summary }}</p>

          @if (item.features.length) {
            <ul class="features" data-testid="feature-list">
              @for (feature of item.features; track feature.name) {
                <li>
                  <strong>{{ feature.name }}</strong>
                  @if (feature.description) {
                    <span>{{ feature.description }}</span>
                  }
                </li>
              }
            </ul>
          }
        </section>

        @if (item.screenshots.length) {
          <section>
            <h2>What it looks like</h2>
            <ul class="shots" data-testid="screenshot-gallery">
              @for (shot of item.screenshots; track shot.url) {
                <li>
                  <img [src]="shot.url" [alt]="shot.altText || shot.caption || item.name" loading="lazy" />
                  @if (shot.caption) {
                    <p>{{ shot.caption }}</p>
                  }
                </li>
              }
            </ul>
          </section>
        }

        <section>
          <h2>What it costs</h2>
          <app-plan-table [plans]="item.plans" />
        </section>

        @if (item.faqs.length) {
          <section>
            <h2>Common questions</h2>
            <div class="faqs" data-testid="faq-list">
              @for (faq of item.faqs; track faq.question) {
                <details>
                  <summary>{{ faq.question }}</summary>
                  <p>{{ faq.answer }}</p>
                </details>
              }
            </div>
          </section>
        }
      </article>
    } @else {
      <p data-testid="product-loading">Loading this product.</p>
    }
  `,
  styles: `
    .crumb,
    .tagline,
    .demo-note {
      color: var(--colour-text-muted);
    }

    .tagline {
      font-size: 1.15rem;
      max-width: 60ch;
    }

    .summary {
      max-width: 65ch;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      margin: var(--space-4) 0;
    }

    .action {
      align-items: center;
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      color: inherit;
      display: inline-flex;
      min-height: 2.75rem;
      padding: 0 var(--space-5);
      text-decoration: none;
    }

    .action--primary {
      background: var(--colour-accent, #0b5cab);
      border-color: transparent;
      color: #fff;
    }

    .features {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 20rem), 1fr));
      list-style: none;
      padding: 0;
    }

    .features li {
      display: grid;
      gap: var(--space-1);
    }

    .shots {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 24rem), 1fr));
      list-style: none;
      padding: 0;
    }

    .shots img {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      height: auto;
      max-width: 100%;
    }

    .faqs details {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-3) 0;
    }

    .faqs summary {
      cursor: pointer;
      font-weight: 600;
    }
  `,
})
export class ProductDetail {
  private readonly catalog = inject(PublicCatalogService);
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly product = signal<PublicProductPage | null>(null);
  protected readonly notFound = signal(false);

  constructor() {
    const slug = this.route.snapshot.paramMap.get('slug') ?? '';

    this.catalog.product(slug).subscribe({
      next: (product) => {
        this.product.set(product);

        // The metadata comes from the product's own record, so an editor controls what a search
        // result says without a deployment (NFR-SEO-01).
        this.title.setTitle(product.metaTitle);
        this.meta.updateTag({ name: 'description', content: product.metaDescription ?? product.tagline });
      },

      // A product that is a draft, archived or never existed is the same page to a visitor. Saying
      // more would confirm what is hidden behind the sign-in.
      error: () => this.notFound.set(true),
    });
  }
}
