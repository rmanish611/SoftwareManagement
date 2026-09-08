import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * The back-office sign-in screen.
 *
 * The failure message never distinguishes an unknown address from a wrong password, matching what
 * the API returns (BR-IAM-02). Telling a visitor which half was wrong tells an attacker which
 * addresses are worth attacking.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <div class="login" data-testid="admin-login">
      <h1>Sign in</h1>

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label for="email">Email</label>
        <input
          id="email"
          type="email"
          formControlName="email"
          autocomplete="username"
          [attr.aria-invalid]="showEmailError() ? 'true' : null"
          [attr.aria-describedby]="showEmailError() ? 'email-error' : null"
        />
        @if (showEmailError()) {
          <p class="field-error" id="email-error">Enter the email address you sign in with.</p>
        }

        <label for="password">Password</label>
        <input
          id="password"
          type="password"
          formControlName="password"
          autocomplete="current-password"
          [attr.aria-invalid]="showPasswordError() ? 'true' : null"
          [attr.aria-describedby]="showPasswordError() ? 'password-error' : null"
        />
        @if (showPasswordError()) {
          <p class="field-error" id="password-error">Enter your password.</p>
        }

        @if (error(); as message) {
          <p class="form-error" role="alert" data-testid="login-error">{{ message }}</p>
        }

        <button type="submit" [disabled]="busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    .login {
      margin: 0 auto;
      max-width: 24rem;
    }

    form {
      display: grid;
      gap: var(--space-2);
    }

    label {
      font-weight: 600;
      margin-top: var(--space-3);
    }

    input {
      background: var(--colour-background);
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      color: var(--colour-text);
      font: inherit;
      min-height: 2.5rem;
      padding: var(--space-2) var(--space-3);
    }

    button {
      background: var(--colour-accent);
      border: none;
      border-radius: var(--radius-sm);
      color: var(--colour-accent-contrast);
      cursor: pointer;
      font: inherit;
      font-weight: 600;
      margin-top: var(--space-4);
      min-height: 2.75rem;
      padding: var(--space-2) var(--space-4);
    }

    button[disabled] {
      cursor: progress;
      opacity: 0.7;
    }

    .field-error,
    .form-error {
      color: var(--colour-danger);
      font-size: 0.9rem;
      margin: 0;
    }

    .form-error {
      margin-top: var(--space-3);
    }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly submitted = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected showEmailError(): boolean {
    return this.submitted() && this.form.controls.email.invalid;
  }

  protected showPasswordError(): boolean {
    return this.submitted() && this.form.controls.password.invalid;
  }

  protected submit(): void {
    this.submitted.set(true);
    this.error.set(null);

    if (this.form.invalid) {
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.busy.set(true);

    this.auth.signIn(email, password).subscribe({
      next: () => {
        this.busy.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/admin';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (response: { status?: number }) => {
        this.busy.set(false);
        this.error.set(
          response.status === 423
            ? 'Too many failed attempts. Try again in a few minutes.'
            : 'Email or password is incorrect.',
        );
      },
    });
  }
}
