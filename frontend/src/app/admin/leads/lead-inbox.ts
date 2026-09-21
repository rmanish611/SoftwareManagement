import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LeadSummary, PipelineService, SavedView, slaCountdown } from '../../core/leads/pipeline.service';

/**
 * The enquiry inbox.
 *
 * The order is the API's and deliberately not re-sorted here: unanswered first, the one waiting
 * longest at the top. Whoever opens this screen in the morning should see the enquiry the company
 * is currently failing, not the newest one (REQ-LEAD-009).
 *
 * The view and the search sit in the address bar, so "my breached leads" is a link somebody can
 * bookmark or send to a colleague rather than a set of clicks they repeat every day.
 */
@Component({
  selector: 'app-lead-inbox',
  imports: [DatePipe, RouterLink, FormsModule],
  template: `
    <section data-testid="lead-inbox">
      <header class="head">
        <h1>Enquiries</h1>
        <form class="search" (ngSubmit)="applySearch()" role="search">
          <label class="visually-hidden" for="lead-search">Search enquiries</label>
          <input
            id="lead-search"
            name="search"
            type="search"
            [(ngModel)]="search"
            placeholder="Name, company or message"
          />
          <button type="submit" class="button" data-testid="lead-search-submit">Search</button>
        </form>
      </header>

      <nav class="views" aria-label="Saved views">
        @for (option of viewOptions; track option.key) {
          <a
            [routerLink]="['/admin/leads']"
            [queryParams]="{ view: option.key === 'all' ? null : option.key, search: search || null }"
            class="views__item"
            [class.views__item--on]="view() === option.key"
            [attr.aria-current]="view() === option.key ? 'page' : null"
            [attr.data-testid]="'view-' + option.key"
            >{{ option.label }}</a
          >
        }
      </nav>

      @if (loading()) {
        <p data-testid="leads-loading">Loading the enquiries.</p>
      } @else if (leads().length) {
        <table>
          <caption class="visually-hidden">
            Enquiries, newest unanswered first, with how long is left to reply
          </caption>
          <thead>
            <tr>
              <th scope="col">Who</th>
              <th scope="col">Company</th>
              <th scope="col">Stage</th>
              <th scope="col">Arrived</th>
              <th scope="col">Reply due</th>
            </tr>
          </thead>
          <tbody>
            @for (lead of leads(); track lead.id) {
              <tr [attr.data-testid]="'lead-' + lead.id">
                <td>
                  <a [routerLink]="['/admin/leads', lead.id]">{{ lead.fullName }}</a>
                  @if (lead.email) {
                    <span class="muted">{{ lead.email }}</span>
                  }
                </td>
                <td>{{ lead.companyName ?? '—' }}</td>
                <td>
                  {{ lead.stage }}
                  @if (lead.isStale) {
                    <span class="flag" data-testid="stale-flag">Stale</span>
                  }
                </td>
                <td>{{ lead.createdAtUtc | date: 'd MMM, HH:mm' }}</td>
                <td>
                  <!--
                    The word, not just a colour: somebody who cannot tell red from grey still has
                    to be able to read which enquiry is late (NFR-ACC-03).
                  -->
                  <span
                    [class.late]="lead.isBreached"
                    [attr.data-testid]="'sla-' + lead.id"
                    >{{ countdown(lead) }}</span
                  >
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p data-testid="leads-empty">
          @if (view() === 'all' && !search) {
            Nothing has come in yet. Enquiries from the public forms arrive here.
          } @else {
            Nothing matches that. Try another view or clear the search.
          }
        </p>
      }
    </section>
  `,
  styles: `
    .head {
      align-items: baseline;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      justify-content: space-between;
    }

    .search {
      display: flex;
      gap: var(--space-2);
      max-width: 26rem;
    }

    .views {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: var(--space-4) 0;
    }

    .views__item {
      align-items: center;
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      color: inherit;
      display: inline-flex;
      min-height: 2.25rem;
      padding: 0 var(--space-3);
      text-decoration: none;
    }

    .views__item--on {
      background: var(--colour-surface);
      border-color: var(--colour-accent);
      font-weight: 600;
    }

    table {
      border-collapse: collapse;
      width: 100%;
    }

    th,
    td {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-2) var(--space-3);
      text-align: left;
      vertical-align: top;
    }

    .muted {
      color: var(--colour-text-muted);
      display: block;
      font-size: 0.85rem;
    }

    .flag,
    .late {
      color: var(--colour-danger);
      font-weight: 600;
    }

    .flag {
      font-size: 0.8rem;
      margin-left: var(--space-2);
    }
  `,
})
export class LeadInbox {
  protected readonly viewOptions: { key: SavedView; label: string }[] = [
    { key: 'all', label: 'All open' },
    { key: 'unanswered', label: 'Unanswered' },
    { key: 'breached', label: 'Late' },
    { key: 'stale', label: 'Gone quiet' },
  ];

  protected search = '';
  protected readonly leads = signal<readonly LeadSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly view = signal<SavedView>('all');

  private readonly pipeline = inject(PipelineService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      this.view.set((params.get('view') as SavedView) ?? 'all');
      this.search = params.get('search') ?? '';
      this.load();
    });
  }

  protected countdown(lead: LeadSummary): string {
    return slaCountdown(lead.minutesToSlaDue);
  }

  protected applySearch(): void {
    void this.router.navigate(['/admin/leads'], {
      queryParams: {
        view: this.view() === 'all' ? null : this.view(),
        search: this.search || null,
      },
    });
  }

  private load(): void {
    this.loading.set(true);

    this.pipeline.leads({ view: this.view(), search: this.search }).subscribe({
      next: (leads) => {
        this.leads.set(leads);
        this.loading.set(false);
      },

      // An inbox that shows an error instead of itself has turned a degraded screen into no
      // screen. The empty state says what to try next.
      error: () => {
        this.leads.set([]);
        this.loading.set(false);
      },
    });
  }
}
