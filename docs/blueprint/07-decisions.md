# 07 Architecture Decision Records

Every ADR compares at least three real options and names a specific disqualifier for each rejection. Versions cited here were printed by a command in `01-research.md`; nothing is quoted from memory.

## ADR-00: Database instance and SQL tooling

| Option | How it works | Pros | Cons | Fit for this project | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| SQL Server LocalDB | On-demand instance started by the SQL client | Zero idle RAM, already installed, no service to manage | Windows only, single user, no SQL Agent | High: the dev machine has 8 GB and every gate needs a real database | Free | Massive | Low |
| SQL Express service | Always-running Windows service | Agent-like features, multi-user | Idles at 200-500 MB on an 8 GB machine | Medium | Free | Massive | Low |
| SQL Server in Docker | Container with a mounted volume | Matches Linux production exactly | Docker Desktop plus WSL2 reserves 2-4 GB before the container starts | None: forbidden by the machine contract | Free | Large | Low |

**Verdict: SQL Server LocalDB `(localdb)\MSSQLLocalDB` for development, SQL Server 2022 or Azure SQL for production.** Access through `Microsoft.Data.SqlClient` with a small `Invoke-Sql` helper, because `sqlcmd` is not guaranteed present and its `-C` flag needs v18 or later. `sqlToolPath = "SqlClient"`.
*Disqualifiers:* Express is rejected for its idle memory on an 8 GB machine; Docker is rejected because the WSL2 reservation would make every Angular build a memory failure.

## ADR-01: How site content is managed

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Typed content in our own schema with an admin UI | Pages, sections, products and plans as first-class EF entities with Draft/Modified/Published states | Every rule is testable, the gates mean something, no second system to host | The admin UI must be built | High: the content shapes are known and stable | Free | n/a | High |
| Headless CMS (Strapi) alongside the API | Content lives in Strapi, Angular reads its API | Rich editing for free, draft and publish built in | A second runtime, a second database and a second auth system on an 8 GB machine; two sources of truth for products that also drive quotes | Low | Free tier | Large | Medium |
| Umbraco CMS as the host application | The .NET CMS hosts the site and the custom code | Mature .NET content model with scheduled publishing | The application becomes a CMS plugin; the sales domain does not belong there | Low | Free (Umbraco 18) | Large | Very high |

**Verdict: a typed content model in our own schema**, borrowing the two ideas that make CMSs pleasant: the three-state Draft/Modified/Published model (S-07) and editor-timezone scheduled publishing with server-time shown (S-06).
*Disqualifiers:* Strapi is rejected because product and plan rows also drive quotes, so splitting them across two databases would create the drift the whole protocol exists to prevent; Umbraco is rejected because it would invert the application, making the sales domain a plugin inside a CMS.

## ADR-02: Angular version, rendering and routing

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Angular 22 with SSR and prerendering for public routes | `@angular/ssr` renders public pages on the server; the admin is a client-side SPA | Search engines get complete HTML (NFR-SEO-01), first paint is fast, one codebase | SSR adds a Node process and a hydration class of bug | High: the site exists to be found in search | MIT | Massive | Medium |
| Angular 22, client-side only | Classic SPA | Simplest build and deploy | A marketing site that renders nothing without JavaScript defeats its own purpose | None | MIT | Massive | Low |
| Angular 21 (LTS) with SSR | Same, one major behind | Slightly more settled | Already in LTS: active support ended 2026-06-03 (S-18), so we would adopt a version already on its way out | Low | MIT | Massive | Low |

**Verdict: Angular 22.1.x with SSR plus build-time prerendering for public routes, and a lazy-loaded client-rendered admin area.** Verified versions: `@angular/cli 22.1.7`, `@angular/core 22.1.5` (S-18: v22 active until 2027-06, LTS to 2028-06).
*Disqualifiers:* client-only rendering is rejected because it fails NFR-SEO-01; Angular 21 is rejected because it entered LTS on 2026-06-03 and would need a major upgrade inside the first year.

## ADR-03: Angular UI component library

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Angular Material 22 + CDK | Official components with the CDK primitives | Accessibility built in (NFR-ACC), tracks the Angular version exactly, MIT, small when imported per component | Recognisably Material unless themed; the data grid is basic | High: the admin is forms and tables, the public site is custom-styled anyway | MIT (verified) | Massive | Medium |
| PrimeNG | Large component set including a rich data table | More components out of the box | Heavier bundle against a 500 KB initial budget; theming is its own world | Medium | MIT | Large | Medium |
| Tailwind UI / commercial kits | Purchased component templates | Fast, attractive | Paid licence; budget is zero | None | Paid | Large | Low |

