import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

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
  imports: [RouterOutlet, RouterLink],
  template: `
    <a class="skip-link" href="#main">Skip to content</a>

    <div class="shell" data-testid="app-shell">
      <header class="shell__header">
        <a class="shell__brand" routerLink="/" aria-label="Software Management home">
          <span class="shell__mark" aria-hidden="true">SM</span>
          <span class="shell__name">Software Management</span>
        </a>

        <nav class="shell__nav" aria-label="Primary">
          <a routerLink="/">Home</a>
          <a routerLink="/services">Services</a>
          <a routerLink="/about">About</a>
        </nav>
      </header>

      <main id="main" class="shell__main" tabindex="-1">
        <router-outlet />
      </main>

      <footer class="shell__footer">
        <p>Built and supported in Noida, India.</p>
      </footer>
    </div>
  `,
  styleUrl: './shell.scss',
})
export class Shell {}
