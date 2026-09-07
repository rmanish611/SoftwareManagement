# Blueprint index - Software Management

The corporate website of the owner's software company: company profile, product catalogue with live demos and plans, delivered projects and public APIs, lead capture with a working pipeline, and the record of the tenants and subscriptions sold. Angular 22 front end, ASP.NET Core 10 Web API, SQL Server.

`INTENT LOCK` — Software Management is the corporate website of the owner's own software company. Its primary user is a prospective customer, an SMB owner or IT decision-maker in India first and globally later, who wants to see what the company builds, open a live demo of a product such as the ERP or the hospital-management SaaS, and get in touch. The three most important jobs it does: present the company and its product catalogue the way a large IT vendor does; turn visitors into leads through spam-safe, consented forms with a pipeline the owner works from the admin panel; and let the owner run the whole site and the sales record without a developer. Out of scope: licence keys and downloads, a marketplace for other sellers, and the SaaS products themselves.

| File | What it holds |
|---|---|
| [00-assumptions.md](00-assumptions.md) | 30 assumptions with the default chosen, the basis, the impact if wrong and the cost to change |
| [01-research.md](01-research.md) | 21-source ledger with verbatim quotes, the 65-row competitive teardown, surprises, the scope boundary, and every package version with the command that printed it |
| [02-domain.md](02-domain.md) | 11 actors, 60 entities, state machines, 72 business rules, money flows, notifications, reporting, audit, compliance, 73 exception flows, time rules, migration and the human process |
| [03-data-model.md](03-data-model.md) | ER diagram, entity dictionary with SQL types and PII flags, index plan, concurrency and gapless numbering, seed data, volume estimates |
| [04-requirements.md](04-requirements.md) | **The contract.** 138 requirements across 12 modules, 98 Must (71 percent), with Given/When/Then acceptance criteria |
| [05-nfr.md](05-nfr.md) | 61 non-functional requirements across 15 categories, every one with a number and a measurement method |
| [06-authz.md](06-authz.md) | 72 resource-and-action rows, the permission catalogue, the enforcement point for each, and the frozen anonymous allowlist |
| [07-decisions.md](07-decisions.md) | 23 ADRs, each with three real options and a named disqualifier for every rejection |
| [08-environment.md](08-environment.md) | Pasted preflight output, the local footprint budget, ports, paths and required environment variables |
| [09-phase-plan.md](09-phase-plan.md) | 14 phases, each a runnable vertical slice with frozen acceptance criteria, minimum test counts, routes, selectors, tables and smoke configuration |
| [10-verification-log.md](10-verification-log.md) | 30 verified claims with the command and its output, plus the metrics and consistency gate results |
| `_pass1/` | The first-pass blueprint, kept for the record. It described a digital-goods storefront and was replaced when the owner re-specified the product. |

## Where the risk actually is

1. **The site's whole job is to be found and to convert.** That is why server-side rendering, the sitemap, the redirects on slug change and the JSON-LD are Must requirements rather than polish.
2. **The enquiry channel must never fail silently.** The two most useful sources in the entire research were people who could not tell whether their contact form had ever sent an email. That is why the outbox, the delivery log and the dashboard counter exist.
3. **The machine has 7.9 GB of RAM.** The footprint budget in `08-environment.md` is the constraint that shapes the phase plan, not an afterthought.
