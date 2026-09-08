import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CatalogService,
  ProductCategory,
  ProductDetail,
  ProductFeature,
  problemMessage,
} from '../../core/catalog/catalog.service';
import { PlanEditor } from './plan-editor';

/**
 * Creates a product and then fills it in: features, plans and the demo link.
 *
 * A product is a draft the moment it is created, and it stays one until someone publishes it, so
 * this screen never asks "are you ready" before saving. It shows what is still missing against the
 * publishing thresholds instead (BR-CAT-01).
 */
@Component({
  selector: 'app-product-editor',
  imports: [FormsModule, PlanEditor],
  template: `
    <section data-testid="product-editor">
      <h1>{{ product() ? product()!.name : 'New product' }}</h1>

      @if (!product()) {
        <form (ngSubmit)="create()">
          <div class="field">
            <label for="product-name">Name</label>
            <input id="product-name" name="name" [(ngModel)]="name" required />
          </div>

          <div class="field">
            <label for="product-slug">Address</label>
            <input id="product-slug" name="slug" [(ngModel)]="slug" />
            <p class="hint">Leave this empty to build the address from the name.</p>
          </div>

          <div class="field">
            <label for="product-category">Industry</label>
            <select id="product-category" name="categorySlug" [(ngModel)]="categorySlug" required>
              @for (category of categories(); track category.slug) {
                <option [value]="category.slug">{{ category.name }}</option>
              }
            </select>
          </div>

          <div class="field">
            <label for="product-tagline">One-line description</label>
            <input id="product-tagline" name="tagline" [(ngModel)]="tagline" required />
          </div>

          <div class="field">
            <label for="product-summary">Summary</label>
            <textarea id="product-summary" name="summary" rows="4" [(ngModel)]="summary"></textarea>
          </div>

          @if (error()) {
            <p class="error" role="alert" data-testid="product-error">{{ error() }}</p>
          }

          <button type="submit" data-testid="save-product" [disabled]="saving()">Create product</button>
        </form>
      } @else {
        <p class="status" data-testid="product-status">
          {{ product()!.status }} in {{ product()!.category }}, at /products/{{ product()!.slug }}
        </p>

        <p class="readiness" data-testid="readiness">{{ readiness() }}</p>

        <section>
          <h2>Features</h2>
          @if (features().length) {
            <ol class="features">
              @for (feature of features(); track feature.id) {
                <li [attr.data-testid]="'feature-' + feature.id">{{ feature.name }}</li>
              }
            </ol>
          } @else {
            <p data-testid="features-empty">No features yet. Three is the minimum before publishing.</p>
          }

          <form (ngSubmit)="addFeature()" data-testid="feature-form">
            <div class="field">
              <label for="feature-name">Feature</label>
              <input id="feature-name" name="featureName" [(ngModel)]="featureName" required />
            </div>

            @if (featureError()) {
              <p class="error" role="alert" data-testid="feature-error">{{ featureError() }}</p>
            }

            <button type="submit" data-testid="add-feature">Add feature</button>
          </form>
        </section>

        <app-plan-editor
          [productId]="product()!.id"
          [initialPlans]="product()!.plans"
        />
      }
    </section>
  `,
  styles: `
    .field {
      display: grid;
      gap: var(--space-1);
      margin-top: var(--space-3);
      max-width: 34rem;
    }

    .hint,
    .status {
      color: var(--colour-text-muted);
    }

    .readiness {
      max-width: 60ch;
    }

    .features {
      display: grid;
      gap: var(--space-1);
      padding-left: var(--space-5);
    }

    .error {
      color: var(--colour-danger, #a11);
      max-width: 60ch;
    }
  `,
})
export class ProductEditor {
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly features = signal<ProductFeature[]>([]);

  protected readonly name = signal('');
  protected readonly slug = signal('');
  protected readonly categorySlug = signal('');
  protected readonly tagline = signal('');
  protected readonly summary = signal('');

  protected readonly featureName = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly featureError = signal<string | null>(null);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id');

    if (id) {
      this.catalog.product(id).subscribe({
        next: (product) => this.load(product),
        error: (failure: unknown) => this.error.set(problemMessage(failure)),
      });
      return;
    }

    this.catalog.categories().subscribe({
      next: (categories) => {
        this.categories.set(categories);
        if (categories.length && !this.categorySlug()) {
          this.categorySlug.set(categories[0].slug);
        }
      },
      error: () => this.categories.set([]),
    });
  }

  /**
   * What is still missing before this product may be published, in the editor's words rather than
   * as a list of rule identifiers.
   */
  protected readiness(): string {
    const product = this.product();
    if (!product) {
      return '';
    }

    const missing: string[] = [];
    const shortBy = 3 - this.features().length;

    if (shortBy > 0) {
      missing.push(`${shortBy} more feature${shortBy === 1 ? '' : 's'}`);
    }

    if (product.screenshots.length === 0) {
      missing.push('a screenshot');
    }

    if (product.plans.length === 0) {
      missing.push('a pricing plan');
    }

    return missing.length === 0
      ? 'This product has everything it needs to be published.'
      : `Still needed before publishing: ${missing.join(', ')}.`;
  }

  protected create(): void {
    if (this.saving()) {
      return;
    }

    this.error.set(null);
    this.saving.set(true);

    this.catalog
      .createProduct({
        name: this.name(),
        slug: this.slug() || null,
        tagline: this.tagline(),
        summary: this.summary(),
        categorySlug: this.categorySlug(),
      })
      .subscribe({
        next: (product) => {
          this.load(product);
          this.saving.set(false);
          void this.router.navigate(['/admin/products', product.id]);
        },
        error: (failure: unknown) => {
          // The server suggests a free address when the one asked for is taken; showing that
          // suggestion is the difference between a dead end and one more click.
          this.error.set(problemMessage(failure));
          this.saving.set(false);
        },
      });
  }

  protected addFeature(): void {
    const product = this.product();
    if (!product) {
      return;
    }

    this.featureError.set(null);

    this.catalog.addFeature(product.id, this.featureName(), null).subscribe({
      next: (feature) => {
        this.features.set([...this.features(), feature]);
        this.featureName.set('');
      },
      error: (failure: unknown) => this.featureError.set(problemMessage(failure)),
    });
  }

  private load(product: ProductDetail): void {
    this.product.set(product);
    this.features.set([...product.features]);
  }
}
