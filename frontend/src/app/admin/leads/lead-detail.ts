import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  DuplicateSuggestion,
  LeadDetail as LeadDetailModel,
  PipelineService,
  slaCountdown,
} from '../../core/leads/pipeline.service';
import { LeadTimeline } from './lead-timeline';

/**
 * One enquiry: who it is, what they asked, what has been done, and the three things a salesperson
 * does next - log what happened, move it on, or say it is spam.
 *
 * The stage form asks for a reason in the same submit as the stage, because the server refuses a
 * disqualification without one and a screen that discovers that after the fact makes the person
 * type it twice (BR-LEAD-09).
 */
@Component({
  selector: 'app-lead-detail',
  imports: [DatePipe, FormsModule, RouterLink, LeadTimeline],
  template: `
    @if (lead(); as item) {
      <section data-testid="lead-detail">
        <p class="crumb"><a routerLink="/admin/leads">Enquiries</a></p>

        <header class="head">
          <div>
            <h1>{{ item.fullName }}</h1>
            <p class="muted">
              {{ item.companyName ?? 'No company given' }}
              @if (item.email) {
                · <a [href]="'mailto:' + item.email">{{ item.email }}</a>
              }
              @if (item.phone) {
                · {{ item.phone }}
              }
            </p>
          </div>

          <dl class="facts">
            <div>
              <dt>Stage</dt>
              <dd data-testid="lead-stage">{{ item.stage }}</dd>
            </div>
            <div>
              <dt>Arrived</dt>
              <dd>{{ item.createdAtUtc | date: 'd MMM y, HH:mm' }}</dd>
            </div>
            <div>
              <dt>First reply</dt>
              <dd data-testid="response-time">
                @if (item.firstResponseBusinessMinutes !== null) {
                  {{ responseTime(item) }}
                } @else {
                  {{ countdown(item) }}
                }
              </dd>
            </div>
          </dl>
        </header>

        @if (item.message) {
          <blockquote data-testid="lead-message">{{ item.message }}</blockquote>
        }

        @if (item.disqualifyReason) {
          <p class="reason" data-testid="disqualify-reason">
            Disqualified: {{ item.disqualifyReason }}
          </p>
        }

        @if (item.organisationName) {
          <p data-testid="converted-to">Converted to {{ item.organisationName }}.</p>
        }

        @if (duplicates().length) {
          <aside class="duplicates" data-testid="duplicate-suggestions">
            <h2>Possibly the same person</h2>
            <ul>
              @for (match of duplicates(); track match.id) {
                <li>
                  <a [routerLink]="['/admin/leads', match.id]">{{ match.fullName }}</a>
                  <span class="muted">{{ match.reason }}</span>
                </li>
              }
            </ul>
            <!-- Suggested, never merged. A person decides (REQ-LEAD-017). -->
            <p class="muted">Nothing is merged until you say so.</p>
          </aside>
        }

        @if (error(); as message) {
          <p class="error" role="alert" data-testid="lead-error">{{ message }}</p>
        }

        <div class="actions">
          <form (ngSubmit)="logActivity(item.id)" class="panel">
            <h2>Record what happened</h2>

            <label for="activity-type">What</label>
            <select id="activity-type" name="activityType" [(ngModel)]="activityType">
              <option value="Note">Note</option>
              <option value="Call">Call</option>
              <option value="EmailOut">Email sent</option>
              <option value="EmailIn">Email received</option>
              <option value="Meeting">Meeting</option>
            </select>

            <label for="activity-body">Details</label>
            <textarea id="activity-body" name="body" rows="3" [(ngModel)]="activityBody"></textarea>

            <label for="activity-due">Remind me on (optional)</label>
            <input id="activity-due" name="dueAt" type="datetime-local" [(ngModel)]="dueAt" />

            <button type="submit" class="button button--primary" data-testid="log-activity">Save</button>
          </form>

          <form (ngSubmit)="changeStage(item.id)" class="panel">
            <h2>Move it on</h2>

            <label for="stage">Stage</label>
            <select id="stage" name="stage" [(ngModel)]="stage">
              @for (option of stageOptions; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>

            <label for="stage-reason">
              Reason
              @if (stage === 'Disqualified') {
                <span class="required" aria-hidden="true">*</span>
                <span class="visually-hidden">(required)</span>
              }
            </label>
            <textarea id="stage-reason" name="reason" rows="2" [(ngModel)]="stageReason"></textarea>

            @if (stage === 'Disqualified') {
              <p class="muted" data-testid="reason-hint">
                At least 10 characters. Whoever reads the funnel later is not in the room now.
              </p>
            }

            <button type="submit" class="button" data-testid="change-stage">Move</button>
          </form>
        </div>

        <app-lead-timeline [activities]="item.timeline" />
      </section>
    } @else if (missing()) {
      <p data-testid="lead-missing">That enquiry is not here. It may have been merged into another.</p>
    } @else {
      <p data-testid="lead-loading">Loading the enquiry.</p>
    }
  `,
  styles: `
    .crumb {
      margin: 0 0 var(--space-3);
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-5);
      justify-content: space-between;
    }

    h1 {
      margin: 0;
    }

    .muted {
      color: var(--colour-text-muted);
      margin: var(--space-1) 0 0;
    }

    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-5);
      margin: 0;
    }

    .facts dt {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
    }

    .facts dd {
      font-weight: 600;
      margin: 0;
    }

    blockquote {
      border-left: 2px solid var(--colour-border);
      margin: var(--space-5) 0;
      padding-left: var(--space-4);
      white-space: pre-wrap;
    }

    .reason,
    .error {
      color: var(--colour-danger);
    }

    .required {
      color: var(--colour-danger);
    }

    .duplicates {
      background: var(--colour-surface);
      border-radius: var(--radius-md);
      margin: var(--space-5) 0;
      padding: var(--space-4);
    }

    .duplicates ul {
      display: grid;
      gap: var(--space-2);
      list-style: none;
      margin: 0 0 var(--space-2);
      padding: 0;
    }

    .duplicates .muted {
      display: block;
      font-size: 0.85rem;
    }

    .actions {
      display: grid;
      gap: var(--space-5);
      grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
      margin: var(--space-5) 0;
    }

    .panel {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md);
      display: grid;
      gap: var(--space-2);
      padding: var(--space-4);
    }

    .panel h2 {
      font-size: 1.05rem;
      margin: 0 0 var(--space-2);
    }

    .panel button {
      justify-self: start;
      margin-top: var(--space-3);
    }
  `,
})
export class LeadDetail {
  protected readonly stageOptions = ['Contacted', 'Qualified', 'Nurturing', 'Disqualified'];

