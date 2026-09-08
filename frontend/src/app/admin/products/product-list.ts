import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService, ProductSummary } from '../../core/catalog/catalog.service';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Every product in the catalogue, with the counts that decide whether it is ready to publish: a
 * product needs three features, a screenshot and a plan before it may go live (BR-CAT-01), so those
 * three numbers are the ones an editor looks at first.
 */
@Component({
  selector: 'app-product-list',
  imports: [RouterLink],
  template: `
    <section data-testid="product-list">
      <header class="head">
        <h1>Products</h1>
        @if (auth.can('catalog.product.write')) {
          <a class="primary" routerLink="/admin/products/new" data-testid="new-product">New product</a>
        }
      </header>

      @if (loading()) {
        <p data-testid="products-loading">Loading the catalogue.</p>
      } @else if (products().length) {
        <table>
          <caption class="visually-hidden">Products, their status and how complete each one is</caption>
          <thead>
            <tr>
              <th scope="col">Product</th>
              <th scope="col">Industry</th>
              <th scope="col">Status</th>
              <th scope="col">Features</th>
              <th scope="col">Screenshots</th>
              <th scope="col">Plans</th>
            </tr>
          </thead>
          <tbody>
            @for (product of products(); track product.id) {
              <tr [attr.data-testid]="'product-' + product.slug">
                <td><a [routerLink]="['/admin/products', product.id]">{{ product.name }}</a></td>
                <td>{{ product.category }}</td>
                <td>{{ product.status }}</td>
                <td class="count">
                  {{ product.featureCount }}
                  @if (product.featureCount < 3) { <span class="short">(short)</span> }
                </td>
                <td class="count">
                  {{ product.screenshotCount }}
                  @if (product.screenshotCount < 1) { <span class="short">(short)</span> }
                </td>
                <td class="count">
                  {{ product.planCount }}
                  @if (product.planCount < 1) { <span class="short">(short)</span> }
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p data-testid="products-empty">
          No products yet. Add the first one and it stays a draft until you publish it.
        </p>
      }
    </section>
  `,
  styles: `
    .head {
      align-items: baseline;
      display: flex;
      gap: var(--space-4);
      justify-content: space-between;
    }

    table {
      border-collapse: collapse;
      margin-top: var(--space-4);
      width: 100%;
    }

    th,
    td {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-2) var(--space-3);
      text-align: left;
    }

    .count {
      text-align: right;
    }

    /* A count below the publishing threshold is what stops the product going live, so the word
       is real text in the table rather than a colour or a CSS pseudo-element: a screen reader
       reads it, and so does anyone who cannot tell the colours apart (NFR-ACC-03). */
    .short {
      color: var(--colour-text-muted);
      font-size: 0.8rem;
    }

    .primary {
      background: var(--colour-accent, #0b5cab);
      border-radius: var(--radius-sm);
      color: #fff;
      display: inline-flex;
      min-height: 2.25rem;
      align-items: center;
      padding: 0 var(--space-4);
      text-decoration: none;
    }

    .visually-hidden {
      clip: rect(0 0 0 0);
      clip-path: inset(50%);
      height: 1px;
      overflow: hidden;
      position: absolute;
      white-space: nowrap;
      width: 1px;
    }
  `,
})
export class ProductList {
  protected readonly auth = inject(AuthService);
  private readonly catalog = inject(CatalogService);

  protected readonly products = signal<ProductSummary[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.catalog.products().subscribe({
      next: (products) => {
        this.products.set(products);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
