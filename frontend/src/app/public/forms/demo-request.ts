import { Component, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { EnquiryForm } from './enquiry-form';

/**
 * The demo request. Opened from a product page it already knows which product, because the address
 * carries it (REQ-LEAD-003).
 */
@Component({
  selector: 'app-demo-request',
  imports: [EnquiryForm],
  template: `
    <app-enquiry-form formKey="request-demo" />

    <aside class="aside" data-testid="demo-aside">
      <h2>What a demo looks like</h2>
      <p>
        Half an hour, on a call, using your own examples rather than ours. If it is not a fit we will
        say so on the call rather than sending a proposal.
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
export class DemoRequest {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  constructor() {
    this.title.setTitle('See it working - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Book a walkthrough of the product using your own examples.',
    });
  }
}
