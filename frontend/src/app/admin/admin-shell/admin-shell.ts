import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * The signed-in back-office frame: who is signed in, the navigation their permissions allow, and
 * the routed screen.
 *
 * Navigation is filtered by permission for tidiness only. Every link behind it leads to an endpoint
 * that checks the same permission on the server, so a user who guesses a URL is refused there.
 */
@Component({
  selector: 'app-admin-shell',
  imports: [RouterOutlet, RouterLink],
  template: `
    <div class="admin" data-testid="admin-shell">
      <header class="admin__bar">
        <a class="admin__brand" routerLink="/admin">Software Management</a>

        <nav class="admin__nav" aria-label="Admin sections">
          @for (item of visibleNavigation(); track item.path) {
            <a [routerLink]="item.path">{{ item.label }}</a>
          }
        </nav>

        <div class="admin__user">
          <span data-testid="signed-in-as">{{ auth.user()?.fullName }}</span>
          <button type="button" (click)="signOut()">Sign out</button>
        </div>
      </header>

      <main class="admin__main">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .admin {
      display: grid;
      grid-template-rows: auto 1fr;
      min-height: 100dvh;
    }

    .admin__bar {
      align-items: center;
      background: var(--colour-surface);
      border-bottom: 1px solid var(--colour-border);
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      justify-content: space-between;
      padding: var(--space-3) var(--space-5);
    }

    .admin__brand {
      color: inherit;
      font-weight: 600;
      text-decoration: none;
    }

    .admin__nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
    }

    .admin__nav a {
      align-items: center;
      color: var(--colour-text-muted);
      display: inline-flex;
      min-height: 24px;
      text-decoration: none;
    }

    .admin__nav a:hover,
    .admin__nav a:focus-visible {
      color: var(--colour-text);
      text-decoration: underline;
    }

    .admin__user {
      align-items: center;
      display: flex;
      gap: var(--space-3);
    }

    .admin__user button {
      background: none;
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-sm);
      color: inherit;
      cursor: pointer;
      font: inherit;
      min-height: 2rem;
      padding: var(--space-1) var(--space-3);
    }

    .admin__main {
      padding: var(--space-5);
    }
  `,
})
export class AdminShell {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly navigation = [
    { path: '/admin', label: 'Dashboard', permission: 'dashboard.read' },
    { path: '/admin/products', label: 'Products', permission: 'catalog.product.read' },
    { path: '/admin/users', label: 'Users', permission: 'admin.user.read' },
    { path: '/admin/login-attempts', label: 'Sign-in log', permission: 'admin.audit.read' },
  ];

  protected visibleNavigation(): { path: string; label: string }[] {
    return this.navigation
      .filter((item) => this.auth.can(item.permission))
      .map(({ path, label }) => ({ path, label }));
  }

  protected signOut(): void {
    this.auth.signOut().subscribe({
      next: () => void this.router.navigateByUrl('/admin/login'),
      error: () => void this.router.navigateByUrl('/admin/login'),
    });
  }
}
