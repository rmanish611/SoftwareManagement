import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface LeadSummary {
  readonly id: string;
  readonly fullName: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly companyName: string | null;
  readonly stage: string;
  readonly source: string;
  readonly product: string | null;
  readonly createdAtUtc: string;
  readonly slaDueAtUtc: string;
  readonly firstResponseAtUtc: string | null;
  readonly isBreached: boolean;
  readonly minutesToSlaDue: number | null;
  readonly isStale: boolean;
}

export interface LeadActivity {
  readonly id: string;
  readonly activityType: string;
  readonly direction: string;
  readonly body: string | null;
  readonly occurredAtUtc: string;
  readonly fromStage: string | null;
  readonly toStage: string | null;
  readonly dueAtUtc: string | null;
  readonly isCompleted: boolean;
  readonly actor: string;
}

export interface LeadDetail {
  readonly id: string;
  readonly fullName: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly companyName: string | null;
  readonly message: string | null;
  readonly stage: string;
  readonly source: string;
  readonly product: string | null;
  readonly createdAtUtc: string;
  readonly slaDueAtUtc: string;
  readonly firstResponseAtUtc: string | null;
  readonly firstResponseBusinessMinutes: number | null;
  readonly disqualifyReason: string | null;
  readonly organisationId: string | null;
  readonly organisationName: string | null;
  readonly timeline: readonly LeadActivity[];
}

export interface DuplicateSuggestion {
  readonly id: string;
  readonly fullName: string;
  readonly email: string | null;
  readonly companyName: string | null;
  readonly stage: string;
  readonly createdAtUtc: string;
  readonly reason: string;
}

/** The three questions the inbox is opened to ask, as the API names them. */
export type SavedView = 'all' | 'unanswered' | 'breached' | 'stale';

/**
 * The pipeline API.
 *
 * Every call is one HTTP round trip and nothing is cached here: a salesperson has this screen open
 * beside a phone call, and a stale stage on it is worse than a slow one.
 */
@Injectable({ providedIn: 'root' })
export class PipelineService {
  private readonly http = inject(HttpClient);

  leads(options: { view?: SavedView; stage?: string; search?: string } = {}): Observable<LeadSummary[]> {
    let params = new HttpParams();

    if (options.view && options.view !== 'all') {
      params = params.set('view', options.view);
    }

    if (options.stage) {
      params = params.set('stage', options.stage);
    }

    if (options.search) {
      params = params.set('search', options.search);
    }

    return this.http.get<LeadSummary[]>('/api/v1/leads', { params });
  }

  lead(id: string): Observable<LeadDetail> {
    return this.http.get<LeadDetail>(`/api/v1/leads/${id}`);
  }

  duplicates(id: string): Observable<DuplicateSuggestion[]> {
    return this.http.get<DuplicateSuggestion[]>(`/api/v1/leads/${id}/duplicates`);
  }

  changeStage(id: string, stage: string, reason: string | null): Observable<void> {
    return this.http.post<void>(`/api/v1/leads/${id}/stage`, { stage, reason });
  }

  addActivity(
    id: string,
    activity: { activityType: string; direction: string; body: string; dueAtUtc?: string | null },
  ): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`/api/v1/leads/${id}/activities`, activity);
  }

  markSpam(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/leads/${id}/spam`, null);
  }
}

/**
 * How long until the reply is owed, or how long it is overdue, in words.
 *
 * A countdown in minutes is unreadable at a glance and a raw timestamp makes the reader do the
 * arithmetic. This is the number the person actually wants: "4h left", "2d late".
 */
export function slaCountdown(minutes: number | null): string {
  if (minutes === null) {
    return 'Answered';
  }

  const overdue = minutes < 0;
  const total = Math.abs(minutes);
  const days = Math.floor(total / (60 * 24));
  const hours = Math.floor((total % (60 * 24)) / 60);

  // "2d 0h" is noise. Whole days read as days, and only a remainder earns the second unit.
  const amount =
    days > 0 ? (hours > 0 ? `${days}d ${hours}h` : `${days}d`) : hours > 0 ? `${hours}h` : `${total}m`;

  return overdue ? `${amount} late` : `${amount} left`;
}
