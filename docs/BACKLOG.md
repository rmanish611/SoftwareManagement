# Backlog

The only place in this repository where a `TODO` may live. Everything here is deliberately
outside the approved 138-requirement contract. Moving an item into the build needs an
amendment in `docs/blueprint/04a-amendments.md` with an ADR written first.

## Deferred by an explicit scope boundary (01-research.md)

| Item | Why it is out of v1 | What it would become |
|---|---|---|
| Online checkout and card payment | A-04: money moves outside the site in v1; Invoice and Payment already model the record | A payment-link endpoint plus a provider webhook |
| Licence keys, activation limits, secure downloads | A-03: the products are hosted SaaS tenants, not downloadable software | A fulfilment module |
| Custom fields defined at runtime | A fixed, migrated schema is what makes the gates and tests meaningful | A field-definition module |
| Visual drag-and-drop page builder | Typed content plus a good admin is testable; a builder is a product in itself | A layout engine |
| Marketing email campaigns and newsletters | Transactional mail only; bulk sending brings consent and deliverability duties | A campaign module with its own suppression rules |
| Customer self-service portal with login | Nothing in the brief requires a customer to sign in | A second identity surface and its own authorization matrix |
| Multi-language content (Hindi first) | English only in v1, but every string already goes through i18n plumbing (NFR-I18N-01) | Translation resource files, no code change |
| Round-robin lead assignment and territories | One owner today (A-02) | An assignment-rule module |
| Full-text search engine (Elasticsearch) | SQL Server full-text is enough at the volumes in A-10 and A-11 | A search service behind the existing query interface |
| Hosted APM (Application Insights, Seq, Datadog) | Cost, and PII scrubbing must be proven first | A sink behind the existing logging interface (ADR-19) |
| Hangfire as the job server | Our own outbox is needed regardless, for the delivery log REQ-NOTIF-005 demands | A storage swap; ADR-13 already records the licence terms |
| Provisioning or hosting demo and tenant instances | A-20, A-21: this site links and records, it does not deploy | A provisioning service calling the product instances |

## Noticed during the build

Nothing yet. An entry added here during a phase must name the phase, the file it was
noticed in, and why it was not built then.
