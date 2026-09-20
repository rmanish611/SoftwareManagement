# P07 gate evidence

Nonce p07a.

Two things about this run are different from P01–P06, and both are stated before the evidence
rather than after it, because they change what the evidence proves.

1. **The tests ran in Debug, not Release.** Smart App Control on this machine refuses the Release
   build of `SoftwareManagement.Api.dll` outright — at any path, on every retry, confirmed against
   CodeIntegrity event log entries rather than inferred from the runner. See BLK-1, which records
   five attempts at getting round it. The same tests over the same source run clean in Debug.
2. **Nothing was published.** `dotnet publish`, the published-DLL smoke and any hosting work are
   off for this project until the owner chooses a server. The runtime checks below therefore ran
   against `dotnet run`, which the smoke prints as `API_SOURCE`, so no report can claim the
   stronger form by accident.

D5 and D6 are consequently **not evidenced** in the form the protocol asks for. They are not
marked green below.

## D1 restore

`dotnet restore` -> `All projects are up-to-date for restore.` `npm ci` completed; the warnings are
optional install scripts that npm declines to run by default and that nothing here needs.

## D2 build, zero warnings

`dotnet build backend/SoftwareManagement.sln -c Release -warnaserror` -> `Build succeeded.`
`0 Warning(s)` `0 Error(s)`. The same in Debug, which is the build the tests then ran against.
`dotnet format --verify-no-changes` exit 0. Frontend `ng lint` -> `All files pass linting.`

## D3 tests

```
TEST_CONFIGURATION=Debug
SAC_RETRIES=0
Api.IntegrationTests  166 passed, 0 failed, 0 skipped
Application.Tests      19 passed, 0 failed, 0 skipped
Architecture.Tests      7 passed, 0 failed, 0 skipped
Domain.Tests            4 passed, 0 failed, 0 skipped
```

**196 backend tests**, against P06's 145, so 51 new where `minTests` is 24. Frontend **93 Vitest
tests**, against 70, so 23 new where `minFrontendTests` is 8.

Tests naming each requirement this phase delivers, all at or above the two-per-requirement floor:

| REQ | tests |
|---|---|
| REQ-LEAD-001 | 10 |
| REQ-LEAD-002 | 2 |
| REQ-LEAD-003 | 3 |
| REQ-LEAD-004 | 2 |
| REQ-LEAD-005 | 2 |
| REQ-LEAD-006 | 3 |
| REQ-LEAD-007 | 2 |
| REQ-LEAD-008 | 2 |
| REQ-NOTIF-001 | 2 |
| REQ-NOTIF-002 | 2 |
| REQ-NOTIF-003 | 2 |
| REQ-NOTIF-004 | 6 |

Counting them is how two shortfalls were found rather than assumed: REQ-LEAD-007 and
REQ-NOTIF-001 each had a single test. Both got a second one that asserts the other side of the
rule — the same person writing again about something else is a second enquiry, not a duplicate,
and a refused submission leaves nothing in the queue.

Three groups of tests were added after the phase's own work was already written, for gaps that
were real rather than for the count:

- `LeadReadingTests` — the admin side of capture. `LeadsController` was 44% covered and had one
  test, the authorization one. A lead written to a table nobody can read is not a captured lead.
- `EmailSenderTests` — `SmtpEmailSender` had **no** coverage at all in a phase whose point is that
  an enquiry is never silently lost. What is asserted is the behaviour when sending does not work:
  not configured, connection refused, a mistyped sending address. A successful delivery is not
  asserted, because faking a server that always says 250 would be asserting on the fake.
- `ProductCategoryTests` — `ProductCategoriesController` was at 22%, shipped in P05. One of its
  rules is the only way to take live products off the public site by accident, which is worth a
  test. Writing them corrected a wrong assumption of mine rather than the code: the editor **may**
  archive a category (AZ-16), and the test now asserts the frozen matrix.

## D4 schema

Migration `20260908030207_AddLeadsAndOutbox` present in `__EFMigrationsHistory` of
`SoftwareManagementDb_GateP07`, a database that did not exist before this run.
`has-pending-model-changes` -> "No changes have been made to the model since the last migration."
44 tables.

Every table the phase declares:

```
DBOBJECT FormDefinitions = present    DBOBJECT ConsentRecords = present
DBOBJECT FormFields = present         DBOBJECT EmailTemplates = present
DBOBJECT FormSubmissions = present    DBOBJECT OutboxEmails = present
DBOBJECT Leads = present              DBOBJECT EmailDeliveryLogs = present
```

## D5, D6 publish and health — NOT EVIDENCED

Publishing is off by the owner's instruction until the hosting target is chosen. The API did start
and answer `/health` with 200, but from `dotnet run`, which is not the artefact D5 and D6 are about.
These two rows stay open and are carried into the phase that publishes.

## D7 deny by default

```
API_SOURCE=dotnet run (-c Debug) - NOT the published artefact
ANON /api/v1/admin/products => 401
ANON /api/v1/admin/product-categories => 401
ANON /api/v1/admin/pages => 401
ANON /api/v1/admin/media => 401
ANON /api/v1/leads => 401
ANON_ROUTES_CHECKED=5
ANON_PUBLIC /api/v1/public/catalog/categories => 200
ANON_PUBLIC /api/v1/public/catalog/products => 200
ANON_PUBLIC /api/v1/public/forms/contact => 200
PUBLIC_DRAFT_STATUS=404
```

`/api/v1/leads` is the row this phase adds. The public form definition is on the allowlist and has
to answer without a token, because the page that renders the form is served to a stranger.

