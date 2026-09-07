# 05 Non-Functional Requirements

No requirement here contains the words fast, secure, scalable, robust or optimised without a number and a named measurement method. Every row is a gate someone can run.

## 1. Performance

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-PERF-01 | The ten hottest API endpoints answer within p50 200 ms, p95 500 ms, p99 1200 ms at 100 concurrent users, measured server-side excluding network. Hot list: GET /api/v1/public/products, GET /api/v1/public/products/{slug}, GET /api/v1/public/pages/{slug}, POST /api/v1/public/forms/{key}/submit, GET /api/v1/leads, GET /api/v1/leads/{id}, GET /api/v1/quotes, GET /api/v1/dashboard/summary, POST /api/v1/auth/login, GET /api/v1/media. | A k6 or JMeter script in `tools/load/` run against the published API; the p50/p95/p99 table is pasted in the phase report of the phase that introduces each endpoint. |
| NFR-PERF-02 | A server-rendered public page reaches Largest Contentful Paint under 2.5 s and Interaction to Next Paint under 200 ms on a simulated 4G connection and a mid-tier mobile device. | Lighthouse CI run against the production build, mobile preset, on the home page and one product page. |
| NFR-PERF-03 | The initial JavaScript bundle is at most **500 KB** transferred and each lazy route chunk at most **250 KB** transferred. These exact numbers are frozen into `angular.json` budgets in P02 and are a hashed guardrail: raising them in either direction is an R-13 violation. | `ng build --configuration production` fails the build when a budget is exceeded; the budget block is pasted in every phase report. |
| NFR-PERF-04 | No API list endpoint returns more than 100 rows in one response; the default page size is 20 and a larger request is clamped, never rejected. | An integration test requests `pageSize=100000` on every list endpoint and asserts at most 100 items. |
| NFR-PERF-05 | The admin dashboard summary is computed in at most 4 database round trips and returns within 800 ms at year-3 volumes (15,000 leads, 4,000 invoices). | Seeded volume test plus an EF Core command interceptor counting round trips. |
| NFR-PERF-06 | No endpoint issues an N+1 query: any request executing more than 12 SQL statements fails the test suite. | A test-only `DbCommandInterceptor` counting commands per request in the integration test host. |

## 2. Concurrency

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-CONC-01 | The system sustains 100 concurrent users and 20 writes per second with no request exceeding 5 s and zero deadlocks over a 10-minute run. | Load script plus `sys.event_log` deadlock count checked before and after. |
| NFR-CONC-02 | Concurrent edits to the same record never silently overwrite: the second writer receives 409 with the current row version. | An integration test issuing two updates with the same `RowVersion`. |
| NFR-CONC-03 | Quote and invoice numbering is gapless and collision-free under 20 concurrent creates. | A test creating 20 documents in parallel and asserting a contiguous number range with no duplicates. |

## 3. Data volume and retention

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-DATA-01 | Year-1 capacity: 2,000 leads, 300 quotes, 60 tenants, 500 invoices, 400 content items, 2,000 media files, without schema change. | Seed script at year-1 volumes; the full gate must still pass. |
| NFR-DATA-02 | Year-3 capacity: 15,000 leads, 2,500 quotes, 300 tenants, 4,000 invoices, 1,500 content items, 10,000 media files, under 1 million rows in total, on a single SQL Server instance. | Seed script at year-3 volumes with the p95 targets of NFR-PERF-01 still met. |
| NFR-DATA-03 | An uploaded image is at most 10 MB and a document at most 25 MB; total media storage is capped at 20 GB with an alert at 80 percent. | Upload tests at the boundary; a scheduled job comparing storage size against the cap. |
| NFR-RET-01 | Retention is enforced automatically: leads and submissions anonymised 3 years after last activity, email delivery logs deleted after 12 months, audit rows kept 7 years, exports expired after 7 days. | The retention job's `ScheduledJobRun` row plus a test that ages rows and asserts the outcome. |