**Verdict: Angular Material 22.1.5 with the CDK**, themed with design tokens (NFR-MAINT-05) so the public site does not look like a Google product.
*Disqualifiers:* PrimeNG is rejected on bundle weight against the frozen 500 KB initial budget; commercial kits are rejected because the dependency budget is zero.

## ADR-04: Validation and object mapping

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| FluentValidation 12 + hand-written mapping | Explicit validators per request; DTOs mapped by hand or by a small extension method | Rules are readable and unit-testable; mapping is explicit and debuggable | Mapping code is repetitive | High: rules like BR-SALE-04 need real logic, not attributes | Apache-2.0, supports .NET 10 (S-21) | Large | Low |
| Data annotations only | Attributes on DTOs | Nothing to install | Cannot express conditional or cross-field rules such as "email or phone" | Low | Free | Massive | Low |
| AutoMapper + annotations | Convention-based mapping | Less mapping code | AutoMapper's licence changed to commercial for newer versions and convention mapping hides breakages the compiler would otherwise catch | Low | Commercial risk | Large | Medium |

**Verdict: FluentValidation 12.1.1 for rules, hand-written mapping.** Validation failures return 422 with a field-keyed dictionary (NFR-OBS-04).
*Disqualifiers:* data annotations cannot express BR-LEAD-01 (email or phone); AutoMapper is rejected on licence risk and on silently breaking when a property is renamed.

## ADR-05: API shape, versioning and error contract

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| REST with controllers, `/api/v1/`, RFC 9457 errors | Attribute-routed controllers, OpenAPI document from `Microsoft.AspNetCore.OpenApi`, Swagger UI from Swashbuckle | The D7 gate can enumerate every route from the OpenAPI document; a familiar, testable shape | More ceremony than minimal APIs | High: the gate depends on an enumerable route list | MIT | Massive | Medium |
| Minimal APIs | Endpoints registered as lambdas | Less code | Filters, conventions and per-endpoint authorization are less uniform, and the gate's route enumeration is easier to get wrong | Medium | MIT | Massive | Low |
| GraphQL | One endpoint, client-shaped queries | Flexible for the client | The D7 "every route returns 401" gate becomes meaningless; authorization moves into resolvers | None | MIT | Large | Very high |

**Verdict: REST controllers under `/api/v1/`, OpenAPI from the built-in generator, Swagger UI in development only, and `application/problem+json` errors per RFC 9457 with a `traceId` and no stack trace (S-05).**
*Disqualifiers:* minimal APIs are rejected because uniform per-endpoint authorization and route enumeration are exactly what the gates test; GraphQL is rejected because it would defeat the D7 route sweep.

## ADR-06: Payment handling

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Record payments only, money moves outside | Owner sends a quote, the customer pays by UPI or NEFT, the payment is recorded with its reference | No PCI scope, no gateway fees, no compliance work, matches how Indian SMB software is actually sold | The customer cannot pay in one click | High: A-04, the owner's own instruction | Free | n/a | Medium |
| PSP integration (Razorpay-style payment links) | The system creates a payment link and reconciles a webhook | One-click payment, automatic reconciliation | KYC, webhook security, refund flows and a gateway fee; global tax remains the seller's problem | Medium, a natural v2 | 2-3 percent per transaction | Large | Medium |
| Merchant of Record (Paddle-style) | The MoR sells to the customer and handles global VAT | Removes global tax liability | About 5 percent plus a fixed fee, and it fits self-serve digital products, not negotiated B2B tenants with quotes | Low | ~5 percent + $0.50 (S-12 class) | Large | High |

**Verdict: record payments, do not process them, in v1.** The data model already carries Invoice and Payment with a mode and a reference, so adding a payment link later is an endpoint plus a webhook, not a redesign.
*Disqualifiers:* a PSP is deferred because it adds KYC and webhook surface before there is a single customer; an MoR is rejected because the sale is a negotiated B2B tenant with a quote, not a self-serve checkout.

## ADR-07: Architecture style

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Modular monolith | One deployable, folders by module (Site, Catalog, Lead, Sales, Notify, Integration), enforced boundaries | One process to run on 8 GB, one transaction boundary, testable seams | Discipline is needed to keep modules from reaching into each other | High | Free | n/a | Medium |
| Layered monolith without module boundaries | Controllers, Services, Repositories folders | Familiar | By phase 10 every service depends on every other; the "one big service" outcome | Medium | Free | n/a | Low |
| Microservices | Separate services per module | Independent scaling | Multiple processes, network failure modes and distributed transactions on a single 8 GB machine | None | Free | Large | Very high |

