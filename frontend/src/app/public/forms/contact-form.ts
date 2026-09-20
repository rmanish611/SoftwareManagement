import { Component, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { EnquiryForm } from './enquiry-form';

/** The general enquiry page. */
@Component({
  selector: 'app-contact-form',
  imports: [EnquiryForm],
  template: `
    <app-enquiry-form formKey="contact" />

    <aside class="aside" data-testid="contact-aside">
      <h2>Prefer to write directly?</h2>
      <p>
        Every enquiry is read by a person here, not routed through a call centre. If you would rather
        email, we will reply to that just as quickly.
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
export class ContactForm {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  constructor() {
    this.title.setTitle('Talk to us - Software Management');
    this.meta.updateTag({
      name: 'description',
      content: 'Tell us what you need to run better. We answer every enquiry ourselves.',
    });
  }
}
