# 06 Authorization

**Hiding a button is not authorization.** Every rule below is enforced on the server at the endpoint, and where data is scoped, again in the query. The Angular application hides what a user may not use, but the API is what actually decides.

## Roles

| Role | Who | Principle |
|---|---|---|
| Owner | The company owner | Everything, including users, settings, money and erasure |
| Sales | A salesperson | Leads, customers, quotes, tenants, subscriptions, invoices and payments. No site content, no settings, no users |
| Editor | A content person | Site content, catalogue, projects, APIs, media. No leads, no customers, no money |
| Auditor | An accountant or reviewer | Read-only everywhere that money or compliance lives, plus the audit log and exports. Writes nothing |
| Anonymous | The public internet | Published content and form submission only |

Cell vocabulary: `Y` full · `N` denied · `Y-OWN` only rows the user owns · `Y-LIMIT(n)` allowed up to a numeric limit · `Y-APPROVAL` allowed but requires an approver · `Y-AUDIT` allowed and always audited · `Y-PUB` only items whose status is Published.

## Authorization matrix

One row per resource and action pair.

| AZ-ID | Resource : action | Permission name | Anonymous | Editor | Sales | Auditor | Owner | Enforcement point |
|---|---|---|---|---|---|---|---|---|
| AZ-01 | page : read | content.page.read | Y-PUB | Y | N | Y | Y | Endpoint policy; public endpoint filters `Status = Published` |
| AZ-02 | page : create | content.page.write | N | Y | N | N | Y | Endpoint policy |
| AZ-03 | page : update | content.page.write | N | Y | N | N | Y | Endpoint policy + row version check |
| AZ-04 | page : publish | content.page.publish | N | Y | N | N | Y | Endpoint policy + publish-time validation (BR-SITE-03) |
| AZ-05 | page : schedule | content.page.publish | N | Y | N | N | Y | Endpoint policy |
| AZ-06 | page : delete | content.page.delete | N | N | N | N | Y-AUDIT | Endpoint policy; refused when referenced (BR-SITE-08) |
| AZ-07 | contentVersion : restore | content.version.restore | N | Y | N | N | Y-AUDIT | Endpoint policy |
| AZ-08 | media : read | content.media.read | Y-PUB | Y | Y | Y | Y | Public files served by key; admin listing behind policy |
| AZ-09 | media : upload | content.media.write | N | Y | N | N | Y | Endpoint policy + magic-number validation |
| AZ-10 | media : delete | content.media.delete | N | Y | N | N | Y-AUDIT | Endpoint policy; refused when in use |
| AZ-11 | navigation : manage | content.navigation.write | N | Y | N | N | Y | Endpoint policy |
| AZ-12 | product : read | catalog.product.read | Y-PUB | Y | Y | Y | Y | Public endpoint filters `Status = Published` |
| AZ-13 | product : create | catalog.product.write | N | Y | N | N | Y | Endpoint policy |
| AZ-14 | product : update | catalog.product.write | N | Y | N | N | Y | Endpoint policy |
| AZ-15 | product : publish | catalog.product.publish | N | Y | N | N | Y | Endpoint policy + readiness thresholds (BR-CAT-01) |
| AZ-16 | product : archive | catalog.product.archive | N | Y | N | N | Y-AUDIT | Endpoint policy; delete refused when referenced (BR-CAT-08) |
| AZ-17 | pricingPlan : read | catalog.plan.read | Y-PUB | Y | Y | Y | Y | Public endpoint filters published plans |
| AZ-18 | pricingPlan : write | catalog.plan.write | N | Y | N | N | Y | Endpoint policy |
| AZ-19 | demoEnvironment : readCredentials | catalog.demo.credentials | Y-PUB | Y | Y | N | Y | Rendered only on the published product page; never logged or emailed (BR-CAT-06) |
| AZ-20 | apiCatalog : read | catalog.api.read | Y-PUB | Y | Y | Y | Y | Public endpoint filters published entries |
| AZ-21 | apiCatalog : write | catalog.api.write | N | Y | N | N | Y | Endpoint policy |
| AZ-22 | project : read | portfolio.project.read | Y-PUB | Y | Y | Y | Y | Public endpoint filters published projects |
| AZ-23 | project : write | portfolio.project.write | N | Y | N | N | Y | Endpoint policy |
| AZ-24 | caseStudy : publish | portfolio.casestudy.publish | N | Y | N | N | Y | Endpoint policy + outcome-metric validation (BR-PRJ-02) |
| AZ-25 | clientLogo : publish | portfolio.client.publish | N | Y | N | N | Y | Endpoint policy + permission flag check (BR-PRJ-01) |
| AZ-26 | formDefinition : read | lead.form.read | Y | Y | Y | Y | Y | Public endpoint returns enabled forms only |
| AZ-27 | formDefinition : write | lead.form.write | N | N | N | N | Y | Endpoint policy; consent text is Owner-only |
| AZ-28 | formSubmission : create | lead.submit | Y | Y | Y | Y | Y | Public endpoint with captcha, honeypot and rate limit (BR-LEAD-02, BR-LEAD-04) |
| AZ-29 | formSubmission : read | lead.submission.read | N | N | Y | Y | Y | Endpoint policy |
| AZ-30 | lead : read | lead.read | N | N | Y | Y | Y | Endpoint policy; Sales sees all leads in v1 (single team) |
| AZ-31 | lead : update | lead.write | N | N | Y-OWN | N | Y | Endpoint policy plus an owner check in the query for stage changes |
| AZ-32 | lead : assign | lead.assign | N | N | N | N | Y | Endpoint policy |
| AZ-33 | lead : merge | lead.merge | N | N | Y | N | Y-AUDIT | Endpoint policy; survivor chosen by rule, not by the user (BR-LEAD-08) |
| AZ-34 | lead : markSpam | lead.spam | N | N | Y | N | Y-AUDIT | Endpoint policy |
| AZ-35 | lead : restoreFromSpam | lead.spam.restore | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-36 | leadActivity : create | lead.activity.write | N | N | Y | N | Y | Endpoint policy; append-only, no update or delete route exists |
| AZ-37 | organisation : read | crm.org.read | N | N | Y | Y | Y | Endpoint policy |
| AZ-38 | organisation : write | crm.org.write | N | N | Y | N | Y | Endpoint policy |
| AZ-39 | organisation : delete | crm.org.delete | N | N | N | N | Y-AUDIT | Endpoint policy; soft delete only when unreferenced (BR-CUST-03) |
| AZ-40 | contact : write | crm.contact.write | N | N | Y | N | Y | Endpoint policy |
| AZ-41 | quote : read | sales.quote.read | N | N | Y | Y | Y | Endpoint policy |
| AZ-42 | quote : create | sales.quote.write | N | N | Y | N | Y | Endpoint policy |
| AZ-43 | quote : applyDiscount | sales.quote.discount | N | N | Y-LIMIT(15) | N | Y | Endpoint policy plus a rule check; above 15 percent returns 403 APPROVAL_REQUIRED (BR-SALE-04) |
| AZ-44 | quote : send | sales.quote.send | N | N | Y | N | Y | Endpoint policy |
| AZ-45 | quote : accept | sales.quote.accept | N | N | Y | N | Y-AUDIT | Endpoint policy; idempotent (BR-SALE-06) |
| AZ-46 | tenant : write | sales.tenant.write | N | N | Y | N | Y | Endpoint policy |
| AZ-47 | subscription : read | sales.subscription.read | N | N | Y | Y | Y | Endpoint policy |
| AZ-48 | subscription : changePlan | sales.subscription.change | N | N | Y | N | Y-AUDIT | Endpoint policy; proration computed server-side (BR-SALE-11) |
| AZ-49 | subscription : cancel | sales.subscription.cancel | N | N | N | N | Y-AUDIT | Endpoint policy; reason mandatory (BR-SALE-12) |
| AZ-50 | invoice : read | finance.invoice.read | N | N | Y | Y | Y | Endpoint policy |
| AZ-51 | invoice : issue | finance.invoice.issue | N | N | Y | N | Y-AUDIT | Endpoint policy; gapless numbering in the transaction (BR-SALE-10) |
| AZ-52 | invoice : cancel | finance.invoice.cancel | N | N | N | N | Y-AUDIT | Endpoint policy; reason mandatory |
| AZ-53 | payment : record | finance.payment.write | N | N | Y | N | Y-AUDIT | Endpoint policy; overpayment refused (BR-SALE-09) |
| AZ-54 | payment : refund | finance.payment.refund | N | N | N | N | Y-AUDIT | Endpoint policy; Owner only |
| AZ-55 | report : read | report.read | N | N | Y | Y | Y | Endpoint policy; Editor has no revenue visibility |
| AZ-56 | report : export | report.export | N | N | Y | Y | Y | Endpoint policy; every export is audited |
| AZ-57 | dashboard : read | dashboard.read | N | Y | Y | Y | Y | Endpoint policy; each role sees only the counters its permissions allow |
| AZ-58 | user : read | admin.user.read | N | N | N | Y | Y | Endpoint policy |
| AZ-59 | user : write | admin.user.write | N | N | N | N | Y-AUDIT | Endpoint policy; last Owner protected (BR-IAM-04) |
| AZ-60 | role : assign | admin.role.assign | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-61 | setting : read | admin.setting.read | N | N | N | Y | Y | Endpoint policy; secret values always masked |
| AZ-62 | setting : write | admin.setting.write | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-63 | auditLog : read | admin.audit.read | N | N | N | Y | Y | Endpoint policy; no update or delete route exists at all |
| AZ-64 | import : run | admin.import.run | N | N | N | N | Y-AUDIT | Endpoint policy; dry run first (BR-ADM-02) |
| AZ-65 | export : personalData | admin.privacy.export | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-66 | erasure : execute | admin.privacy.erase | N | N | N | N | Y-AUDIT | Endpoint policy; financial and audit rows preserved (BR-ADM-04) |
| AZ-67 | webhookEndpoint : write | integration.webhook.write | N | N | N | N | Y-AUDIT | Endpoint policy; secret stored hashed |
| AZ-68 | webhookDelivery : replay | integration.webhook.replay | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-69 | emailTemplate : write | notify.template.write | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-70 | outbox : read | notify.outbox.read | N | N | Y | Y | Y | Endpoint policy; bodies redacted for Sales |
| AZ-71 | outbox : retry | notify.outbox.retry | N | N | N | N | Y-AUDIT | Endpoint policy |
| AZ-72 | health : read | none | Y | Y | Y | Y | Y | Anonymous allowlist; `/health/ready` reveals status only, never versions or connection strings |

