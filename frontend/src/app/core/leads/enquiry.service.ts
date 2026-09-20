import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface EnquiryFormField {
  readonly name: string;
  readonly label: string;
  readonly fieldType: 'Text' | 'Email' | 'Phone' | 'TextArea' | 'Select' | 'Checkbox' | 'ProductPicker';
  readonly isRequired: boolean;
  readonly maxLength: number | null;
  readonly options: readonly string[];
}

export interface EnquiryForm {
  readonly key: string;
  readonly title: string;
  readonly intro: string | null;
  readonly submitLabel: string;
  readonly consentText: string;
  readonly consentVersion: number;
  readonly isEnabled: boolean;

  /** The hidden field's name comes from the server, so the client cannot get it wrong. */
  readonly honeypotField: string;
  readonly fields: readonly EnquiryFormField[];
  readonly products: readonly { slug: string; name: string }[];
}

export interface EnquiryAccepted {
  readonly reference: string;
  readonly message: string;
}

export interface EnquirySubmission {
  readonly answers: Record<string, string | null>;
  readonly consent: boolean;
  readonly captchaToken: string | null;
  readonly honeypot: string | null;
  readonly utm: Record<string, string>;
}

/**
 * The public enquiry forms.
 *
 * The client renders whatever the server says the form asks, rather than carrying its own copy of
 * the questions. That is what stops the required flags on the two sides drifting apart, and it means
 * the owner can add a question without a deployment.
 */
@Injectable({ providedIn: 'root' })
export class EnquiryService {
  private readonly http = inject(HttpClient);

  form(key: string): Observable<EnquiryForm> {
    return this.http.get<EnquiryForm>(`/api/v1/public/forms/${encodeURIComponent(key)}`);
  }

  submit(key: string, submission: EnquirySubmission): Observable<EnquiryAccepted> {
    return this.http.post<EnquiryAccepted>(
      `/api/v1/public/forms/${encodeURIComponent(key)}/submit`,
      submission,
    );
  }
}

/**
 * Turns a refusal into the sentence to put beside the form, and says which field to point at.
 *
 * A status code is not something a visitor can act on. Each of these has a different next step, so
 * each gets its own words rather than one apologetic paragraph.
 */
export function enquiryProblem(error: unknown): { message: string; field: string | null } {
  const problem = (error as { error?: { detail?: string; title?: string; code?: string; field?: string } } | null)
    ?.error;

  const code = problem?.code;

  if (code === 'RATE_LIMITED') {
    return {
      message: 'We have had a lot of enquiries from your connection just now. Please wait a few minutes and try again.',
      field: null,
    };
  }

  if (code === 'CAPTCHA_INVALID') {
    return {
      message: 'We could not confirm you are a person. Please reload the page and send it again.',
      field: null,
    };
  }

  if (code === 'CONSENT_REQUIRED') {
    return { message: 'Please tick the box so we may reply to you.', field: 'consent' };
  }

  return {
    message: problem?.detail ?? 'That could not be sent. Please check the form and try again.',
    field: problem?.field ?? null,
  };
}
