import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PublicCatalogService,
  PublicCategory,
  PublicProductCard,
  formatPrice,
} from '../core/catalog/public-catalog.service';

/**
 * The home page.
 *
 * A visitor arriving here is deciding whether this company builds the kind of thing they need and
 * whether it is worth a conversation. So the page answers those two questions in order: what we
 * build, shown as the real catalogue rather than a picture of one, and then how to start. The
 * products come from the API, so publishing one puts it here without a deploy.
 *
 * Everything renders with or without the API answering. A home page that shows an error because a
 * catalogue call failed has turned a degraded page into no page at all.
 */
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <div class="home" data-testid="home">
      <section class="hero">
        <h1>Software that runs the business, built and supported by one team.</h1>
        <p class="hero__lead">
          We design, build and run multi-tenant software: enterprise resource planning, hospital
          management, billing, and school and college administration. Every product on this site is
          one we build ourselves and support directly.
        </p>
        <div class="hero__actions">
          <a class="button button--primary" routerLink="/products">See what we build</a>
          <a class="button" routerLink="/request-demo">Book a demo</a>
        </div>
      </section>

      @if (industries().length) {
        <section class="industries" aria-labelledby="industries-heading">
          <h2 id="industries-heading">Who we build for</h2>
          <ul class="industries__list">
            @for (industry of industries(); track industry.slug) {
              <li>
                <a [routerLink]="['/products']" [queryParams]="{ category: industry.slug }">
                  <span class="industries__name">{{ industry.name }}</span>
                  <span class="industries__count">
                    {{ industry.productCount }}
                    {{ industry.productCount === 1 ? 'product' : 'products' }}
                  </span>
                </a>
              </li>
            }
          </ul>
        </section>
      }

      @if (products().length) {
        <section class="catalogue" aria-labelledby="catalogue-heading">
          <div class="catalogue__head">
            <h2 id="catalogue-heading">What we build</h2>
            <a routerLink="/products">All software</a>
          </div>

          <ul class="cards">
            @for (product of products(); track product.slug) {
              <li class="card">
                <h3>
                  <a [routerLink]="['/products', product.slug]">{{ product.name }}</a>
                </h3>
                <p class="card__category">{{ product.category }}</p>
                <p class="card__tagline">{{ product.tagline }}</p>
                <p class="card__price">
                  @if (product.fromPrice !== null && product.currency) {
                    From {{ formatPrice(product.fromPrice, product.currency) }} a month
                  } @else {
                    Priced per deployment
                  }
                </p>
                @if (product.hasLiveDemo) {
                  <p class="card__demo">Live demo available</p>
                }
              </li>
            }
          </ul>
        </section>
      }

      <section class="how" aria-labelledby="how-heading">
        <h2 id="how-heading">How working with us goes</h2>
        <ol class="how__steps">
          <li>
            <h3>Look at it first</h3>
            <p>
              Every product has a page with its feature list, screenshots and prices, and most have
              a demo you can open without talking to anyone.
            </p>
          </li>
          <li>
            <h3>Tell us what you actually run</h3>
            <p>
              A half-hour call using your own examples, not ours. If it is not a fit we say so on
              the call rather than sending a proposal.
            </p>
          </li>
          <li>
            <h3>Your own instance</h3>
            <p>
              We set the product up as a tenant of its own, migrate what you already have, and
              stay on it afterwards. The people who built it are the people who answer.
            </p>
          </li>
        </ol>
      </section>

      <section class="closing">
        <h2>Tell us what you are trying to run better.</h2>
        <p>We answer every enquiry ourselves, usually the same working day.</p>
        <div class="hero__actions">
          <a class="button button--primary" routerLink="/contact">Talk to us</a>
          <a class="button" routerLink="/request-quote">Request a quote</a>
        </div>
      </section>
    </div>
  `,
  styleUrl: './home.scss',
})
export class Home {
  private readonly catalog = inject(PublicCatalogService);

  protected readonly products = signal<readonly PublicProductCard[]>([]);
  protected readonly industries = signal<readonly PublicCategory[]>([]);
  protected readonly formatPrice = formatPrice;

  constructor() {
    // Six is what fits two rows on a laptop without the page turning into the catalogue; the
    // catalogue is one link away and says so.
    this.catalog.products().subscribe({
      next: (cards) => this.products.set(cards.slice(0, 6)),
      error: () => this.products.set([]),
    });

    this.catalog.categories().subscribe({
      next: (categories) => this.industries.set(categories.filter((c) => c.productCount > 0)),
      error: () => this.industries.set([]),
    });
  }
}
