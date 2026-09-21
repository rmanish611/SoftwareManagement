import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiProblem, NewOrganisation, OrganisationSummary, SalesService } from '../../core/sales/sales.service';

/**
 * The customer list, with the form for adding one beside it.
 *
 * The GSTIN is the field that goes wrong, so the refusal from the server is shown against that
 * field with the pattern the server sent, rather than as a banner saying something failed
 * (REQ-CUST-001).
 */
@Component({
  selector: 'app-organisation-list',
  imports: [FormsModule],
  template: `
    <section data-testid="organisation-list">
      <header class="head">
        <h1>Customers</h1>
        <form class="search" (ngSubmit)="applySearch()" role="search">
          <label class="visually-hidden" for="customer-search">Search customers</label>
          <input
            id="customer-search"
            name="search"
            type="search"
            [(ngModel)]="search"
            placeholder="Name or GSTIN"
          />
          <button type="submit" class="button" data-testid="customer-search-submit">Search</button>
        </form>
      </header>

      <form class="new" (ngSubmit)="create()">
        <h2>Add a customer</h2>

        <div class="field">
          <label for="legal-name">Registered name</label>
          <input id="legal-name" name="legalName" [(ngModel)]="legalName" />
        </div>

        <div class="field">
          <label for="gstin">GSTIN (optional)</label>
          <input
            id="gstin"
            name="gstin"
            [(ngModel)]="gstin"
            [attr.aria-invalid]="errorField() === 'gstin' ? 'true' : null"
            [attr.aria-describedby]="errorField() === 'gstin' ? 'gstin-error' : null"
          />
          @if (errorField() === 'gstin') {
            <p class="error" id="gstin-error" role="alert" data-testid="gstin-error">{{ error() }}</p>
          }
        </div>

        <div class="field">
          <label for="city">City</label>
          <input id="city" name="city" [(ngModel)]="city" />
        </div>

        @if (error() && errorField() !== 'gstin') {
          <p class="error" role="alert" data-testid="customer-error">{{ error() }}</p>
        }

        <button type="submit" class="button button--primary" data-testid="add-customer">Add customer</button>
      </form>

      @if (loading()) {
        <p data-testid="customers-loading">Loading the customers.</p>
      } @else if (organisations().length) {
        <table>
          <caption class="visually-hidden">Customers, their GSTIN and how many people we know there</caption>
          <thead>
            <tr>
              <th scope="col">Customer</th>
              <th scope="col">GSTIN</th>
              <th scope="col">Where</th>
              <th scope="col">Status</th>
              <th scope="col">Contacts</th>
            </tr>
          </thead>
          <tbody>
            @for (organisation of organisations(); track organisation.id) {
              <tr [attr.data-testid]="'organisation-' + organisation.id">
                <td>{{ organisation.displayName }}</td>
                <td>{{ organisation.gstin ?? '—' }}</td>
                <td>{{ organisation.city ?? '—' }}</td>
                <td>{{ organisation.status }}</td>
                <td class="count">{{ organisation.contactCount }}</td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p data-testid="customers-empty">
          No customers match that yet. A converted lead appears here, and so does anything added above.
        </p>
      }
    </section>
  `,
  styles: `
    .head {
      align-items: baseline;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      justify-content: space-between;
    }

    .search {
      display: flex;
      gap: var(--space-2);
      max-width: 24rem;
    }

    .new {
      border: 1px solid var(--colour-border);
      border-radius: var(--radius-md);
      display: grid;
      gap: var(--space-3);
      margin: var(--space-5) 0;
      max-width: 34rem;
      padding: var(--space-4);
    }

    .new h2 {
      font-size: 1.05rem;
      margin: 0;
    }

    .field {
      display: grid;
      gap: var(--space-1);
    }

    .error {
      color: var(--colour-danger);
      margin: 0;
    }

    table {
      border-collapse: collapse;
      width: 100%;
    }

    th,
    td {
      border-bottom: 1px solid var(--colour-border);
      padding: var(--space-2) var(--space-3);
      text-align: left;
    }

    .count {
      text-align: right;
    }

    .new button {
      justify-self: start;
    }
  `,
})
export class OrganisationList {
  protected search = '';
  protected legalName = '';
  protected gstin = '';
  protected city = '';

  protected readonly organisations = signal<readonly OrganisationSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly errorField = signal<string | null>(null);

  private readonly sales = inject(SalesService);

  constructor() {
    this.load();
  }

  protected applySearch(): void {
    this.load();
  }

  protected create(): void {
    if (!this.legalName.trim()) {
      this.fail('A company needs a name.', 'legalName');
      return;
    }

    const body: NewOrganisation = {
      legalName: this.legalName.trim(),
      gstin: this.gstin.trim() || null,
      city: this.city.trim() || null,
    };

    this.error.set(null);
    this.errorField.set(null);

    this.sales.createOrganisation(body).subscribe({
      next: () => {
        this.legalName = '';
        this.gstin = '';
        this.city = '';
        this.load();
      },

      // The server's own words against the server's own field. It knows whether the number was
      // malformed or already taken, and a banner saying "something went wrong" throws that away.
      error: (response: { error?: ApiProblem }) =>
        this.fail(response.error?.detail ?? 'That customer could not be saved.', response.error?.field ?? null),
    });
  }

  private fail(message: string, field: string | null): void {
    this.error.set(message);
    this.errorField.set(field);
  }

  private load(): void {
    this.loading.set(true);

    this.sales.organisations(this.search.trim() || undefined).subscribe({
      next: (rows) => {
        this.organisations.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.organisations.set([]);
        this.loading.set(false);
      },
    });
  }
}
