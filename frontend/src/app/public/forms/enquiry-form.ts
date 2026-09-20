import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  EnquiryForm as EnquiryFormDefinition,
  EnquiryService,
  enquiryProblem,
} from '../../core/leads/enquiry.service';
import { ConsentField } from './consent-field';

/**
 * Renders whatever the server says a form asks, and sends it.
 *
 * Three things here exist because of how these forms are actually abused or misused, rather than
 * because of a specification:
 *
 * - The hidden field is present but visually and programmatically removed, so a person never meets
 *   it and a form-filling bot does. It is named by the server (BR-LEAD-03).
 * - The campaign parameters in the address bar travel with the submission, so the owner can see
 *   which page earned the enquiry without any tracking of the person (REQ-LEAD-008).
 * - On success the form is replaced by the reference, not merely cleared. Somebody who has just
 *   sent a message wants proof it arrived, and a cleared form looks like it did nothing.
 */
@Component({
  selector: 'app-enquiry-form',
  imports: [FormsModule, ConsentField, RouterLink],
  template: `
    @if (accepted(); as success) {
      <section class="done" data-testid="enquiry-accepted">
        <h1>Thank you</h1>
        <p>{{ success.message }}</p>
        <p>
          Your reference is <strong data-testid="enquiry-reference">{{ success.reference }}</strong>.
          Please quote it if you write to us again.
        </p>
        <p><a routerLink="/products">See what we build</a></p>
      </section>
    } @else if (form(); as definition) {
      <section data-testid="enquiry-form">
        <h1>{{ definition.title }}</h1>

        @if (definition.intro) {
          <p class="lead">{{ definition.intro }}</p>
        }

        @if (!definition.isEnabled) {
          <p class="closed" data-testid="form-closed">
            This form is closed at the moment. Please email us and we will reply.
          </p>
        } @else {
          <form (ngSubmit)="send()" novalidate>
            @for (field of definition.fields; track field.name) {
              <div class="field">
                <label [attr.for]="'field-' + field.name">
                  {{ field.label }}
                  @if (field.isRequired) {
                    <span class="required" aria-hidden="true">*</span>
                    <span class="visually-hidden">(required)</span>
                  }
                </label>

                @if (field.fieldType === 'TextArea') {
                  <textarea
                    [id]="'field-' + field.name"
                    [name]="field.name"
                    rows="5"
                    [maxlength]="field.maxLength"
                    [ngModel]="answers()[field.name] ?? ''"
                    (ngModelChange)="setAnswer(field.name, $event)"
                    [attr.aria-invalid]="fieldError() === field.name ? 'true' : null"
                  ></textarea>
                } @else if (field.fieldType === 'ProductPicker') {
                  <select
                    [id]="'field-' + field.name"
                    [name]="field.name"
                    [ngModel]="answers()[field.name] ?? ''"
                    (ngModelChange)="setAnswer(field.name, $event)"
                    [attr.aria-invalid]="fieldError() === field.name ? 'true' : null"
                  >
                    <option value="">Please choose</option>
                    @for (product of definition.products; track product.slug) {
                      <option [value]="product.slug">{{ product.name }}</option>
                    }
                  </select>
                } @else {
                  <input
                    [id]="'field-' + field.name"
                    [name]="field.name"
                    [type]="inputType(field.fieldType)"
                    [maxlength]="field.maxLength"
                    [ngModel]="answers()[field.name] ?? ''"
                    (ngModelChange)="setAnswer(field.name, $event)"
                    [attr.aria-invalid]="fieldError() === field.name ? 'true' : null"
                  />
                }
              </div>
            }

            <!--
              The hidden field. It is removed from the accessibility tree and from the tab order as
              well as from view, so nobody using a screen reader or a keyboard ever meets it.
            -->
            <div class="honeypot" aria-hidden="true">
              <label [attr.for]="'field-' + definition.honeypotField">Leave this empty</label>
              <input
                [id]="'field-' + definition.honeypotField"
                [name]="definition.honeypotField"
                type="text"
                tabindex="-1"
                autocomplete="off"
                [ngModel]="honeypot()"
                (ngModelChange)="honeypot.set($event)"
              />
            </div>

            <app-consent-field
              [text]="definition.consentText"
              [(checked)]="consent"
              [error]="fieldError() === 'consent' ? error() : null"
            />

            @if (error() && fieldError() !== 'consent') {
              <p class="error" role="alert" data-testid="enquiry-error">{{ error() }}</p>
            }

            <button type="submit" data-testid="enquiry-submit" [disabled]="sending()">
              {{ sending() ? 'Sending' : definition.submitLabel }}
            </button>
          </form>
        }
      </section>
    } @else {
      <p data-testid="enquiry-loading">Loading the form.</p>
    }
  `,
  styles: `
    .lead,
    .closed {
      color: var(--colour-text-muted);
      max-width: 60ch;
    }

    .field {
      display: grid;
      gap: var(--space-1);
      margin-top: var(--space-4);
      max-width: 34rem;
    }

    .required {
      color: var(--colour-danger, #a11);
    }

    /* Off screen rather than display:none, so a bot that reads the markup still finds it. */
    .honeypot {
      left: -9999px;
      position: absolute;
      top: -9999px;
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

    .error {
      color: var(--colour-danger, #a11);
      max-width: 60ch;
    }

    .done {
      max-width: 60ch;
    }

    button[type='submit'] {
      background: var(--colour-accent, #0b5cab);
      border: none;
      border-radius: var(--radius-sm);
      color: #fff;
      cursor: pointer;
      font: inherit;
      margin-top: var(--space-4);
      min-height: 2.75rem;
      padding: 0 var(--space-5);
    }
  `,
})
export class EnquiryForm implements OnInit {
  private static readonly UtmKeys = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content'];