**Verdict: a modular monolith**, with an architecture test (NetArchTest-style, hand-written) asserting that a module never references another module's internals, only its published contract.
*Disqualifiers:* the plain layered approach is rejected because it has no mechanism to prevent the layers collapsing into each other; microservices are rejected on the machine constraint alone.

## ADR-08: Identity and session handling

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| ASP.NET Core Identity + JWT access + rotating refresh cookie | Identity owns users and hashing; a short access token plus an HttpOnly refresh cookie | Standard, self-contained, no external dependency or cost, reuse detection is straightforward | Refresh rotation must be implemented carefully | High: 5 admin users, no public login | MIT | Massive | Medium |
| Cookie authentication only | Classic server session cookie | Simplest; no token handling | The SSR and SPA split plus future mobile or CLI clients want a bearer token | Medium | MIT | Massive | Low |
| External IdP (Entra ID, Auth0) | Identity is delegated | MFA and lifecycle for free | A monthly cost and an external dependency for a five-user back office; offline development becomes awkward | Low | Paid above a small free tier | Large | Medium |

**Verdict: ASP.NET Core Identity with EF Core stores, a 15-minute JWT access token, and a 14-day rotating refresh token with reuse detection that revokes the whole chain (BR-IAM-03).**
*Disqualifiers:* cookie-only is rejected because a bearer token is needed for SSR-to-API calls and future clients; an external IdP is rejected on recurring cost for five users and on making local development depend on a network.

## ADR-09: Data access - is Repository plus Unit of Work justified?

This is a hypothesis to evaluate, not an instruction.

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Plain EF Core `DbContext` in application services | `DbContext` is the unit of work; `DbSet<T>` is the repository | No layer that only forwards calls; full LINQ, `Include`, projections and `AsNoTracking` available; fewer places for a bug to hide | Application services depend on EF Core types | High | MIT | Massive | Low |
| Repository + Unit of Work over EF Core | Interfaces wrapping `DbSet` and `SaveChanges` | Swappable in theory; mockable | `DbContext` already is a unit of work and `DbSet` already is a repository; the wrapper leaks `IQueryable` or blocks projections, and nobody has ever swapped the ORM | Low | MIT | Massive | Medium |
| Specification pattern over EF Core | Query objects composed and executed by a generic repository | Reusable queries, testable in isolation | Real value only when the same complex query recurs across modules; here queries are mostly simple and module-local | Medium | MIT | Large | Medium |

**Verdict: plain EF Core.** `DbContext` per request, application services own the transaction, integration tests run against a real LocalDB rather than mocks. Where a query is genuinely reused (published-content filters, SLA windows), it becomes a named `IQueryable` extension, not a repository.
*Disqualifiers:* Repository plus Unit of Work is rejected because it duplicates what `DbContext` and `DbSet` already provide and would block the projections NFR-PERF-06 depends on; the specification pattern is rejected as premature for module-local queries, and may be revisited if three modules ever need the same complex query.

## ADR-10: Solution layout

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Four projects: Domain, Application, Infrastructure, Api, plus test projects | Dependencies point inward; EF Core lives in Infrastructure | Architecture tests can enforce it; the migrations project is unambiguous | Four projects to build on a small machine | High | Free | n/a | Medium |
| Single project | Everything in the API project | Fastest build | No compile-time boundary; the modular monolith becomes a wish | Low | Free | n/a | Low |
| Project per module | Site, Catalog, Lead, Sales as separate assemblies | Hard boundaries | Twelve-plus projects; build time and memory on 8 GB become the bottleneck | Low | Free | n/a | High |

**Verdict: `backend/src/{Domain, Application, Infrastructure, Api}` with modules as folders inside Application and Infrastructure, and `backend/tests/{Domain.Tests, Application.Tests, Api.IntegrationTests, Architecture.Tests}`.** Frontend in `frontend/` as one Angular workspace with feature libraries as folders.
*Disqualifiers:* a single project is rejected because nothing then prevents the Domain referencing EF Core; project-per-module is rejected on build time and memory on this machine.

