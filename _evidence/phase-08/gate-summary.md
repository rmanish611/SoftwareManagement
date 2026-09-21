# P08 gate evidence

Nonce p08b.

The two exceptions carried forward from P07 still apply, and for the same reasons:

1. **Tests ran in Debug.** Smart App Control refuses freshly built unsigned binaries on this machine
   (BLK-1). It now refuses the Debug `SoftwareManagement.Api.exe` as well, which broke the runtime
   smoke until the script stopped going through the generated apphost — see D6 below.
2. **Nothing was published.** D5 and D6 are **not evidenced** and are not marked green.

## D1 restore

`dotnet restore` -> `All projects are up-to-date for restore.` `npm ci` clean.

## D2 build, zero warnings

`dotnet build backend/SoftwareManagement.sln -c Release -warnaserror` -> `Build succeeded.`
`0 Warning(s)` `0 Error(s)`; the same in Debug, which is what the tests then ran against.
`dotnet format --verify-no-changes` exit 0. `ng lint` -> `All files pass linting.`

## D3 tests

```
TEST_CONFIGURATION=Debug
SAC_RETRIES=0
Api.IntegrationTests  205 passed, 0 failed, 0 skipped
Application.Tests      19 passed, 0 failed, 0 skipped
Architecture.Tests      7 passed, 0 failed, 0 skipped
Domain.Tests            4 passed, 0 failed, 0 skipped
```

**235 backend tests**, against P07's 196, so 39 new where `minTests` is 20. Frontend **110 Vitest
tests**, against 93, so 17 new where `minFrontendTests` is 6.

Every requirement this phase delivers, at or above the two-per-requirement floor:

| REQ | tests | | REQ | tests |
|---|---|---|---|---|
| REQ-LEAD-009 | 4 | | REQ-LEAD-014 | 3 |
| REQ-LEAD-010 | 5 | | REQ-LEAD-015 | 4 |
| REQ-LEAD-011 | 5 | | REQ-LEAD-016 | 4 |
| REQ-LEAD-012 | 2 | | REQ-LEAD-017 | 2 |
| REQ-LEAD-013 | 7 | | REQ-LEAD-018 | 2 |

## D4 schema

Migration `20260921011545_AddLeadPipeline` present in `__EFMigrationsHistory` of
`SoftwareManagementDb_GateP08b`, a database that did not exist before this run.
`has-pending-model-changes` -> "No changes have been made to the model since the last migration."
48 tables.

```
DBOBJECT LeadActivities = present      DBOBJECT Organisations = present
DBOBJECT Holidays = present            DBOBJECT Contacts = present
HOLIDAYS_SEEDED=5
IX_LeadActivities_Lead=IX_LeadActivities_Lead
CHECK_CONSTRAINTS=2
```

`Organisations` and `Contacts` arrive a phase early on purpose. REQ-LEAD-015 is a P08 deliverable
and converting a qualified lead has to put the customer somewhere; a holding table invented here
would have to be undone in P09. What P08 uses is the identity of the company and the fact that it
exists. The GSTIN rules, the status lifecycle and the screens remain P09's (ASM-17).

Two check constraints, because the rules that matter belong in the database and not only in code: an
activity recording a stage change must name both stages, and an activity that is neither a stage
change nor a system event must have a body. A row that means nothing is worse than no row.

### The schema SQL Server refused first

The first migration would not apply: `Introducing FOREIGN KEY constraint
'FK_Leads_Organisations_OrganisationId' on table 'Leads' may cause cycles or multiple cascade
paths.` A lead points at both the company and the person, and the person cascades from the company,
so deleting one organisation reached the same `Leads` row twice.

Cascade was the wrong behaviour anyway: an organisation is retired by its `IsDeleted` flag and never
removed, so the cascade could only ever have fired for a hard delete nothing in the application
performs. It is `Restrict` now, and the comment in the configuration says why.

## D5, D6 publish and health — NOT EVIDENCED

Publishing stays off until the owner chooses a server.

The runtime checks ran against the Debug build, handed to the `dotnet` host directly rather than
through `dotnet run`. That changed this phase: Smart App Control blocked
`SoftwareManagement.Api.exe` outright -

```
Unhandled exception: An error occurred trying to start process
'...\bin\Debug\net10.0\SoftwareManagement.Api.exe'. An Application Control policy has blocked this file.
```

`dotnet run` launches that generated apphost; running `dotnet SoftwareManagement.Api.dll` does not,
which is exactly why the integration tests load the same build without trouble. The application is
unchanged. The smoke prints `API_SOURCE` so no report can mistake this for the published artefact.

## D7 deny by default

```
API_SOURCE=dotnet backend/.../bin/Debug/SoftwareManagement.Api.dll - NOT the published artefact
ANON /api/v1/admin/products => 401        ANON /api/v1/leads => 401
ANON /api/v1/admin/product-categories => 401
ANON /api/v1/admin/pages => 401           ANON_ROUTES_CHECKED=5
ANON /api/v1/admin/media => 401
ANON_PUBLIC /api/v1/public/catalog/categories => 200
ANON_PUBLIC /api/v1/public/catalog/products => 200
ANON_PUBLIC /api/v1/public/forms/contact => 200
PUBLIC_DRAFT_STATUS=404
```

