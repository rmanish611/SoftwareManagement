import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

/**
 * The application shell: the header, the navigation, the routed content and the footer that
 * every page shares. It is a separate component from App so that a component test can render
 * the whole chrome without bootstrapping the router configuration of the real application.
 *
 * The data-testid attribute is the anchor the phase gate asserts on: `<app-root>` appearing in
 * index.html proves only that the HTML was served, never that Angular rendered anything.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip-link" href="#main">Skip to content</a>

    <div class="shell" data-testid="app-shell">
      <header class="shell__header">
        <a class="shell__brand" routerLink="/" aria-label="Software Management home">
          <span class="shell__mark" aria-hidden="true">SM</span>
          <span class="shell__name">Software Management</span>
        </a>

        <button
          type="button"
          class="shell__toggle"
          data-testid="nav-toggle"
          [attr.aria-expanded]="navOpen()"
          aria-controls="primary-nav"
          (click)="toggleNav()"
        >
          <span class="shell__toggle-bars" aria-hidden="true"></span>
          <span class="visually-hidden">{{ navOpen() ? 'Close menu' : 'Menu' }}</span>
        </button>

        <nav
          id="primary-nav"
          class="shell__nav"
          [class.shell__nav--open]="navOpen()"
          aria-label="Primary"
        >
          <a routerLink="/" routerLinkActive="is-current" [routerLinkActiveOptions]="{ exact: true }">Home</a>
          <a routerLink="/products" routerLinkActive="is-current">Software</a>
          <a routerLink="/services" routerLinkActive="is-current">Services</a>
          <a routerLink="/projects" routerLinkActive="is-current">Work</a>
          <a routerLink="/developers" routerLinkActive="is-current">Developers</a>
          <a routerLink="/about" routerLinkActive="is-current">About</a>
          <a class="shell__cta" routerLink="/contact" routerLinkActive="is-current">Talk to us</a>
        </nav>
      </header>

      <main id="main" class="shell__main" tabindex="-1">
        <router-outlet />
      </main>

      <footer class="shell__footer">
        <nav class="shell__footer-nav" aria-label="Footer">
          <a routerLink="/products">Software</a>
          <a routerLink="/services">Services</a>
          <a routerLink="/projects">Work</a>
          <a routerLink="/developers">Developers</a>
          <a routerLink="/about">About</a>
          <a routerLink="/request-demo">Request a demo</a>
          <a routerLink="/request-quote">Request a quote</a>
          <a routerLink="/contact">Contact</a>
        </nav>
        <p>Built and supported in Noida, India.</p>
      </footer>
    </div>
  `,
  styleUrl: './shell.scss',
})
export class Shell {
  /**
   * Whether the narrow-screen menu is open.
   *
   * It starts closed, and it starts closed on the server too, so the HTML a crawler and a
   * JavaScript-less browser receive has the navigation in it and a wide screen shows it without
   * anything having to run. The toggle only matters below the breakpoint, where CSS hides the nav
   * until this says otherwise.
   */
  protected readonly navOpen = signal(false);

  private readonly router = inject(Router);

  constructor() {
    // The menu closes when the page changes, rather than on a click handler attached to the
    // navigation itself. Same effect for a mouse, and it also covers the back button and a link
    // followed by keyboard - and it keeps a click handler off an element that is not a control.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.navOpen.set(false));
  }

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }
}