## ADR-11: How public forms are defined

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Form definitions as data, fixed field types | `FormDefinition` and `FormField` rows render a typed Angular form | The owner can change labels, order and consent text without a deployment; validation stays server-authoritative | New field types need code | High: five known forms with different questions (S-10) | Free | n/a | Medium |
| Hard-coded forms in Angular | Each form is a component | Simplest | Every wording change is a deployment; the owner is blocked on a developer | Low | Free | n/a | Low |
| Full form builder with dynamic schema | Runtime-defined fields, conditional logic | Marketing autonomy | A product in itself, and it makes server-side validation generic and weak | Low | Free | n/a | High |

**Verdict: form definitions as data with a fixed set of field types** (text, email, phone, textarea, select, checkbox, product picker), each with server-side validation.
*Disqualifiers:* hard-coded forms are rejected because consent text must be editable without a release; a full builder is rejected because generic validation cannot express BR-LEAD-01 and BR-LEAD-06 rigorously.

## ADR-12: Meeting scheduling

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Embed the owner's existing scheduling link | An external booking page, loaded after consent, with the contact form as fallback | Zero build, real calendar integration, free tier is enough for one owner (S-14) | A third-party iframe and a cookie-consent dependency | High | Free tier | Large | Low |
| Build slot booking in the system | Availability, slots, timezone handling, reminders, calendar sync | Fully owned | A calendar product inside a website; timezone and double-booking bugs are guaranteed | Low | Free | n/a | High |
| No scheduling at all | Only the contact form | Nothing to maintain | Loses the buyer who wants to talk now | Medium | Free | n/a | Low |

**Verdict: embed a configured scheduling link, loaded only after consent, with the enquiry form always present as the fallback (REQ-INT-008).**
*Disqualifiers:* building slot booking is rejected as a separate product; offering nothing is rejected because a demo request that cannot become a conversation is a lost sale.

## ADR-13: Background jobs

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| `IHostedService` with a database-backed outbox and a lock table | A timer polls the outbox and the due-work tables inside the same application | No extra dependency; the durable state is our own tables, which the gates can inspect | Scheduling, retries and locking are written by hand (about 200 lines) | High: the outbox must be our own table anyway for NFR-OBS and REQ-NOTIF-005 | Free | n/a | Low |
| Hangfire Core with SQL Server storage | A job server with a dashboard and retries | Mature retries and a visible dashboard; free under LGPL even commercially (S-20) | Its own schema and dashboard security surface; the retry policy we need is already spelled out in BR-NOTIF-02 | Medium | LGPL-3.0, free (Pro from $500/year) | Large | Low |
| Windows Task Scheduler or cron calling an endpoint | External scheduler | Trivial | Scheduling lives outside the repository, so a clean clone does not reproduce it (NFR-DEP-01) | Low | Free | n/a | Low |

**Verdict: `IHostedService` plus our own outbox and a `ScheduledJobRun` table, with a database lock so a second instance cannot double-send (NFR-DEP-05).** Hangfire 1.8.25 stays the documented fallback if job volume ever justifies it, and its LGPL position is recorded so the choice can be made without re-researching.
*Disqualifiers:* Hangfire is deferred because we still need our own outbox table for the delivery log that REQ-NOTIF-005 demands, so it would add a second scheduling system rather than replace one; an external scheduler is rejected because a clean clone must reproduce the whole system.

## ADR-14: Frontend test runner

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Vitest via the Angular builder | Node-based runner, no browser | Headless, fast, low memory, no Chrome dependency in CI | Needs jsdom for DOM APIs; a browser-only bug can slip through | High on an 8 GB machine | MIT | Growing | Low |
| Karma + ChromeHeadless | Runs in a real Chrome | Real browser semantics | A Chrome process per run on a memory-constrained machine; Karma is deprecated | Medium | MIT | Shrinking | Low |
| Jest with a custom preset | Node-based | Large ecosystem | Angular's official support has moved on; the preset is community-maintained | Low | MIT | Large | Low |

**Verdict: Vitest through the Angular 22 test builder,** with component tests asserting rendered DOM text or an `HttpTestingController` expectation; `toBeTruthy()` alone counts as zero (D11).
*Disqualifiers:* Karma is rejected on memory and on deprecation; Jest is rejected because its Angular preset lags each major release, which is exactly the upgrade pain NFR-MAINT-01 exists to avoid.

