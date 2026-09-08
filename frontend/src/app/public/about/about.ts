import { Component, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ContentService, PublicCompany } from '../../core/content/content.service';

/**
 * The about page: who the company is, the people, and what customers have said.
 *
 * Title and description are set on the server as well as in the browser, because a search engine
 * reads the server-rendered HTML and never runs this component (NFR-SEO-01).
 */
@Component({
  selector: 'app-about',
  template: `
    <section data-testid="about-page">
      <h1>About {{ company()?.name ?? 'us' }}</h1>

      @if (company(); as profile) {
        <p class="lede">
          We build and support multi-tenant software from {{ profile.city || 'India' }}: enterprise
          resource planning, hospital management, billing, and school and college administration.
        </p>

        @if (profile.team.length) {
          <h2>The team</h2>
          <ul class="team" data-testid="about-team">
            @for (person of profile.team; track person.fullName) {
              <li>
                <strong>{{ person.fullName }}</strong>
                <span>{{ person.roleTitle }}</span>
                @if (person.bio) {
                  <p>{{ person.bio }}</p>
                }
              </li>
            }
          </ul>
        }

        @if (profile.testimonials.length) {
          <h2>What customers say</h2>
          <ul class="testimonials" data-testid="about-testimonials">
            @for (quote of profile.testimonials; track quote.quote) {
              <li>
                <blockquote>{{ quote.quote }}</blockquote>
                <cite>{{ quote.authorName }}, {{ quote.authorRole }}</cite>
              </li>
            }
          </ul>
        }
      } @else if (failed()) {
        <p data-testid="about-error">
          We could not load this page just now. Email us and we will send it to you directly.
        </p>
      }
    </section>
  `,
  styles: `
    .lede {
      color: var(--colour-text-muted);
      font-size: var(--font-size-lead);
      max-width: 60ch;
    }

    ul {
      display: grid;
      gap: var(--space-4);
      list-style: none;
      padding: 0;
    }

    .team li span {
      color: var(--colour-text-muted);
      display: block;
    }

    blockquote {
      border-left: 3px solid var(--colour-accent);
      margin: 0;
      padding-left: var(--space-4);
    }

    cite {
      color: var(--colour-text-muted);
      display: block;
      font-size: 0.9rem;
      margin-top: var(--space-2);
    }
  `,
})
export class About {
  private readonly content = inject(ContentService);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly company = signal<PublicCompany | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    this.content.company().subscribe({
      next: (profile) => {
        this.company.set(profile);
        this.title.setTitle(`About ${profile.name}`);
        this.meta.updateTag({
          name: 'description',
          content: `${profile.name} builds and supports multi-tenant business software from ${profile.city || 'India'}.`,
        });
      },
      error: () => this.failed.set(true),
    });
  }
}
