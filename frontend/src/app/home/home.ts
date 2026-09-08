import { Component } from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <section class="home" data-testid="home">
      <h1>Software that runs the business, built and supported by one team.</h1>
      <p>
        We design, build and run multi-tenant software: enterprise resource planning, hospital
        management, billing, and school and college administration. Every product on this site is
        one we build ourselves and support directly.
      </p>
    </section>
  `,
  styles: `
    .home {
      display: grid;
      gap: var(--space-4);
      max-width: 60ch;
    }

    h1 {
      font-size: var(--font-size-hero);
      line-height: 1.15;
      margin: 0;
    }

    p {
      color: var(--colour-text-muted);
      font-size: var(--font-size-lead);
      margin: 0;
    }
  `,
})
export class Home {}