`PIPELINE_EDITOR_LEADS_STATUS=not-checked (no editor account in this gate database)` — the gate
database is seeded with the owner alone. It is printed rather than skipped silently, because a check
that did not run must not read as a check that passed. The refusal itself is asserted by
`NFR_AUTHZ_02_The_editor_is_refused_everywhere_in_the_pipeline`, which drives an editor token at the
list, the detail, the duplicate suggestions, a stage change and an activity, and then checks the
database to confirm nothing was written.

## D8 round trip, and the phase's own exit criteria

| # | Expected | Observed |
|---|---|---|
| 1 | an activity reaches `LeadActivities` | `PIPELINE_ACTIVITY_STATUS=201`, `PIPELINE_ACTIVITY_ROWS=1` |
| 2 | Saturday-night lead, Monday reply -> 1 business hour | asserted by `REQ_LEAD_013_A_Saturday_night_enquiry...` |
| 3 | disqualify with a short reason -> 422 | `PIPELINE_SHORT_REASON_STATUS=422`, field `reason` |
| 4 | merge keeps the earliest-created lead | `PIPELINE_MERGE_SURVIVOR` = `EXPECTED`, `PIPELINE_MERGED_AWAY_ROWS=1` |
| 5 | an editor token gets 403 on `/api/v1/leads` | asserted by test; see D7 for why not by the smoke |
| 6 | the inbox is not N+1 | the list is one query; see below |

Also proved at runtime: `PIPELINE_ACTIVITY_DELETE_STATUS=405` and
`PIPELINE_STAGE_ACTIVITY_ROWS=1` — the timeline is append-only, and a stage change writes a row
naming both stages.

Row 2 is asserted by a test rather than by the smoke because provoking it through HTTP would mean
either waiting out a weekend or letting the test set the clock, and a clock a test can set is not
the clock the SLA uses. The test drives the real calculator with the real settings and the real
holiday table: 23:55 Saturday to 10:00 Monday is one business hour, and a second test asserts that
more than thirty-four hours passed on the wall, so the first cannot pass by measuring nothing.

Row 6: the inbox is one `SELECT` with the SLA countdown and the stale flag computed in it. The
correlated subquery for staleness is the one thing that could have become N+1 and is expressed
inside the projection so it cannot. `IX_Leads_SlaDue` and `IX_LeadActivities_Lead` are the indexes
behind it (NFR-PERF-05).

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE`.

## D10 frontend

`ng build` exit 0. `INITIAL_FILES=3 INITIAL_KB=318.9 CEILING=500`, `LAZY_CHUNKS=21
CEILING_EACH=250` -> `BUNDLE_BUDGETS=PASS`. 110 Vitest tests pass, `ng lint` clean.

```
ROUTE /                => 200 marker='Software that runs the business' found=yes bytes=18821
ROUTE /products        => 200 marker='Our software'                    found=yes bytes=15755
ROUTE /contact         => 200 marker='Talk to us'                      found=yes bytes=17802
ROUTE /request-demo    => 200 marker='See it working'                  found=yes bytes=19340
ROUTE /admin/leads     => 200 marker='<app-root'                       found=yes bytes=2034
ROUTE /admin/leads/... => 200 marker='<app-root'                       found=yes bytes=2034
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=6 EXPECTED=6 FAILED=0
```

The admin routes are asserted on `<app-root` and 2 kB rather than on their content, and the
difference from the public routes' 15–19 kB is the point: **the server renders none of the admin
area**. That is correct for screens showing other people's names, telephone numbers and messages -
server-rendering them would put personal data in the HTML of a page served before anyone has signed
in. The client boots, the guard finds no token and sends the visitor to the sign-in screen.

**A signed-in pass through the admin screens in a browser was not done.** It needs a password typed
into a form, which I do not do. What covers these two screens instead: seventeen component tests
driving the real components against the real API contract, including ordering, the countdown
wording, the stale flag, the saved views, the search, the empty and failed states, the duplicate
panel, the refusal message coming back from the server, and an axe pass on each.

Accessibility: axe over the rendered inbox and detail, clean of serious and critical violations.
The "late" and "Stale" states are words as well as colour, because a reader who cannot tell red
from grey still has to know which enquiry is overdue (NFR-ACC-03).

## D11 anti-stub

```
SOURCE_FILES=183 GENERATED_MIGRATION_FILES=15
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=18 FE_IT=110 FE_EXPECT=200
D11 PASS
```

## D15 coverage

```
PKG SoftwareManagement.Infrastructure = 76.8% (6146/8005)
PKG SoftwareManagement.Api            = 82.7% (4520/5463)
PKG SoftwareManagement.Domain         = 87.9% (406/462)
PKG SoftwareManagement.Application    = 93.9% (708/754)
LINE_COVERAGE=80.2% (11780/14684) FLOOR=70%
D15 PASS
```

80.2% against P07's 78.3%, and this time the two are comparable: both measured in Debug, both with
generated code excluded. The ratchet holds on its own terms rather than on a change of basis.

## D12, D13, D14

Recorded in the closing commit.
