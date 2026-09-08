import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  PublicCatalogService,
  PublicCategory,
  PublicProductCard,
  formatPrice,
} from '../../core/catalog/public-catalog.service';

/**
 * The catalogue a visitor lands on.
 *
 * It is grouped by industry rather than by product name, because someone arrives running a hospital
 * or a school, not looking for a product they have never heard of. The filter and the search are in
 * the address bar, so a filtered view can be linked to and shared.
 */
@Component({
  selector: 'app-product-catalog',
  imports: [RouterLink, FormsModule],
  template: `
    <section data-testid="product-catalog">
      <h1>Our software</h1>
      <p class="lead">
        Multi-tenant systems we build, run and support. Every one of them can be tried before you
        talk to us.
      </p>

      <nav class="industries" aria-label="Filter by industry">
        <a
          routerLink="/products"
          [class.industries__item--on]="!category()"
          class="industries__item"
          data-testid="industry-all"
          >All</a
        >
        @for (industry of categories(); track industry.slug) {
          <a
            [routerLink]="['/products']"
            [queryParams]="{ category: industry.slug }"
            class="industries__item"
            [class.industries__item--on]="category() === industry.slug"
            [attr.data-testid]="'industry-' + industry.slug"
            >{{ industry.name }}</a
          >
        }
      </nav>

      <form class="search" (ngSubmit)="applySearch()" role="search">
        <label for="catalog-search">Search products</label>
        <input
          id="catalog-search"
          name="search"
          type="search"
          [(ngModel)]="search"
          placeholder="What do you need it to do?"
        />
        <button type="submit" data-testid="search-submit">Search</button>
      </form>

      @if (loading()) {
        <p data-testid="catalog-loading">Loading the catalogue.</p>
      } @else if (products().length) {
        <ul class="cards">
          @for (product of products(); track product.slug) {
            <li class="card" [attr.data-testid]="'card-' + product.slug">
              <h2><a [routerLink]="['/products', product.slug]">{{ product.name }}</a></h2>
              <p class="card__industry">{{ product.category }}</p>
              <p>{{ product.tagline }}</p>

              <p class="card__price">
                @if (product.fromPrice !== null && product.currency) {
                  <span data-testid="from-price">From {{ price(product) }} a month</span>
                } @else if (product.hasFreeTier) {
                  <span data-testid="from-price">Free to start</span>
                } @else {
                  <span data-testid="from-price">Priced on enquiry</span>
                }
              </p>

              @if (product.hasLiveDemo) {
                <p class="card__demo" [attr.data-testid]="'demo-' + product.slug">Live demo available</p>
              }
            </li>
          }
        </ul>
      } @else {
        <div class="empty" data-testid="catalog-empty">
          <p>Nothing here matches that yet.</p>
          <p>
            Tell us what you need and we will say plainly whether we have built it before.
            <a routerLink="/contact">Get in touch</a>.
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

    .industries {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: var(--space-4) 0;
    }

    .industries__item {
      align-items: center;
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      color: inherit;
      display: inline-flex;
      min-height: 2.25rem;
      padding: 0 var(--space-3);
      text-decoration: none;
    }

    .industries__item--on {
      background: var(--colour-surface);
      font-weight: 600;
    }

    .search {
      align-items: end;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-bottom: var(--space-5);
    }

    .search label {
      display: block;
      width: 100%;
    }

    .cards {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 18rem), 1fr));
      list-style: none;
      padding: 0;
    }

    .card {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md, 8px);
      padding: var(--space-4);
    }

    .card h2 {
      font-size: 1.1rem;
      margin: 0 0 var(--space-1);
    }

    .card__industry,
    .card__demo {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
      margin: 0 0 var(--space-2);
    }

    .card__price {
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
export class ProductCatalog {
  private readonly catalog = inject(PublicCatalogService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly products = signal<PublicProductCard[]>([]);
  protected readonly categories = signal<PublicCategory[]>([]);
  protected readonly category = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly loading = signal(true);

  constructor() {
    this.title.setTitle('Software we build - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Enterprise resource planning, hospital, billing and education software, built and supported in India.',
    });

    this.catalog.categories().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.categories.set([]),
    });

    // The filter and the search live in the query string, so the subscription reloads the list
    // whenever either changes, and a filtered catalogue can be linked to.
    this.route.queryParamMap.subscribe((params) => {
      this.category.set(params.get('category'));
      this.search.set(params.get('search') ?? '');
      this.load();
    });
  }

  protected price(product: PublicProductCard): string {
    return formatPrice(product.fromPrice ?? 0, product.currency ?? 'INR');
  }

  protected applySearch(): void {
    void this.router.navigate(['/products'], {
      queryParams: {
        category: this.category() || null,
        search: this.search() || null,
      },
    });
  }

  private load(): void {
    this.loading.set(true);

    this.catalog.products({ category: this.category(), search: this.search() }).subscribe({
      next: (products) => {
        this.products.set(products);
        this.loading.set(false);
      },
      error: () => {
        // A failed request shows the same invitation as an empty result. A visitor cannot act on a
        // status code, and the alternative is a blank page.
        this.products.set([]);
        this.loading.set(false);
      },
    });
  }
}
