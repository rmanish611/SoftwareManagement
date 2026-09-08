import { Component, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ContentService, PublicService } from '../../core/content/content.service';

/** What the company does, and the technology behind each service. */
@Component({
  selector: 'app-services-page',
  template: `
    <section data-testid="services-page">
      <h1>Services</h1>

      @if (services().length) {
        <ul class="services">
          @for (service of services(); track service.slug) {
            <li>
              <h2>{{ service.name }}</h2>
              <p>{{ service.summary }}</p>
              @if (service.technologies.length) {
                <ul class="stack" [attr.aria-label]="'Technologies used for ' + service.name">
                  @for (technology of service.technologies; track technology) {
                    <li>{{ technology }}</li>
                  }
                </ul>
              }
            </li>
          }
        </ul>
      } @else {
        <p data-testid="services-empty">
          Our services are being written up. Tell us what you need and we will answer directly.
        </p>
      }
    </section>
  `,
  styles: `
    .services {
      display: grid;
      gap: var(--space-6);
      list-style: none;
      max-width: 70ch;
      padding: 0;
    }

    .services h2 {
      margin-bottom: var(--space-2);
    }

    .stack {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      list-style: none;
      margin-top: var(--space-3);
      padding: 0;
    }

    .stack li {
      background: var(--colour-surface);
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      font-size: 0.85rem;
      padding: var(--space-1) var(--space-3);
    }
  `,
})
export class ServicesPage {
  private readonly content = inject(ContentService);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly services = signal<readonly PublicService[]>([]);

  constructor() {
    this.title.setTitle('Services');
    this.meta.updateTag({
      name: 'description',
      content: 'Custom software, ERP and hospital management implementation, and long-term support.',
    });

    this.content.services().subscribe({
      next: (services) => this.services.set(services),
      // An empty list and a failed request look the same to the visitor on purpose: either way
      // the page offers a way to get in touch rather than an error code.
      error: () => this.services.set([]),
    });
  }
}