## Permission catalogue

The strings in the table above are the permission catalogue, seeded into `Permissions` and mapped to roles in `RolePermissions`. The code never checks a role name; it checks a permission. Adding a role later is therefore a data change, not a code change.

Role to permission mapping is generated from this file and asserted by a test: for every row above, a test signs in as each role and asserts the documented status code on the real endpoint. A row with no such test is not implemented.

## Anonymous allowlist (frozen into STATE.json at approval)

```
/health
/health/live
/health/ready
/api/v1/auth/login
/api/v1/auth/refresh
/api/v1/public/**        (published content, read-only)
POST /api/v1/public/forms/{key}/submit
```

Every other path returns 401 without a token. The D7 gate enumerates the OpenAPI document each phase and proves it: a single 200 on an unlisted path is a gate failure.

## Notes that are easy to get wrong

- **A draft is a 404, not a 403.** Telling an anonymous visitor that a hidden page exists is an information leak (NFR-AUTHZ-04).
- **Public endpoints are read-only** except the single form-submission endpoint, which is protected by captcha, honeypot and rate limit rather than by identity.
- **The Auditor writes nothing.** Any write route reachable by the Auditor role is a bug, and the matrix test proves it for every row.
- **Editors never see personal data.** Leads, organisations, invoices and the outbox are outside the Editor's permission set entirely, so a content person cannot export a customer list.
- **Owner-scoped rules are enforced in the query,** not by a client-side filter: a Sales user changing another person's lead stage is refused server-side (AZ-31).