## 4. Security

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-SEC-01 | The application satisfies OWASP ASVS 4.0 Level 2 for authentication, session management, access control, input validation, error handling and logging. Each control is mapped in `docs/SECURITY.md`. | Checklist review each phase plus the automated checks below. |
| NFR-SEC-02 | Access tokens live 15 minutes, refresh tokens 14 days with rotation and reuse detection; an account locks for 15 minutes after 5 failed attempts in 15 minutes. | Integration tests asserting each boundary value. |
| NFR-SEC-03 | Every response carries `Content-Security-Policy` (no `unsafe-inline` for scripts), `Strict-Transport-Security` with `max-age=31536000`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and `X-Frame-Options: DENY`. | An integration test asserting each header on a public and an admin route. |
| NFR-SEC-04 | CORS names the site's own origins explicitly. `AllowAnyOrigin` combined with credentials appears nowhere in the codebase. | A source scan in the anti-stub gate asserting zero matches. |
| NFR-SEC-05 | Rate limits: 5 form submissions per IP per 10 minutes, 10 login attempts per IP per 15 minutes, 300 public API requests per IP per minute; each returns 429 with `Retry-After`. | Integration tests firing the limit plus one, using ASP.NET Core rate limiting. |
| NFR-SEC-06 | Uploads are validated by magic number, not by file extension or client content type; executable and script types are rejected with 415. | A test uploading a renamed executable. |
| NFR-SEC-07 | No secret is ever committed. The staged-index secret scan must exit clean before every commit. | The G9 gate scan; a broken scan is a failed gate. |
| NFR-SEC-08 | Dependencies carry no known high or critical vulnerability at the time of each phase gate. | `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev`, both pasted. |

## 5. Authorization

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-AUTHZ-01 | Deny by default: every route outside the anonymous allowlist returns 401 without a token. The allowlist is exactly `/health`, `/health/live`, `/health/ready`, `/api/v1/auth/login`, `/api/v1/auth/refresh` and the public read endpoints under `/api/v1/public/**`. | The D7 gate enumerates every path in the OpenAPI document and asserts 401. |
| NFR-AUTHZ-02 | A token whose role lacks the endpoint's permission receives 403, never 200 and never a partially filtered result. | The D7b gate signs in as a low-privilege user and asserts 403 on a restricted route. |
| NFR-AUTHZ-03 | Authorization is enforced server-side at the endpoint and, where data is owner-scoped, in the query. Hiding a button is not authorization. | Code review checklist plus tests calling the API directly with the wrong role. |
| NFR-AUTHZ-04 | Public read endpoints return only `Published` content; a draft is indistinguishable from a non-existent item (404, never 403). | Integration tests requesting a draft slug anonymously. |

## 6. Privacy and PII

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-PRIV-01 | The PII inventory in `docs/PRIVACY.md` lists every column holding personal data, its purpose, its retention and its erasure behaviour, and is updated in the same commit as any schema change that adds one. | A test asserting that every column marked PII in the model has an entry in the inventory file. |
| NFR-PRIV-02 | Consent is captured with its exact text, version, purpose, timestamp and IP, and is never mutated. | A test attempting an update on a consent row and asserting failure. |
| NFR-PRIV-03 | Logs never contain a password, token, captcha secret, demo credential, full email address or full IP address; emails are masked as `a***@example.com` and IPv4 addresses truncated to /24. | A log-scrubbing test asserting the masked forms, plus the A2.6 acceptance scan over the API log. |
| NFR-PRIV-04 | No third-party script, cookie or beacon loads before consent; the consent choice persists 180 days and is revocable from the footer. | An automated check of network requests on first load with no consent cookie present. |

## 7. Availability, RPO and RTO

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-AVAIL-01 | Target availability 99.5 percent monthly for public pages, measured by an external uptime check every 5 minutes; that allows about 3 hours 39 minutes of downtime per month. | Uptime monitor report; the number is stated publicly nowhere until it is measured for a full month. |
| NFR-AVAIL-02 | RPO 24 hours and RTO 4 hours: the system can be restored from the most recent nightly backup within 4 hours by following `docs/DEPLOY.md` alone. | The A4 restore drill, timed and pasted. |
| NFR-AVAIL-03 | The public site continues to serve cached pages for at least 10 minutes when the database is unreachable, and form submission fails with an honest message showing the contact email and phone number, never a blank error. | A test with the database stopped, asserting the cached response and the error page content. |
| NFR-AVAIL-04 | Health endpoints distinguish liveness from readiness: `/health/live` answers without touching the database, `/health/ready` checks the database and the outbox and returns 503 when either is unhealthy. | Integration tests with the database available and unavailable. |

