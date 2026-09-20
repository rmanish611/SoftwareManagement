import { Component, input, model } from '@angular/core';

/**
 * The consent checkbox.
 *
 * It starts unticked, always. A pre-ticked box is not consent, it is an assumption, and under the
 * DPDP Act it is not consent that can be demonstrated either (BR-LEAD-06).
 *
 * The exact words come from the server and are shown beside the box rather than behind a link,
 * because the record that is stored is a record of these words, and a person cannot agree to text
 * they were not shown.
 */
@Component({
  selector: 'app-consent-field',
  template: `
    <div class="consent" data-testid="consent-field">
      <input
        id="consent"
        type="checkbox"
        [checked]="checked()"
        (change)="onToggle($event)"
        [attr.aria-describedby]="'consent-text'"
        [attr.aria-invalid]="error() ? 'true' : null"
      />
      <label for="consent">
        <span id="consent-text" data-testid="consent-text">{{ text() }}</span>
      </label>
    </div>

    @if (error()) {
      <p class="error" role="alert" data-testid="consent-error">{{ error() }}</p>
    }
  `,
  styles: `
    .consent {
      align-items: start;
      display: grid;
      gap: var(--space-3);
      grid-template-columns: auto 1fr;
      margin: var(--space-4) 0;
      max-width: 60ch;
    }

    .consent input {
      /* A checkbox people can actually hit, including on a phone (NFR-ACC-03). */
      height: 1.25rem;
      margin-top: 0.15rem;
      width: 1.25rem;
    }

    .consent label {
      color: var(--colour-text-muted);
      font-size: 0.95rem;
      line-height: 1.5;
    }

    .error {
      color: var(--colour-danger);
      max-width: 60ch;
    }
  `,
})
export class ConsentField {
  readonly text = input.required<string>();
  readonly error = input<string | null>(null);
  readonly checked = model(false);

  protected onToggle(event: Event): void {
    this.checked.set((event.target as HTMLInputElement).checked);
  }
}