  protected activityType = 'Note';
  protected activityBody = '';
  protected dueAt = '';
  protected stage = 'Contacted';
  protected stageReason = '';

  protected readonly lead = signal<LeadDetailModel | null>(null);
  protected readonly duplicates = signal<readonly DuplicateSuggestion[]>([]);
  protected readonly missing = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly pipeline = inject(PipelineService);
  private readonly route = inject(ActivatedRoute);

  constructor() {
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');

      if (id) {
        this.load(id);
      }
    });
  }

  protected countdown(item: LeadDetailModel): string {
    const minutes = Math.round((Date.parse(item.slaDueAtUtc) - Date.now()) / 60000);
    return slaCountdown(minutes);
  }

  protected responseTime(item: LeadDetailModel): string {
    const minutes = item.firstResponseBusinessMinutes ?? 0;
    const hours = Math.floor(minutes / 60);

    // Working hours, which is what the promise on the contact page is measured in.
    return hours > 0 ? `${hours}h ${minutes % 60}m of working time` : `${minutes}m of working time`;
  }

  protected logActivity(id: string): void {
    if (!this.activityBody.trim()) {
      this.error.set('Say what happened. An empty note is not a record.');
      return;
    }

    this.error.set(null);

    this.pipeline
      .addActivity(id, {
        activityType: this.activityType,
        direction: this.activityType === 'EmailIn' ? 'Inbound' : this.activityType === 'Note' ? 'Internal' : 'Outbound',
        body: this.activityBody.trim(),
        dueAtUtc: this.dueAt ? new Date(this.dueAt).toISOString() : null,
      })
      .subscribe({
        next: () => {
          this.activityBody = '';
          this.dueAt = '';
          this.load(id);
        },
        error: () => this.error.set('That could not be saved. Try again.'),
      });
  }

  protected changeStage(id: string): void {
    this.error.set(null);

    this.pipeline.changeStage(id, this.stage, this.stageReason.trim() || null).subscribe({
      next: () => {
        this.stageReason = '';
        this.load(id);
      },

      // The server's own words rather than a generic failure: it knows whether the reason was too
      // short or the move was not allowed, and the person needs to be told which.
      error: (response: { error?: { detail?: string } }) =>
        this.error.set(response.error?.detail ?? 'That move was refused.'),
    });
  }

  private load(id: string): void {
    this.pipeline.lead(id).subscribe({
      next: (lead) => {
        this.lead.set(lead);
        this.missing.set(false);
      },
      error: () => {
        this.lead.set(null);
        this.missing.set(true);
      },
    });

    this.pipeline.duplicates(id).subscribe({
      next: (matches) => this.duplicates.set(matches),
      error: () => this.duplicates.set([]),
    });
  }
}