## 8. Backup and restore

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-BACKUP-01 | A verified backup exists at the end of every phase: a source archive with its SHA-256 and entry count, a database `.bak` that passes `RESTORE VERIFYONLY`, and a `git bundle` that passes `git bundle verify`. | The D13 gate, pasted every phase. |
| NFR-BACKUP-02 | A restore drill is executed in final acceptance: the latest `.bak` is restored into a scratch database, the elapsed time recorded, and the scratch database dropped. An untested backup does not exist. | The A4 drill, pasted. |
| NFR-BACKUP-03 | Backup retention: the last 3 phase source archives plus the archive of the last verified phase are always kept; no gate run may delete more than one phase's backups. | The retention block in the G10 gate, with `BACKUPS_RETAINED` printed. |

## 9. Observability

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-OBS-01 | Every request carries a correlation id, present in structured logs, in the `traceId` of any error response and in every audit and outbox row it produces. | A test asserting the same id in the response header, the log line and the audit row. |
| NFR-OBS-02 | Logs are structured JSON with level, timestamp in UTC, correlation id, user id where present, route and duration. Console writes are banned in application code. | The anti-stub scan asserts zero `Console.WriteLine` in `src/**`. |
| NFR-OBS-03 | Every error response is `application/problem+json` per RFC 9457 with `type`, `title`, `status`, `detail`, `instance` and `traceId`, and never a stack trace or an inner exception message. | An integration test on a deliberate failure endpoint asserting the shape and the absence of `.cs:line`. |
| NFR-OBS-04 | Validation failures return 422 with a field-keyed error dictionary; the shape is identical across every endpoint. | A contract test over three different endpoints. |
| NFR-OBS-05 | Every background job writes a `ScheduledJobRun` row with outcome, duration and items processed, and a job that has not run for twice its interval turns the readiness check amber on the status page. | A test advancing the clock and asserting the status. |

## 10. Internationalisation

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-I18N-01 | No user-facing string is hard-coded in a template or a controller: every string comes from a resource file, so adding Hindi is a translation task and not a code change. | A source scan asserting no literal text nodes in Angular templates outside the resource pipeline, run in the anti-stub gate. |
| NFR-I18N-02 | All money carries an explicit currency code and all instants are UTC in storage and ISO-8601 with offset on the wire; no endpoint returns a naive local time. | A contract test asserting the serialised shape on three endpoints. |
| NFR-I18N-03 | Dates, numbers and currency render in the viewer's locale with `Asia/Kolkata` and `en-IN` as defaults. | A component test rendering a known instant and asserting the formatted output. |

## 11. Accessibility

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-ACC-01 | Every public page and every admin screen meets WCAG 2.2 Level AA, including 1.4.3 contrast at 4.5:1 for normal text and 2.5.8 Target Size (Minimum) at 24 by 24 CSS pixels. | `axe-core` in CI with zero violations of impact serious or critical, run against every route added by the phase. |
| NFR-ACC-02 | Every interactive element is reachable and operable by keyboard alone with a visible focus indicator, and no focus trap exists outside a dialog that can be dismissed with Escape. | A manual keyboard pass per phase, recorded in the phase report, plus automated focus-order tests on forms. |
| NFR-ACC-03 | Every form field has a programmatically associated label, every error is announced by an assistive technology, and every image carries alt text or an explicit empty alt for decoration. | axe rules plus a component test asserting `aria-describedby` on an errored field. |

## 12. Browser, device and hardware

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-BROW-01 | The last two versions of Chrome, Edge, Firefox and Safari, plus Chrome on Android and Safari on iOS, render every page without horizontal scrolling at 320, 768, 1024 and 1440 CSS pixels. | Responsive checks at the four widths in the phase gate; a manual pass on one real phone before final acceptance. |
| NFR-BROW-02 | The development machine constraint is 8 GB RAM: no gate step may exceed 3 GB working set, and never two heavy builds at once. | `Available MBytes` measured before every gate and pasted; the Node heap sized from it. |