## ADR-15: Frontend state management

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Angular signals plus small feature stores | Signals in services, one store per feature | Built in, no dependency, excellent with SSR hydration | Conventions must be agreed and enforced | High: the admin is CRUD screens and the public site is read-mostly | MIT | Massive | Low |
| NgRx | Redux with actions, reducers and effects | Predictable, great tooling, scales to large teams | Boilerplate far beyond the need; a one-person team pays the cost daily | Low | MIT | Large | Medium |
| Plain services with BehaviorSubject | Ad-hoc RxJS state | Familiar | Change detection and hydration issues that signals were designed to fix | Medium | MIT | Massive | Low |

**Verdict: Angular signals with a small store per feature and typed HTTP services.**
*Disqualifiers:* NgRx is rejected as boilerplate a solo maintainer pays for every day; BehaviorSubject services are rejected because signals are the supported answer in Angular 22 and hydrate cleanly under SSR.

## ADR-16: Bot protection and rate limiting

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Cloudflare Turnstile with server-side siteverify, plus honeypot and ASP.NET Core rate limiting | Token issued client-side, verified once server-side with an idempotency key (S-04); limits from `Microsoft.AspNetCore.RateLimiting` (S-19) | Free, privacy-friendly, no puzzle for the user; fail-open path keeps the enquiry channel alive (EX-183) | A third-party dependency on every form | High | Free | Large | Low |
| Google reCAPTCHA v3 | Score-based | Familiar | Sends visitor data to an advertising network, which sits badly with the DPDP consent posture | Medium | Free | Massive | Low |
| Honeypot and rate limit only | No third party | Zero dependency | Stops naive bots only; the research shows form spam is the top complaint (S-15, S-16) | Low | Free | n/a | Low |

**Verdict: Turnstile plus honeypot plus rate limiting, all three.** Turnstile failure is fail-open with the submission marked `CaptchaUnverified` and the strict limit applied, because losing a real enquiry costs more than storing a spam row.
*Disqualifiers:* reCAPTCHA is rejected on the privacy posture; honeypot alone is rejected because the complaint threads show it is not enough.

## ADR-17: Schema ownership and migration strategy

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| EF Core migrations, one per phase, applied in production as an idempotent script | `dotnet dotnet-ef migrations script --idempotent` runs during deployment | The model is the source of truth; the script is reviewable before it touches production | Generated SQL must be read, not trusted blindly | High | Free | Massive | Low |
| `Database.Migrate()` at startup | The application migrates itself | One less step | A bad migration takes the site down at boot and two instances race | Low | Free | Massive | Low |
| Hand-written SQL scripts (DbUp-style) | Scripts are the source of truth | Total control | The model and the database drift silently; EF's drift check is lost | Medium | Free | Large | Medium |

**Verdict: EF Core migrations, one migration per phase, applied through an idempotent script; `has-pending-model-changes` runs in every gate; migration files are frozen once applied and corrected by a new migration.** `EnsureCreated()` is banned.
*Disqualifiers:* startup migration is rejected because it makes a bad migration an outage; hand-written scripts are rejected because they forfeit the automated drift check the D4 gate relies on.

## ADR-18: Deployment target, CI and monthly cost

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| One small Linux VM in an India region, SQL Server on the same VM, behind Cloudflare | Publish, copy, run behind a reverse proxy | Lowest cost, full control, database and app in one place | The owner is the operator; scaling is manual | High: NFR-DEP-04 caps cost at INR 3,000 per month | ~INR 1,500-2,500/month | Large | Medium |
| Azure App Service + Azure SQL | Managed platform | Managed backups, easy scaling, no OS patching | Basic App Service plus a basic SQL tier is already near or above the cap | Medium | ~INR 3,000-6,000/month | Massive | Low |
| Shared Windows hosting | Traditional IIS hosting | Cheapest | No control over .NET runtime versions or background processes; SSR and jobs become fragile | Low | ~INR 500/month | Shrinking | Medium |

**Verdict: one small VM in an India region with SQL Server on the same host, Cloudflare in front, GitHub Actions on `windows-latest` for CI (build, unit tests, Angular production build), and deployment by a documented script.** Integration tests carrying `[Trait("Category","Integration")]` run only in the local gate because no SQL Server exists on the hosted runner (NFR-MAINT-04).
*Disqualifiers:* Azure PaaS is deferred because a basic pair already threatens the INR 3,000 cap before there is revenue; shared hosting is rejected because SSR and background jobs need process control.