  private readonly enquiries = inject(EnquiryService);
  private readonly route = inject(ActivatedRoute);

  readonly formKey = input.required<string>();

  protected readonly form = signal<EnquiryFormDefinition | null>(null);
  protected readonly answers = signal<Record<string, string | null>>({});
  protected readonly consent = signal(false);
  protected readonly honeypot = signal('');
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = signal<string | null>(null);
  protected readonly accepted = signal<{ reference: string; message: string } | null>(null);

  private utm: Record<string, string> = {};

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;

    for (const key of EnquiryForm.UtmKeys) {
      const value = params.get(key);
      if (value) {
        this.utm[key] = value;
      }
    }

    // A demo or quote request opened from a product page arrives knowing which product it is
    // about, so the visitor does not have to explain what they were just looking at
    // (REQ-LEAD-003).
    const product = params.get('product');
    if (product) {
      this.answers.set({ product });
    }

    this.enquiries.form(this.formKey()).subscribe({
      next: (definition) => this.form.set(definition),
      error: () => this.error.set('This form could not be loaded. Please email us instead.'),
    });
  }

  protected inputType(fieldType: string): string {
    switch (fieldType) {
      case 'Email':
        return 'email';
      case 'Phone':
        return 'tel';
      default:
        return 'text';
    }
  }

  protected setAnswer(name: string, value: string): void {
    this.answers.set({ ...this.answers(), [name]: value });
  }

  protected send(): void {
    if (this.sending()) {
      return;
    }

    this.error.set(null);
    this.fieldError.set(null);
    this.sending.set(true);

    this.enquiries
      .submit(this.formKey(), {
        answers: this.answers(),
        consent: this.consent(),

        // No captcha widget is loaded on this page yet; the token is supplied by the challenge
        // script the owner enables in settings. Until then the server decides what to do with an
        // absent token, which is to refuse it, and that refusal is visible rather than silent.
        captchaToken: this.captchaToken(),
        honeypot: this.honeypot() || null,
        utm: this.utm,
      })
      .subscribe({
        next: (success) => {
          this.accepted.set(success);
          this.sending.set(false);
        },
        error: (failure: unknown) => {
          const problem = enquiryProblem(failure);
          this.error.set(problem.message);
          this.fieldError.set(problem.field);
          this.sending.set(false);
        },
      });
  }

  /**
   * The token the challenge widget leaves on the page. It is read from the DOM rather than held in
   * a service because the widget writes it there itself, and reading it at send time means a token
   * that expired while the person was typing is not sent stale.
   */
  private captchaToken(): string | null {
    if (typeof document === 'undefined') {
      return null;
    }

    const field = document.querySelector<HTMLInputElement>('[name="cf-turnstile-response"]');
    return field?.value || null;
  }
}
