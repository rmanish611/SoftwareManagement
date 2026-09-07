# 02 Domain

## 1. Actors and roles

| ID | Actor | What they do here | Authenticated? |
|---|---|---|---|
| ACT-01 | Visitor (anonymous) | Browses the company site, reads product pages and case studies, opens a live demo, submits an enquiry. The only actor the public internet ever is. | No |
| ACT-02 | Prospect / enquirer | A visitor who has submitted a form. Exists as a Lead. Receives the acknowledgement mail and the owner's reply. Never logs in. | No |
| ACT-03 | Owner | The company owner. Full access: content, catalogue, leads, quotes, tenants, money, settings, users. | Yes |
| ACT-04 | Sales user | Works the lead inbox and the pipeline, creates quotes, records tenants and subscriptions. Cannot change site content, settings or users. | Yes |
| ACT-05 | Editor | Maintains pages, products, plans, projects, case studies, API entries and media. Cannot see leads, money or customer data. | Yes |
| ACT-06 | Auditor / accountant | Read-only across quotes, invoices, payments, subscriptions and the audit log. Can export. Can change nothing. | Yes |
| ACT-07 | System (scheduler) | Background jobs: outbox sending, scheduled publish/unpublish, SLA breach detection, renewal reminders, demo-link health checks, sitemap regeneration, retention sweeps. | Service identity |
| ACT-08 | Search engine crawler | Fetches server-rendered public pages, `robots.txt`, `sitemap.xml` and JSON-LD. Must never reach a draft or an admin route. | No |
| ACT-09 | Integration consumer | An external system (or the owner's own product instance) receiving signed outbound webhooks such as `lead.created` and `subscription.changed`. | Signature |
| ACT-10 | Captcha provider (Cloudflare Turnstile) | Issues a token to the browser and answers the server's siteverify call. An outage of this actor must not close the enquiry channel. | External |
| ACT-11 | Mail transfer agent (SMTP relay) | Accepts outbound transactional mail. Can be slow, can bounce, can silently spam-file. Its failures are first-class domain events. | External |

## 2. Entities

| ID | Entity | One-line meaning |
|---|---|---|
| E-01 | Page | A public page: home, about, services index, contact, privacy, terms. |
| E-02 | PageSection | An ordered block on a page (hero, feature grid, CTA, rich text). |
| E-03 | NavigationItem | One entry in the header or footer menu, ordered, nestable one level. |
| E-04 | Service | A service the company sells (custom development, ERP implementation, support). |
| E-05 | Technology | A technology in the stack (.NET, C#, Angular, SQL Server, Python) with a category. |
| E-06 | ServiceTechnology | Which technologies a service uses. |
| E-07 | TeamMember | A person on the about page, with an optional public flag. |
| E-08 | Testimonial | A quoted customer sentence with attribution and a permission flag. |
| E-09 | MediaAsset | An uploaded file: screenshot, logo, hero image, brochure PDF. |
| E-10 | SeoMetadata | Title, description, canonical URL, OG image and index/no-index for one content item. |
| E-11 | Redirect | A permanent redirect from an old slug to a new one. |
| E-12 | ContentVersion | An immutable snapshot of a content item, written on every publish, used for rollback. |
| E-13 | Product | A software product the company sells (ERP, HMS, billing, school, college). |
| E-14 | ProductCategory | An industry or vertical grouping for products. |
| E-15 | ProductFeature | One named capability listed on a product page, ordered, optionally grouped. |
| E-16 | ProductScreenshot | A MediaAsset attached to a product with a caption and sort order. |
| E-17 | PricingPlan | A named plan for a product: price, billing period, currency, seat allowance. |
| E-18 | PlanFeature | The value of a feature within a plan (included, limited to N, not included). |
| E-19 | DemoEnvironment | The public demo URL for a product, optional shared credentials, health state. |
| E-20 | FaqItem | A question and answer attached to a product or to the site. |
| E-21 | ApiCatalogEntry | A public API the company exposes: name, purpose, auth scheme, docs URL. |
| E-22 | ApiVersion | A version of an API entry with status (beta, stable, deprecated) and a sunset date. |
| E-23 | Project | A delivered piece of work in the portfolio. |
| E-24 | CaseStudy | The narrative for a project: problem, approach, outcome metrics. |
| E-25 | ClientLogo | A client's mark, shown only when `HasPermission` is true. |
| E-26 | FormDefinition | A public form: contact, request demo, request quote, request tenant, partnership. |
| E-27 | FormField | A field on a form definition with type, label, required flag and order. |
| E-28 | FormSubmission | The raw, immutable payload of one public submission, with IP, user agent and captcha result. |
| E-29 | ConsentRecord | The consent text, version, purpose, timestamp and IP captured with a submission. |
| E-30 | Lead | A qualified-or-not enquiry: person, company, interest, stage, owner, SLA clock. |
| E-31 | LeadActivity | A note, call, email or stage change recorded against a lead. Append-only. |
| E-32 | Organisation | A customer company: legal name, GSTIN, address, status. |
| E-33 | Contact | A person at an organisation. |
| E-34 | Quote | A priced proposal to an organisation, with validity and a lifecycle. |
| E-35 | QuoteLineItem | A line on a quote: product, plan, quantity, unit price, discount, tax rate. |
| E-36 | Tenant | A sold instance of a product for an organisation, with its environment URL. |
| E-37 | Subscription | The commercial agreement behind a tenant: plan, term, seats, renewal date, state. |
| E-38 | SubscriptionEvent | An append-only record of every subscription state transition and its reason. |
| E-39 | Invoice | A demand for payment against a subscription or quote, with a gapless number. |
| E-40 | Payment | Money actually received, recorded manually or by an external reference. |
| E-41 | AdminUser | A person who can sign in to the back office. |
| E-42 | Role | Owner, Sales, Editor, Auditor. |
| E-43 | Permission | A `resource:action` string the code checks. |
| E-44 | RolePermission | Which permissions a role holds. |
| E-45 | RefreshToken | A rotating refresh token with expiry, replacement chain and revocation. |
| E-46 | LoginAttempt | Every sign-in attempt, for lockout and for the audit trail. |
| E-47 | EmailTemplate | A named, versioned subject and body with typed placeholders. |
| E-48 | OutboxEmail | A queued message with attempt count, next attempt time and terminal state. |
| E-49 | EmailDeliveryLog | What the SMTP server actually said, per attempt. |
| E-50 | Unsubscribe | A suppression entry for an email address with a reason and timestamp. |
| E-51 | WebhookEndpoint | An outbound webhook target with a secret and an event subscription list. |
| E-52 | WebhookDelivery | One delivery attempt with request, response code and retry state. |
| E-53 | AuditLog | Append-only record of every mutating admin action, with before and after values. |
| E-54 | SystemSetting | A typed key-value setting: company profile, timezone, SLA hours, SMTP, captcha keys. |
| E-55 | Holiday | A non-working date, used by the SLA clock. |
| E-56 | ImportJob | A CSV/XLSX import with dry-run results, per-row errors and a committed flag. |
| E-57 | ExportJob | A generated export file with its filter, row count and expiry. |
| E-58 | PageViewStat | A daily first-party counter per public route. |
| E-59 | ScheduledJobRun | Every background job execution with outcome and duration. |
| E-60 | Announcement | A time-boxed site banner. |

## 3. Lifecycle state machines

**Content item** (Page, Product, Project, CaseStudy, ApiCatalogEntry share this):
`Draft -> Published` (publish) · `Published -> Modified` (edit a published item) · `Modified -> Published` (publish the draft) · `Published|Modified -> Unpublished` (unpublish) · `Unpublished -> Published` (re-publish) · any -> `Archived` (terminal for listing, still addressable by redirect).
Scheduled transitions are the same transitions performed by ACT-07 at a stored UTC instant.

**Lead**: `New -> Contacted -> Qualified -> Converted` (terminal, produces Organisation + Contact + optionally Quote) · `New|Contacted|Qualified -> Disqualified` (terminal, requires a reason) · `New -> Spam` (terminal, no notification, excluded from every funnel metric) · `Qualified -> Nurturing -> Contacted` (re-engagement loop).

**Quote**: `Draft -> Sent -> Accepted` (terminal, may create a Subscription) · `Sent -> Rejected` (terminal, reason required) · `Sent -> Expired` (terminal, automatic at `ValidUntil`) · `Draft|Sent -> Withdrawn` (terminal).

**Subscription**: `Trial -> Active` · `Trial -> Expired` (terminal) · `Active -> PastDue` (invoice unpaid past grace) · `PastDue -> Active` (payment recorded) · `PastDue -> Suspended` (grace exhausted) · `Suspended -> Active` (payment recorded) · `Active|PastDue|Suspended -> Cancelled` (terminal) · `Active -> Expired` (term ended, not renewed; terminal).

**OutboxEmail**: `Pending -> Sending -> Sent` · `Sending -> Pending` (retryable failure, attempt + 1) · `Pending -> DeadLettered` (attempt limit reached; terminal, alerts the owner) · `Pending -> Cancelled` (recipient unsubscribed or suppressed).

**Invoice**: `Draft -> Issued -> PartiallyPaid -> Paid` (terminal) · `Issued -> Overdue -> Paid` · `Issued|Overdue -> Cancelled` (terminal, requires a credit reason).

**WebhookDelivery**: `Pending -> Delivered` (2xx) · `Pending -> Failed -> Pending` (retry) · `Failed -> Abandoned` (attempt limit; terminal).

## 4. Business rules

Deterministic, with concrete boundary values. `BR-<MOD>-<NN>`.

| ID | Rule |
|---|---|
| BR-SITE-01 | A content item has exactly one of `Draft`, `Published`, `Modified`, `Unpublished`, `Archived`. Editing a `Published` item sets it to `Modified` and leaves the public version untouched until the draft is published (S-07). |
| BR-SITE-02 | A slug is unique per content type, lowercase, 3 to 120 characters, matching `^[a-z0-9]+(-[a-z0-9]+)*$`. A 121st character is rejected with 422. |
| BR-SITE-03 | Publishing is refused if the item references an unpublished or archived item (a plan, screenshot, product or media asset). The response lists every offending reference (S-07). |
| BR-SITE-04 | Scheduled publish and unpublish times are entered in the admin's display timezone, stored in UTC, and executed by ACT-07 within 60 seconds of the stored instant. The dialog shows the server-time equivalent (S-06). |
| BR-SITE-05 | `PublishAtUtc` must be at least 5 minutes in the future and at most 365 days ahead; `UnpublishAtUtc`, when present, must be strictly after `PublishAtUtc`. |
| BR-SITE-06 | Changing the slug of a published item creates a 301 Redirect row from the old path automatically. Redirect chains deeper than 3 hops are collapsed to a single hop. |
| BR-SITE-07 | Every publish writes a ContentVersion. The most recent 20 versions per item are retained and any of them can be restored; restoring creates a new version rather than deleting history. |
| BR-SITE-08 | A page cannot be deleted while a NavigationItem or a published page links to it; the API returns 409 `PAGE_IN_USE` naming the referrers. |
| BR-SITE-09 | SEO title is 1 to 60 characters and meta description 50 to 160 characters; outside that range the admin shows a warning but publishing is still allowed, because search engines truncate rather than fail. |
| BR-CAT-01 | A product may be published only if it has at least 1 published category, 3 features, 1 screenshot and 1 pricing plan. Below any of those thresholds publishing returns 422 with the failing counts. |
| BR-CAT-02 | Plan prices are stored in minor units (paise) as `decimal(18,2)` with an ISO-4217 currency code. A price below 0 is rejected; a price of exactly 0 is allowed only when `IsFreeTier` is true. |
| BR-CAT-03 | Exactly one plan per product may carry `IsRecommended = true`. Setting it on a second plan clears the first inside the same transaction. |
| BR-CAT-04 | A plan's billing period is one of `Monthly`, `Quarterly`, `Yearly`, `OneTime`. Annual plans must be priced at or below 12 x the monthly price of the same tier; a higher figure is rejected as a data-entry error. |
| BR-CAT-05 | Displaying "from Rs. X" on a product card uses the lowest published non-free plan price for that product. |
| BR-CAT-06 | Demo credentials are optional; when present they are shown only on the published product page and never written to a log or an email. |
| BR-CAT-07 | A product slug that has ever been published is never reused for a different product, even after archiving. |
| BR-CAT-08 | Deleting a product is refused when a Quote line item, Tenant or Subscription references it; the product may only be archived (409 `PRODUCT_IN_USE`). |
| BR-API-01 | An ApiCatalogEntry must carry at least one ApiVersion before it can be published. |
| BR-API-02 | Exactly one version of an API entry may be marked `IsCurrent`. Marking a `Deprecated` version as current is rejected with 422. |
| BR-API-03 | A version marked `Deprecated` requires a `SunsetDateUtc` at least 90 days in the future at the time it is deprecated. |
| BR-PRJ-01 | A client logo or client name appears publicly only when `HasPermission = true`; otherwise the case study renders as "a leading <industry> company". |
| BR-PRJ-02 | A case study requires a problem, an approach and at least one outcome metric with a number and a unit before it can be published. |
| BR-PRJ-03 | A testimonial requires the author's name and role and an explicit permission flag; anonymous testimonials are not published. |
| BR-LEAD-01 | A submission is accepted with only three values: name, a valid email or a 10-digit Indian mobile number, and the message. Everything else is optional (S-09). |
| BR-LEAD-02 | Every public submission carries a Turnstile token. The server verifies it once against siteverify with an idempotency key; a token older than 300 seconds or already used is rejected with 400 `CAPTCHA_INVALID` (S-04). |
| BR-LEAD-03 | A submission whose honeypot field is non-empty is stored with `IsSpam = true`, answered with the normal 200 response, and never notified or counted in the funnel. |
| BR-LEAD-04 | Public submissions are rate limited to 5 per IP per 10 minutes and 20 per IP per 24 hours; the 6th within the window returns 429 with `Retry-After`. |
| BR-LEAD-05 | A submission from the same email with the same form and the same message hash within 10 minutes is treated as a double-click: the original Lead is returned and no second notification is sent. |
| BR-LEAD-06 | Consent is an unticked checkbox; the exact consent text, its version, purpose, timestamp and IP are stored with the submission. Without consent the submission is rejected with 422 `CONSENT_REQUIRED`. |
| BR-LEAD-07 | Every accepted submission creates exactly one Lead in stage `New`, owned by the default owner from settings. |
| BR-LEAD-08 | Merging two leads keeps the earliest `CreatedAtUtc` record as the survivor, appends the other's activities, and marks the merged record `Merged` with a pointer to the survivor (S-08). |
| BR-LEAD-09 | The stage may only move `New -> Contacted -> Qualified -> Converted`; skipping Contacted is allowed for a qualified inbound only when a reason is recorded. Any move to `Disqualified` requires a reason of at least 10 characters. |
| BR-LEAD-10 | First response time is measured from `CreatedAtUtc` to the first outbound LeadActivity, counted only inside business hours (09:00-18:00 IST, Monday to Saturday, excluding Holiday rows). Target 1 business day (A-28). |
| BR-LEAD-11 | A lead with no activity for 14 calendar days and stage `Contacted` is flagged `Stale` on the dashboard; at 30 days the system reminds the owner. |
| BR-LEAD-12 | A lead marked `Spam` is excluded from every funnel, SLA and conversion metric, permanently. |
| BR-LEAD-13 | Converting a lead requires an Organisation (new or existing) and a Contact; the lead becomes `Converted` and is never edited again except by appending activities. |
| BR-CUST-01 | An organisation's GSTIN, when present, is 15 characters matching `^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$` and is unique across active organisations. |
| BR-CUST-02 | Contact email is unique per organisation; the same email may exist at two organisations (people change jobs). |
| BR-CUST-03 | An organisation cannot be deleted once a quote, tenant or invoice references it; it can only be set `Inactive`. |
| BR-CUST-04 | Deduplication compares normalised email domain plus normalised company name; a match above the threshold is surfaced to the user, never merged automatically. |
| BR-SALE-01 | A quote number is gapless per financial year in the format `Q/<FY>/<00001>`, allocated inside the transaction that first persists the quote, never reused, never renumbered. |
| BR-SALE-02 | A quote's `ValidUntilUtc` defaults to 15 days after issue and may not exceed 90 days. At expiry ACT-07 moves it to `Expired`. |
| BR-SALE-03 | Line totals are `round(Quantity x UnitPrice, 2) - Discount`, tax is `round(LineTotal x TaxRate, 2)` per line, and the quote total is the sum of rounded line values. Rounding happens per line, never once at the end. |
| BR-SALE-04 | A discount above 15% of the line subtotal requires the Owner role; Sales attempting it receives 403 `APPROVAL_REQUIRED`. |
| BR-SALE-05 | A quote can only be edited while `Draft`. After `Sent` a change requires a revision, which creates a new quote linked to the original. |
| BR-SALE-06 | Accepting a quote is idempotent: a second acceptance returns the existing subscription rather than creating a duplicate. |
| BR-SALE-07 | A trial subscription lasts exactly 14 days from activation unless overridden by the Owner, and converts to `Expired` at the boundary if no plan is confirmed (S-13). |
| BR-SALE-08 | An invoice is `Overdue` the day after `DueDateUtc`. After 7 further days the subscription moves to `PastDue`, and after 21 days to `Suspended`. |
| BR-SALE-09 | A payment may not exceed the invoice's outstanding amount; an over-payment is rejected with 422 `OVERPAYMENT` naming the outstanding figure. |
| BR-SALE-10 | Invoice numbers are gapless per financial year in the format `INV/<FY>/<00001>`, allocated under the same rule as BR-SALE-01. |
| BR-SALE-11 | A subscription's renewal date is anchored to the activation date in UTC; a monthly renewal on the 31st falls to the last day of shorter months. |
| BR-SALE-12 | Cancelling a subscription requires a reason and records the effective date; the tenant keeps working until the paid period ends unless `ImmediateTermination` is chosen by the Owner. |
| BR-IAM-01 | Passwords are at least 12 characters with three of four character classes, hashed by ASP.NET Core Identity's default hasher. |
| BR-IAM-02 | Five failed sign-in attempts within 15 minutes lock the account for 15 minutes; the response is identical whether the account exists or not. |
| BR-IAM-03 | Access tokens live 15 minutes; refresh tokens live 14 days, rotate on every use, and a reused refresh token revokes the whole chain and alerts the Owner. |
| BR-IAM-04 | The last remaining user in the Owner role cannot be deleted, disabled or demoted (409 `LAST_OWNER`). |
| BR-IAM-05 | Every non-public endpoint is deny-by-default: an endpoint with no explicit policy is unreachable, and the anonymous allowlist is exactly health, login and refresh. |
| BR-NOTIF-01 | Every outbound email is written to the outbox inside the same transaction as the business change; nothing is sent inline during the request. |
| BR-NOTIF-02 | The outbox retries on failure at 1, 5, 15, 60 and 240 minutes. After the 5th failure the message is `DeadLettered` and the owner is alerted. |
| BR-NOTIF-03 | The envelope sender is always an address at the company's own domain; the recipient's address never becomes the sender (S-16). |
| BR-NOTIF-04 | An address on the Unsubscribe list receives no non-transactional mail; acknowledgements and quotes remain transactional and are still sent. |
| BR-NOTIF-05 | Email bodies never contain a password, a token, an API key or demo credentials. |
| BR-RPT-01 | Every report states the timezone and the exact window it covers, and excludes `Spam` and `Merged` leads. |
| BR-RPT-02 | Conversion rate is `Converted / (Total - Spam - Merged)` for leads created inside the window, computed on the lead's creation date, not the conversion date. |
| BR-ADM-01 | Every mutating admin action writes an AuditLog row with actor, action, entity, before and after values; audit rows are never updated or deleted. |
| BR-ADM-02 | An import runs as a dry run first and reports per-row errors; committing an import with any invalid row is refused unless the user explicitly chooses "skip invalid rows", which is recorded on the job. |
| BR-ADM-03 | An import file is at most 5 MB and 5,000 rows; larger files are rejected with 413 before parsing. |
| BR-ADM-04 | Retention: leads and submissions are anonymised 3 years after last activity, email delivery logs are deleted after 12 months, audit rows are kept 7 years (A-26). |
| BR-INT-01 | Every outbound webhook carries an HMAC-SHA256 signature over the raw body with a per-endpoint secret and a timestamp header; receivers older than 5 minutes must reject it. |
| BR-INT-02 | Webhook delivery retries 5 times with exponential backoff (1, 5, 25, 125, 625 seconds) and is then `Abandoned` with an owner alert. |
| BR-INT-03 | `sitemap.xml` contains only `Published` public URLs, is regenerated within 5 minutes of any publish, and is split into a sitemap index if it would exceed 50,000 URLs or 50 MB (S-02). |
| BR-INT-04 | A demo link is checked hourly; two consecutive non-2xx results mark the demo `Down`, hide the demo button on the public page and alert the owner. |

## 5. Money and billing flows

Every event that moves or commits money:

1. **Quote issued** - line items priced from the plan at that moment; the price is copied onto the line, never referenced live, so a later plan change does not alter a sent quote.
2. **Quote accepted** - creates a Subscription (Trial or Active) and the first Invoice.
3. **Trial started** - no money; a 14-day clock (BR-SALE-07).
4. **Trial converted** - first invoice issued on conversion date.
5. **Invoice issued** - gapless number allocated (BR-SALE-10), tax computed per line (BR-SALE-03).
6. **Payment recorded** - manual entry with mode (UPI, NEFT, cheque, card link), reference number and date; partial payments allowed (BR-SALE-09).
7. **Partial payment** - invoice moves to `PartiallyPaid`, outstanding recomputed.
8. **Invoice overdue** - dunning email at day 1, 7 and 14; subscription state changes at day 7 and 21 (BR-SALE-08).
9. **Discount applied** - stored on the line with the approving user when above 15% (BR-SALE-04).
10. **Waiver / write-off** - Owner-only credit note against an invoice with a mandatory reason; recorded, never deleted.
11. **Refund** - a negative Payment row referencing the original, Owner-only, with a reason. Money leaves outside the system; the record does not.
12. **Renewal** - a renewal invoice is generated 15 days before the renewal date and the customer is reminded at 15, 7 and 1 days.
13. **Upgrade / downgrade mid-term** - proration in whole days: `newPrice x remainingDays / termDays` minus the unused portion of the old plan, rounded to 2 decimals.
14. **Seat change** - a seat added mid-term is prorated the same way; seats removed take effect at renewal, never mid-term.
15. **Cancellation** - service continues to the end of the paid period unless the Owner terminates immediately (BR-SALE-12).
16. **Subscription expiry** - no invoice; the tenant is marked `Ended` and the owner is prompted to archive it.
17. **Day close** - a daily job snapshots invoiced, collected and outstanding totals so a later edit can never silently rewrite yesterday's number.
18. **Financial-year rollover** - quote and invoice sequences restart at 00001 for the new FY prefix on 1 April.

## 6. Notifications matrix

| Event | To | Channel | Retry | Opt-out |
|---|---|---|---|---|
| Enquiry acknowledgement | Enquirer | Email | Outbox, 5 attempts | Transactional, none |
| New lead alert | Owner / assigned sales | Email | Outbox, 5 attempts | Per-user setting |
| SLA breach warning (75% of target) | Owner | Email + dashboard | 1 attempt, dashboard is authoritative | Per-user |
| Quote sent | Contact | Email with PDF | Outbox, 5 attempts | Transactional |
| Quote expiring in 3 days | Owner | Email | Outbox | Per-user |
| Invoice issued / overdue | Contact | Email | Outbox | Transactional |
| Renewal due (15, 7, 1 days) | Contact + Owner | Email | Outbox | Contact may opt out of the 15-day notice only |
| Subscription suspended | Contact + Owner | Email | Outbox | Transactional |
| Demo link down | Owner | Email | 1 per incident, not per check | No |
| Outbox dead-letter | Owner | Email via a second, independent path; if that fails, dashboard alert only | No further retry | No |
| Daily digest (new leads, breaches, renewals) | Owner | Email at 08:00 IST | Outbox | Per-user |
| Import finished | Initiating user | In-app + email | Outbox | Per-user |

## 7. Reporting

- **Operational (daily)**: new leads by form and source, unanswered leads, SLA breaches, demo-link health, outbox failures.
- **Managerial (weekly/monthly)**: funnel by stage with conversion rates, quotes sent/accepted/rejected with win rate, revenue booked and collected, MRR from active subscriptions, renewals due in 30 days, top products by enquiry volume, traffic-to-enquiry conversion per page.
- **Statutory / financial**: invoice register per financial year with GST per line, payment register, outstanding ageing (0-30, 31-60, 61-90, 90+). These exist to be handed to an accountant, so every one exports to CSV and XLSX with the same numbers the screen shows.

## 8. Back-office and master data

The admin panel is the whole back office. Master data maintained there: products, categories, features, plans, plan features, services, technologies, team members, testimonials, FAQ items, API entries and versions, projects, case studies, client logos, form definitions and fields, email templates, holidays, roles and permissions, system settings, navigation menus, redirects. Every one of these is editable without a deployment; a system with no back office is not a real system.

## 9. Audit trail

`AuditLog` is append-only: `Id, OccurredAtUtc, ActorUserId, ActorEmail, ActorIp, Action, EntityType, EntityId, BeforeJson, AfterJson, CorrelationId`. Written by a `SaveChangesAsync` override so no code path can forget it. Sensitive values (password hashes, tokens, captcha secrets, demo credentials) are redacted to `***` before serialisation. There is no update or delete path in the API or the UI.

## 10. Compliance and data protection

India's DPDP Act 2023 posture. Personal data held: name, email, phone, company, message text, IP address, user agent, consent record. Purpose is stated on the form and stored with the consent. Rights supported operationally: access (export a person's data by email), correction (edit the lead), erasure (anonymise, keeping the financial record required for tax). Cookie banner: no non-essential tag fires before consent. Data lives in an India region (A-17). A breach-notification runbook lives in `docs/` and names the owner as the responsible person.

## 11. Multi-tenancy

**Answered explicitly: single-tenant.** One company owns all rows; there is no `TenantId`. This is A-02 and it is the single most expensive assumption to reverse: adding tenancy later means a `TenantId` column on every table, an EF Core global query filter, a tenant resolver in the pipeline, and a re-test of every authorization rule. Estimated cost recorded at 3 to 4 phases. The word "tenant" in this system means *a customer's instance of a product the owner sells*, never a tenant of this website.

## 12. Offline, edge and physical-world cases

The public site is read-mostly and is fronted by a CDN, so a database outage still serves cached pages. Form submission requires the database; if it is unreachable the visitor sees an honest error with the owner's email address and phone number rather than a silent failure. The owner works from a laptop on Indian broadband with real outages: every admin action must be safe to retry, and no workflow may depend on a long-running browser session. Demo environments live on other machines entirely and go down independently (BR-INT-04). Printed artifacts exist: a quote PDF is emailed and often printed, so its layout must survive A4.

## 13. Failure and exception flows

| ID | Flow | Handling |
|---|---|---|
| EX-101 | Two editors save the same page concurrently | `RowVersion` check; second save returns 409 `CONCURRENCY_CONFLICT` with a diff, never a silent overwrite |
| EX-102 | Publish scheduled while the server is down | On start, ACT-07 processes every due transition in order and logs the lateness |
| EX-103 | Scheduled publish time is in the past at save | Rejected 422 (BR-SITE-05) |
| EX-104 | Publishing an item that references an unpublished plan | 422 listing the offending references (BR-SITE-03) |
| EX-105 | Slug collides with an existing published slug | 409 `SLUG_TAKEN`, suggests `slug-2` |
| EX-106 | Old URL still linked from Google after a slug change | Automatic 301 (BR-SITE-06) |
| EX-107 | Redirect loop created by two renames | Chain collapsed; a self-referencing redirect is rejected |
| EX-108 | Media upload exceeds 10 MB | 413 before the file is written to disk |
| EX-109 | Upload with a spoofed content type (`.exe` renamed `.png`) | Magic-number sniffing; rejected 415, filename never trusted |
| EX-110 | Image is a 6000px camera JPEG | Resized on upload to a 1920px web version plus a thumbnail; the original is kept |
| EX-111 | Disk full during upload | Transaction rolled back, no orphan row, owner alerted |
| EX-112 | Deleting a media asset still used on a published page | 409 naming the pages |
| EX-113 | Rollback to a version whose linked entities no longer exist | Restore is refused with an explanation rather than resurrecting broken links |
| EX-114 | Navigation item points at an unpublished page | The item is hidden from the public menu and flagged in the admin |
| EX-115 | Announcement whose end date is before its start date | 422 |
| EX-121 | Product published with 2 features (threshold is 3) | 422 with the counts (BR-CAT-01) |
| EX-122 | Plan price entered as `1,20,000` with separators | Parsed against the invariant culture; a value that does not parse is rejected, never silently zeroed |
| EX-123 | Two plans both marked recommended | Second write clears the first (BR-CAT-03) |
| EX-124 | Yearly price higher than 12x monthly | 422 as a probable data-entry error (BR-CAT-04) |
| EX-125 | Demo URL returns 502 for an hour | Two consecutive failures mark it `Down` and hide the button (BR-INT-04) |
| EX-126 | Demo credentials pasted into a public feature description | Publish-time scan for `password:` patterns warns the editor |
| EX-127 | Product archived while it appears in a sent quote | Archive allowed, delete refused (BR-CAT-08) |
| EX-141 | API entry published with no version | 422 (BR-API-01) |
| EX-142 | Deprecating the only current version | 422; a replacement must be marked current first (BR-API-02) |
| EX-143 | Sunset date less than 90 days away | 422 (BR-API-03) |
| EX-161 | Case study published without an outcome metric | 422 (BR-PRJ-02) |
| EX-162 | Client logo used without permission | Publish blocked; the anonymised label is offered (BR-PRJ-01) |
| EX-181 | Submission with an empty message but a valid email | Accepted: only three fields are required (BR-LEAD-01) |
| EX-182 | Submission with a 200 KB message body | Truncation is not silent: 422 above 5,000 characters |
| EX-183 | Turnstile is unreachable (provider outage) | Fail-open after a 3-second timeout, mark the submission `CaptchaUnverified`, apply the strict rate limit, notify the owner. The enquiry channel never closes because a third party is down. |
| EX-184 | Replayed captcha token | 400 `CAPTCHA_INVALID` (BR-LEAD-02) |
| EX-185 | Bot floods 500 submissions in a minute | Rate limit returns 429 from the 6th; entries are stored as spam, not notified (BR-LEAD-04) |
| EX-186 | Visitor double-clicks submit | Idempotent by message hash within 10 minutes (BR-LEAD-05) |
| EX-187 | Consent checkbox not ticked | 422 `CONSENT_REQUIRED` (BR-LEAD-06) |
| EX-188 | Notification email to the owner bounces | Outbox retries, then dead-letters and shows a dashboard alert; the lead is still saved and visible (S-16, S-17) |
| EX-189 | SMTP relay accepts the mail but the recipient's provider files it as spam | Delivery log records acceptance; the dashboard "unread leads" count is the authoritative channel, not the inbox |
| EX-190 | Same person submits from two email addresses | Duplicate surfaced by BR-CUST-04, merged manually by BR-LEAD-08 |
| EX-191 | Lead moved to Converted without an organisation | 422 (BR-LEAD-13) |
| EX-192 | Disqualified without a reason | 422 (BR-LEAD-09) |
| EX-193 | SLA clock crosses a Sunday and a public holiday | Only business hours count (BR-LEAD-10) |
| EX-194 | Enquiry arrives at 23:55 IST | The clock starts at 09:00 the next business day |
| EX-195 | Spam lead accidentally marked spam | Owner can restore it; the restore is audited and the SLA clock resumes from the original creation time |
| EX-201 | Organisation created with an invalid GSTIN | 422 with the expected pattern (BR-CUST-01) |
| EX-202 | Two organisations with the same GSTIN | 409 `GSTIN_DUPLICATE` |
| EX-203 | Contact email reused across organisations | Allowed by BR-CUST-02 |
| EX-221 | Quote numbering under two concurrent creates | Sequence allocated inside the transaction; no gaps, no duplicates (BR-SALE-01) |
| EX-222 | Quote accepted twice (double click, or a re-sent link) | Idempotent (BR-SALE-06) |
| EX-223 | Quote edited after being sent | 409; a revision is offered (BR-SALE-05) |
| EX-224 | Sales user applies a 30% discount | 403 `APPROVAL_REQUIRED` (BR-SALE-04) |
| EX-225 | Payment recorded larger than the outstanding amount | 422 `OVERPAYMENT` (BR-SALE-09) |
| EX-226 | Payment recorded twice with the same reference | 409 on the unique (invoice, reference) pair |
| EX-227 | Renewal falls on 31 February | Falls back to the last day of the month (BR-SALE-11) |
| EX-228 | Subscription cancelled mid-term | Runs to the end of the paid period unless the Owner terminates (BR-SALE-12) |
| EX-229 | Financial year rolls over during an open quote | The quote keeps its original FY number; new quotes start the new sequence |
| EX-241 | Six failed logins | Lockout for 15 minutes with a response identical to an unknown user (BR-IAM-02) |
| EX-242 | Refresh token replayed after rotation | Whole chain revoked, owner alerted (BR-IAM-03) |
| EX-243 | Attempt to delete the last Owner | 409 `LAST_OWNER` (BR-IAM-04) |
| EX-244 | Editor calls a leads endpoint directly with a valid token | 403, proven by the D7 gate on every route (BR-IAM-05) |
| EX-245 | Password reset link reused | Single-use token, 60-minute expiry, second use returns 410 |
| EX-261 | Notification sent from the hosting server's own domain | Prevented: envelope sender is always the company domain (BR-NOTIF-03, S-16) |
| EX-262 | SPF or DKIM not configured at go-live | Startup check warns loudly in the admin dashboard until the DNS records verify |
| EX-263 | Owner cannot tell whether a mail was sent | Every attempt is in the delivery log with the SMTP response (S-17) |
| EX-264 | Template placeholder has no value | Render fails at compose time and the message is never queued half-formed |
| EX-265 | Recipient unsubscribes then submits a new enquiry | Transactional acknowledgement is still sent (BR-NOTIF-04) |
| EX-281 | Report requested for a range wider than 24 months | Rejected with a suggestion to export instead |
| EX-282 | Export of 15,000 leads | Streamed to a file and emailed as a link; never assembled in memory |
| EX-301 | Import file with a wrong header row | Dry run reports it and commits nothing (BR-ADM-02) |
| EX-302 | Import of 6,000 rows | 413 above 5,000 rows (BR-ADM-03) |
| EX-303 | Import partially applied then the process dies | Import runs in one transaction per batch with a resume token; a half-import is never left behind |
| EX-321 | Webhook endpoint returns 500 for an hour | Exponential backoff, then abandoned with an alert (BR-INT-02) |
| EX-322 | Webhook secret rotated while deliveries are queued | Queued deliveries are signed with the secret current at send time |
| EX-323 | Sitemap exceeds 50,000 URLs | Automatically split into a sitemap index (BR-INT-03, S-02) |
| EX-324 | Analytics tag loaded before cookie consent | Prevented; the tag is injected only after consent (A-27) |

## 14. Integrations and devices

Cloudflare Turnstile (captcha), an SMTP relay (transactional mail), an optional analytics tag, an optional meeting-scheduling link (Calendly-style, embedded not integrated), outbound webhooks to the owner's own product instances, and the demo environments themselves as monitored external URLs. No card terminal, no scanner, no printer driver: the only physical artifact is a printed quote PDF.

## 15. Time, calendar and cross-midnight rules

All instants are UTC `datetime2(3)`. Display timezone is a setting, default `Asia/Kolkata`. Business hours 09:00-18:00 IST Monday to Saturday, minus Holiday rows, drive every SLA calculation. Date-only values (quote validity, renewal dates, holidays) are stored as `date` and are never shifted by a timezone conversion. A renewal on the 31st falls back to the month's last day. Financial year runs 1 April to 31 March for numbering. Two submissions in the same second are distinguished by the sequence, not the timestamp.

## 16. Search, discovery and bulk data

Public: product search by name, feature text and category, plus filters by industry and price band, using SQL Server full-text on a few hundred rows (A-10). Admin: leads by name, email, phone, company, stage, source, date range and owner; quotes by number, organisation and state; content by title and slug. Every list is paged with a hard maximum page size of 100 and a documented default of 20. Bulk: CSV/XLSX import for products, projects, leads and organisations; CSV and XLSX export from every admin list with the current filter applied.

## 17. Data migration and day-one onboarding

The owner starts with an Excel sheet of past enquiries, a folder of screenshots, and text scattered across an old site. Day one: import organisations, then leads, then products; upload media in bulk; create pages from a seeded set of templates so the site is never empty. Every importer runs as a dry run first, reports per-row errors with row numbers, and commits only on an explicit second action (BR-ADM-02).

## 18. Human process around the software

The owner checks the dashboard first thing in the morning; the daily digest at 08:00 IST exists so the phone is enough. A new enquiry is answered inside one business day (A-28) - the promise is printed on the contact page, so the software must make it visible when it is at risk. Quotes are discussed on WhatsApp and by phone; the system records the outcome, it does not replace the conversation. Money arrives by UPI or bank transfer and is reconciled by hand against the invoice register. When the owner hires, the Editor role must be safe to hand to someone who has never seen the system: that is why publishing is guarded by validation rather than by trust, and why every change is versioned and audited.