## ADR-19: Logging and tracing

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| Serilog to rolling files with structured JSON and a correlation id | Enrichers add request id, user and route | No external service, greppable, cheap; rotates with a size cap | Searching is manual on a single VM | High | Apache-2.0 | Massive | Low |
| Built-in `ILogger` to console only | Framework default | Nothing to add | On a VM the console goes nowhere; NFR-OBS-02 needs structure | Low | Free | Massive | Low |
| Hosted APM (Application Insights, Seq, Datadog) | Telemetry shipped to a service | Query, dashboards, alerts | Monthly cost or a free tier with retention limits; personal data must be scrubbed before it leaves the machine | Medium | Paid above free tiers | Massive | Low |

**Verdict: Serilog 10.0.0 writing structured JSON to rolling files with a 7-day retention and a 200 MB cap, a correlation id on every request, and scrubbing enforced by NFR-PRIV-03.** A hosted APM stays a later addition behind the same interface.
*Disqualifiers:* console-only is rejected because it satisfies no requirement in section 9; a hosted APM is deferred on cost and on the PII scrubbing work it would require first.

## ADR-20: Email transport

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| MailKit over SMTP with our own outbox | Queue in our table, send with MailKit, log every attempt | Provider-independent; the delivery log answers "was it sent?" (S-17); a provider change is a configuration change | Deliverability depends on correct SPF and DKIM | High | MIT | Massive | Low |
| Provider SDK (SendGrid, Resend, Brevo) | Vendor SDK and API | Deliverability tooling, webhooks, dashboards | Locks the code to one vendor; free tiers change; the outbox is still needed | Medium | Free tier then paid | Large | Medium |
| `System.Net.Mail.SmtpClient` | Built-in client | No dependency | Documented by Microsoft as obsolete for new development, with weaker TLS handling | Low | Free | n/a | Low |

**Verdict: MailKit 4.17.0 over SMTP, driven by our own outbox with the retry schedule in BR-NOTIF-02, the envelope sender always on the company domain (BR-NOTIF-03), and a startup check that warns until SPF and DKIM verify.**
*Disqualifiers:* a provider SDK is rejected for vendor lock-in when the outbox must exist regardless; `SmtpClient` is rejected because Microsoft recommends against it for new code.

## ADR-21: Image processing

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| SkiaSharp | Native Skia bindings for resize and encode | MIT, cross-platform, fast | A native dependency per platform | High | MIT | Large | Low |
| SixLabors.ImageSharp | Managed image library | Pure managed, pleasant API | Split licence with revenue and headcount thresholds; free use is conditional | Low | Conditional | Large | Low |
| `System.Drawing.Common` | GDI+ wrapper | Familiar | Unsupported outside Windows since .NET 7 | None | Free | Shrinking | Low |

**Verdict: SkiaSharp 4.151.2** for the 1920 px web version and the thumbnail generated on upload (REQ-SITE-009).
*Disqualifiers:* ImageSharp is rejected because its licence depends on revenue thresholds that could change the project's position without a code change; `System.Drawing.Common` is rejected as unsupported outside Windows.

## ADR-22: Test assertion library

| Option | How it works | Pros | Cons | Fit | Licence + cost | Ecosystem | Reversal cost |
|---|---|---|---|---|---|---|---|
| AwesomeAssertions | Apache-2.0 fork of the fluent assertion API | Same syntax the team knows, free for commercial use | A younger project | High | Apache-2.0 | Growing | Very low |
| FluentAssertions 8 | The original | Mature | Version 8 requires a paid licence for commercial use; nothing fails at build time, so the breach would be silent | None | Paid | Massive | Very low |
| xUnit's built-in `Assert` | Framework assertions | Zero dependency | Less readable failure messages on collection and object comparisons | Medium | Apache-2.0 | Massive | Very low |

**Verdict: AwesomeAssertions 9.6.0** with xUnit 2.9.3, NSubstitute 6.2.0 and coverlet 10.0.1.
*Disqualifiers:* FluentAssertions 8 is rejected because commercial use requires a paid licence and the breach would be invisible; raw `Assert` is rejected because weaker failure messages slow down every red test, which the escalation ladder punishes.

## Support-window summary

| Component | Version | Support status |
|---|---|---|
| .NET | 10.0.400 SDK, ASP.NET Core 10.0.11 | LTS |
| Angular | 22.1.x | Active support until 2027-06, LTS until 2028-06 (S-18) |
| SQL Server | LocalDB in development, 2022 or Azure SQL in production | Supported |
| Node | 24.13.0 | Active LTS line |

A component leaving support becomes a `BLOCKERS.md` entry with a named upgrade phase, never a silent risk (NFR-MAINT-01).
