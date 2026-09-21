import { Component, inject, signal } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PortfolioService, PublicProjectCard } from '../../core/portfolio/portfolio.service';

/**
 * Delivered work, filtered by industry.
 *
 * A visitor arriving here is asking one question - have you done this for somebody like me - so the
 * industry filter is the whole navigation, and it lives in the query string so a filtered view can
 * be sent to a colleague (REQ-PRJ-004).
 */
@Component({
  selector: 'app-project-list',
  imports: [RouterLink],
  template: `
    <section data-testid="project-list">
      <h1>Work we have delivered</h1>
      <p class="lead">
        Systems in production, with the numbers behind them. Where a client has asked not to be
        named we say the industry instead, and nothing more.
      </p>

      <nav class="industries" aria-label="Filter by industry">
        <a
          routerLink="/projects"
          class="industries__item"
          [class.industries__item--on]="!industry()"
          data-testid="industry-all"
          >All work</a
        >
        @for (name of industries(); track name) {
          <a
            [routerLink]="['/projects']"
            [queryParams]="{ industry: name }"
            class="industries__item"
            [class.industries__item--on]="industry() === name"
            [attr.data-testid]="'industry-' + name"
            >{{ name }}</a
          >
        }
      </nav>

      @if (loading()) {
        <p data-testid="projects-loading">Loading our work.</p>
      } @else if (projects().length) {
        <ul class="cards">
          @for (project of projects(); track project.slug) {
            <li class="card" [attr.data-testid]="'project-' + project.slug">
              <p class="card__industry">{{ project.industry }}</p>
              <h2><a [routerLink]="['/projects', project.slug]">{{ project.title }}</a></h2>
              <p class="card__client" [attr.data-testid]="'client-' + project.slug">{{ project.client }}</p>
              <p>{{ project.summary }}</p>
              <p class="card__when">{{ when(project) }}</p>
            </li>
          }
        </ul>
      } @else {
        <div class="empty" data-testid="projects-empty">
          <p>We have not published anything in that industry yet.</p>
          <p>
            That does not mean we have not built it.
            <a routerLink="/contact">Tell us what you need</a> and we will say plainly whether we
            have done it before.
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

    .cards {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 20rem), 1fr));
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
    .card__when {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
      margin: 0 0 var(--space-2);
    }

    .card__client {
      font-weight: 600;
      margin: 0 0 var(--space-2);
    }

    .empty {
      border: 1px dashed var(--colour-border);
      border-radius: var(--radius-md, 8px);
      max-width: 60ch;
      padding: var(--space-5);
    }
  `,
})
export class ProjectList {
  private readonly portfolio = inject(PortfolioService);
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  protected readonly projects = signal<PublicProjectCard[]>([]);
  protected readonly industries = signal<string[]>([]);
  protected readonly industry = signal<string | null>(null);
  protected readonly loading = signal(true);

  constructor() {
    this.title.setTitle('Work we have delivered - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Delivered projects with the outcomes measured, by industry.',
    });

    this.portfolio.industries().subscribe({
      next: (industries) => this.industries.set(industries),
      error: () => this.industries.set([]),
    });

    this.route.queryParamMap.subscribe((params) => {
      this.industry.set(params.get('industry'));
      this.load();
    });
  }

  protected when(project: PublicProjectCard): string {
    const started = year(project.startedOn);
    const finished = project.completedOn ? year(project.completedOn) : null;

    if (!finished) {
      return `Started ${started}, still running`;
    }

    return started === finished ? `Delivered ${finished}` : `${started} to ${finished}`;
  }

  private load(): void {
    this.loading.set(true);

    this.portfolio.projects(this.industry()).subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loading.set(false);
      },
      error: () => {
        // The same invitation as an empty result: a visitor cannot act on a status code.
        this.projects.set([]);
        this.loading.set(false);
      },
    });
  }
}

function year(date: string): string {
  return date.slice(0, 4);
}
