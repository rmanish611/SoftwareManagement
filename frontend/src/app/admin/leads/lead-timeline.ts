import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { LeadActivity } from '../../core/leads/pipeline.service';

/**
 * What has happened to this enquiry, newest first.
 *
 * A list rather than a table: it is a narrative, and the thing a reader wants is the order and the
 * words, not the ability to sort by column. Each entry says who, because "the stage changed" is a
 * fact and "Sales moved it on Tuesday" is an account.
 */
@Component({
  selector: 'app-lead-timeline',
  imports: [DatePipe],
  template: `
    <section data-testid="lead-timeline" aria-labelledby="timeline-heading">
      <h2 id="timeline-heading">History</h2>

      @if (activities().length) {
        <ol class="timeline">
          @for (activity of activities(); track activity.id) {
            <li [attr.data-testid]="'activity-' + activity.id" [class.system]="isSystem(activity)">
              <p class="what">
                <strong>{{ label(activity) }}</strong>
                <span class="when">{{ activity.occurredAtUtc | date: 'd MMM y, HH:mm' }}</span>
              </p>

              @if (activity.body) {
                <p class="body">{{ activity.body }}</p>
              }

              <p class="who">
                {{ activity.actor }}
                @if (activity.dueAtUtc) {
                  <span [attr.data-testid]="'reminder-' + activity.id">
                    · reminder {{ activity.isCompleted ? 'sent' : 'due' }}
                    {{ activity.dueAtUtc | date: 'd MMM, HH:mm' }}
                  </span>
                }
              </p>
            </li>
          }
        </ol>
      } @else {
        <p data-testid="timeline-empty">Nothing has been recorded yet.</p>
      }
    </section>
  `,
  styles: `
    .timeline {
      display: grid;
      gap: var(--space-4);
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .timeline li {
      border-left: 2px solid var(--colour-accent);
      padding-left: var(--space-4);
    }

    /* Something the system did rather than a person. Drawn back so the human record reads first. */
    .timeline li.system {
      border-left-color: var(--colour-border);
    }

    .what {
      align-items: baseline;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      margin: 0;
    }

    .when,
    .who {
      color: var(--colour-text-muted);
      font-size: 0.85rem;
    }

    .body {
      margin: var(--space-1) 0;
      white-space: pre-wrap;
    }

    .who {
      margin: 0;
    }
  `,
})
export class LeadTimeline {
  readonly activities = input.required<readonly LeadActivity[]>();

  protected isSystem(activity: LeadActivity): boolean {
    return activity.activityType === 'SystemEvent' || activity.activityType === 'StageChange';
  }

  /**
   * A sentence rather than an enum name. "StageChange" is what the database calls it; "Moved from
   * New to Contacted" is what happened.
   */
  protected label(activity: LeadActivity): string {
    if (activity.activityType === 'StageChange' && activity.fromStage && activity.toStage) {
      return `Moved from ${activity.fromStage} to ${activity.toStage}`;
    }

    switch (activity.activityType) {
      case 'Call':
        return activity.direction === 'Inbound' ? 'Call in' : 'Call out';
      case 'EmailOut':
        return 'Email sent';
      case 'EmailIn':
        return 'Email received';
      case 'Meeting':
        return 'Meeting';
      case 'SystemEvent':
        return 'System';
      default:
        return 'Note';
    }
  }
}