## D8 round trip, and the phase's own exit criteria

Every row of the P07 exit table, against the running API and read back out of SQL Server:

| # | Expected | Observed |
|---|---|---|
| 1 | one row in `Leads` | `LEAD_SUBMIT_STATUS=202`, `LEAD_DB_ROWS=1`, `LEAD_CONSENT_ROWS=1` |
| 2 | replayed token -> 400 `CAPTCHA_INVALID` | `LEAD_REPLAY_STATUS=400`, and no lead written |
| 3 | 6th submission in 10 min -> 429 with `Retry-After` | `LEAD_FLOOD_6=429 RETRY_AFTER=600` |
| 4 | no consent -> 422 `CONSENT_REQUIRED` | `LEAD_NO_CONSENT_STATUS=422`, nothing stored |
| 5 | same body twice -> one lead, one acknowledgement | `LEAD_DUPLICATE_FIRST=202 SECOND=202`, `LEAD_DUPLICATE_ROWS=1` |
| 6 | outbox holds the message, attempts rescheduled | `LEAD_OUTBOX_QUEUED=8`, nothing sent inline |
| 7 | the submitter's address masked in the log | `LEAD_LOG_ADDRESS_IN_FULL=0` |

Row 6 is split deliberately. What the smoke proves is that the message is queued and that nothing
is sent during a request. The attempt count rising, the schedule of 1, 5, 15, 60 and 240 minutes,
and the dead letter with its alert after the fifth failure are proved by the integration tests
(`REQ_NOTIF_004`, three of them), because provoking five failures through the HTTP surface would
mean waiting out the real schedule.

The rest of the catalogue path runs in the same pass and still holds: create, duplicate slug
refused with a suggestion, plan rules, the publishing thresholds, the draft's public address as a
404, upload, attach, publish, read back, page-size cap and search.

An earlier version of this smoke reported three failures that were mine, not the product's. Every
submission carried the same email and message, and the duplicate check hashes the form key with the
email, the phone and the message — so the flood was five repeats of one enquiry and never reached
the rate limiter. The bodies vary properly now, and the reason is written into the script so the
next person does not spend the same twenty minutes.

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE`. Under `-FromSource` the process started is `dotnet run`, which
launches the application as a child; killing only the launcher leaves the child holding the port,
so the shutdown reaps it as well.

## D10 frontend

`ng build` exit 0. `INITIAL_FILES=3 INITIAL_KB=305.8 CEILING=500`, `LAZY_CHUNKS=18
CEILING_EACH=250`, largest lazy chunk 51 kB -> `BUNDLE_BUDGETS=PASS`. 93 Vitest tests pass,
`ng lint` clean.

Server-render proof, against the running API, markers read out of the HTML before any browser
JavaScript ran:

```
ROUTE /              => 200 marker='Software that runs the business' found=yes
ROUTE /products      => 200 marker='Our software'                    found=yes
ROUTE /contact       => 200 marker='Talk to us'                      found=yes
ROUTE /request-demo  => 200 marker='See it working'                  found=yes
ROUTE /request-quote => 200 marker='Get a price'                     found=yes
ROUTE /about         => 200 marker='data-testid="about-page"'        found=yes
ROUTE /services      => 200 marker='data-testid="services-page"'     found=yes
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=7 EXPECTED=7 FAILED=0
```

The three form markers are the form titles, which come from `FormDefinitions` in the database. They
cannot appear in server-rendered HTML unless the renderer reached the API, which is the discipline
P06 established after every public page turned out to be shipping to crawlers as an empty shell.

Accessibility: the axe engine runs inside the test suite over the rendered components, with colour
contrast disabled and the reason stated in the code (ASM-12). The shell with its menu open and the
home page are both clean of serious and critical violations. Lighthouse LCP remains unmeasured for
the reason recorded in ASM-11.

## D11 anti-stub

```
SOURCE_FILES=165 GENERATED_MIGRATION_FILES=13
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=16 FE_IT=93 FE_EXPECT=175
D11 PASS
```

## D15 coverage

```
PKG SoftwareManagement.Infrastructure = 73.3% (4935/6732)
PKG SoftwareManagement.Api            = 82%   (4064/4957)
PKG SoftwareManagement.Domain         = 90.2% (312/346)
PKG SoftwareManagement.Application    = 93.8% (662/706)
LINE_COVERAGE=78.3% (9973/12741) FLOOR=70%
D15 PASS
```

**The ratchet against P06's 76.5% cannot be read straight, and two things moved the number.**

*The measurement is in Debug.* Debug emits far more sequence points than Release, so the same tests
over the same code count more lines. Measured in Debug with the P06 rules, this phase came out at
72.6% (9803/13503) — lower than P06's Release figure while testing more. The two numbers are not
comparable, and treating the drop as a regression would be as wrong as treating it as a pass.

*Generated code is now excluded.* `OpenApiXmlCommentSupport.generated.cs` is 762 lines the SDK
emits and no test can reach. It was 5.6% of the denominator, reported as a shortfall in the test
suite. Excluding it applies to `*.generated.cs` the same rule the project already applies to
generated migrations (ASM-6); it is recorded as ASM-15 rather than slipped in, because it raises
the figure.

Honest split of the improvement, in Debug throughout: 72.6% before, 73.9% with the new tests alone,
78.3% once generated code is out. So about a quarter of the movement is new tests and the rest is
the measurement basis. The part that is not a definition change is `SoftwareManagement.Api` going
from 68.1% to 82%, which is the new tests and nothing else.

## D12, D13, D14

Recorded in the closing commit.
