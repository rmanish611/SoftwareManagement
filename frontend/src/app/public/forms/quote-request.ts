import { Component, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { EnquiryForm } from './enquiry-form';

/** The quote request. It insists on a product, because a quote for nothing in particular is not one. */
@Component({
  selector: 'app-quote-request',
  imports: [EnquiryForm],
  template: `
    <app-enquiry-form formKey="request-quote" />

    <aside class="aside" data-testid="quote-aside">
      <h2>What the quote covers</h2>
      <p>
        A written price for the plan and the number of people who will use it, the one-off setup, and
        what is included in support. Prices exclude 18% GST.
      </p>
    </aside>
  `,
  styles: `
    .aside {
      border-top: 1px solid var(--colour-border);
      color: var(--colour-text-muted);
      margin-top: var(--space-6);
      max-width: 60ch;
      padding-top: var(--space-4);
    }
  `,
})
export class QuoteRequest {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  constructor() {
    this.title.setTitle('Get a price - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Tell us the size of the operation and we will send a written quote.',
    });
  }
}