## 13. Maintainability

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-MAINT-01 | The stack is pinned to versions in active support: .NET 10 (LTS), Angular 22 (active until 2027-06), SQL Server 2022 or Azure SQL. A dependency leaving support is a BLOCKERS entry, not a silent risk. | Version print at every gate (R-30) plus the support table in 07-decisions. |
| NFR-MAINT-02 | Line coverage is at least **70 percent** overall and never lower than the previous phase; the domain and application layers are at least 80 percent. | `dotnet test --collect:"XPlat Code Coverage"` parsed from Cobertura at the D15 gate. `nfrCoverageFloor = 70`. |
| NFR-MAINT-03 | The backend builds in Release with `-warnaserror` and zero warnings. `NoWarn` exists in exactly one file, scoped to generated EF migrations. `#pragma warning disable` appears nowhere. | The D2 and D11 gates. |
| NFR-MAINT-04 | Tests carrying `[Trait("Category","Integration")]` need a database and run only in the local gate; CI runs the rest on `windows-latest` and prints the filtered-out count. This split is a documented scope boundary, not a skipped test: `Skipped: 0` still holds locally. | The CI workflow log plus the local gate summary. |
| NFR-MAINT-05 | The frontend uses a single design-token file and a shared component library; no component declares a raw hex colour or a raw pixel font size outside the token file. | A stylelint rule failing the lint gate on a raw colour outside tokens. |
| NFR-MAINT-06 | Public API surface is versioned under `/api/v1/`; a breaking change requires `/api/v2/` and an ADR, never a silent change to v1. | Contract tests pinned to the v1 shape. |

## 14. Deployability and cost

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-DEP-01 | A clean clone reaches a running application in at most 10 documented commands using only `README.md` and `docs/ENVIRONMENT.md`, with no undocumented secret, file or manual SQL step. | The A3 clean-clone acceptance test from a fresh clone with isolated NuGet and npm caches. |
| NFR-DEP-02 | Schema changes ship as an idempotent SQL script generated by `dotnet dotnet-ef migrations script --idempotent`; the application never calls `Database.Migrate()` at startup in production and never `EnsureCreated()`. | A source scan plus the deployment runbook. |
| NFR-DEP-03 | CI on `windows-latest` runs tool restore, restore, build with `-warnaserror`, unit tests, `npm ci` and the production Angular build, and is green before final acceptance. | The GitHub Actions run URL and conclusion, pasted. |
| NFR-DEP-04 | Monthly running cost stays at or under **INR 3,000** for the first year: one small VM or App Service, SQL Server on the same machine or a basic managed tier, Cloudflare free tier, and an SMTP relay's free or lowest paid tier. Any choice pushing past that needs an ADR. | The cost table in 07-decisions and 08-environment. |
| NFR-DEP-05 | The application runs as a single instance in v1. Every scheduled job takes a database-backed lock so that a second instance, if ever started, cannot double-send. | A test running two schedulers against one database and asserting a single send. |

## 15. Search engine visibility

| ID | Requirement | Measurement method |
|---|---|---|
| NFR-SEO-01 | Every public route is server-rendered and returns complete HTML with title, meta description, canonical link and Open Graph tags without executing JavaScript. | `curl.exe` of each public route asserting the tags in the raw response body. |
| NFR-SEO-02 | `sitemap.xml` lists only published public URLs, regenerates within 5 minutes of a publish, stays under 50,000 URLs and 50 MB, and `robots.txt` disallows `/admin` and every API route. | An integration test comparing the sitemap against the published set. |
| NFR-SEO-03 | Every changed slug on a published item leaves a 301 redirect; no published URL ever returns 404 after a rename. | A test renaming a published item and asserting 301 then 200. |
| NFR-SEO-04 | Product pages emit valid `SoftwareApplication` JSON-LD and the site emits `Organization` JSON-LD; both parse as valid JSON and carry the required properties. | A test parsing the rendered JSON-LD and asserting required fields. |
