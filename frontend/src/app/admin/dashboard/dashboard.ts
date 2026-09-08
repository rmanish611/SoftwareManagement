import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * The landing screen after sign-in. The counters it will show arrive in P12; today it confirms who
 * is signed in and what they are allowed to do, which is what makes a permission mistake visible on
 * day one rather than at the end.
 */
@Component({
  selector: 'app-dashboard',
  template: `
    <section data-testid="admin-dashboard">
      <h1>Dashboard</h1>
      <p>Signed in as {{ auth.user()?.email }} ({{ roles() }}).</p>
      <p class="hint">
        Lead, quote and renewal counters appear here once those modules exist. Until then this
        screen exists to prove the session and the permission set are real.
      </p>
    </section>
  `,
  styles: `
    .hint {
      color: var(--colour-text-muted);
      max-width: 60ch;
    }
  `,
})
export class Dashboard {
  protected readonly auth = inject(AuthService);

  protected roles(): string {
    return this.auth.user()?.roles.join(', ') ?? 'no role';
  }
}
