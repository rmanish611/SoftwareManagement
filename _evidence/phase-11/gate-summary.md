# P11 gate evidence

Nonce p11a. Gate database `SoftwareManagementDb_GateP11`, created empty by this run's migrations.

The two exceptions carried since P07 still hold: **tests ran in Debug**, because Smart App Control
refuses this machine's freshly built binaries (BLK-1, ASM-16, ASM-18), and **nothing was
published**, so **D5 and D6 are not evidenced and are not marked green**. The runtime checks ran
against the Debug build through the signed `dotnet` host, and both scripts print which mode they
were in.

`scripts/render-proof.ps1` gained the same `-FromSource` switch `phase-smoke.ps1` has, for the same
reason and with the same printed disclaimer. Before this it ran `_publish/api`, which on this
machine is a build from 20 September and does not contain any of this phase's endpoints — the
render proof would have been run against last phase's API and would have passed by rendering empty
shells.

## D1 restore

`dotnet restore backend/SoftwareManagement.sln` → `All projects are up-to-date for restore.`,
exit 0. `npm ci` exit 0.

## D2 build, zero warnings

```
dotnet build backend/SoftwareManagement.sln -c Release -warnaserror -m:1
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`dotnet format --verify-no-changes` exit 0. `ng lint` → `All files pass linting.`

## D3 tests

```
TEST_CONFIGURATION=Debug
SAC_RETRIES=0
Api.IntegrationTests  328 passed, 0 failed, 0 skipped
Application.Tests      19 passed, 0 failed, 0 skipped
Architecture.Tests      7 passed, 0 failed, 0 skipped
Domain.Tests            4 passed, 0 failed, 0 skipped
```

**358 backend tests** against P10's 313, so 45 new where `minTests` is 34. Frontend **164 Vitest
tests** against 148, so 16 new where `minFrontendTests` is 10.

Every requirement this phase delivers is named by at least two passing tests:

| REQ | tests | | REQ | tests |
|---|---|---|---|---|
| REQ-PRJ-001 | 4 | | REQ-API-001 | 2 |
| REQ-PRJ-002 | 3 | | REQ-API-002 | 2 |
| REQ-PRJ-003 | 4 | | REQ-API-003 | 4 |
| REQ-PRJ-004 | 3 | | REQ-API-004 | 3 |
| REQ-PRJ-005 | 2 | | REQ-API-005 | 2 |
| REQ-PRJ-006 | 3 | | REQ-API-006 | 2 |
| REQ-PRJ-007 | 3 | | REQ-API-007 | 2 |
| REQ-PRJ-008 | 3 | | REQ-API-008 | 3 |
| REQ-PRJ-009 | 2 | | | |

### The one defect this phase's tests found

Five of them failed the first time on `MakeCurrentAsync`, with a 500. Clearing the old current
version and setting the new one in a single `SaveChangesAsync` is a coin toss: EF gives no order to
the UPDATEs inside one save, and `IX_ApiVersions_OneCurrent` — the filtered unique index that exists
precisely to make two current versions impossible — refuses the pair whenever the set lands before
the clear. It is now two saves inside one transaction, clear then set, with the reason written above
the code. The index was right and the code was wrong, which is the outcome a constraint in the
database is for.

## D4 schema

```
20260921105851_AddPortfolio   present in __EFMigrationsHistory
TABLE_COUNT=62
DB_OBJECT Projects            = PRESENT
DB_OBJECT CaseStudies         = PRESENT
DB_OBJECT ClientLogos         = PRESENT
DB_OBJECT ApiCatalogEntries   = PRESENT
DB_OBJECT ApiVersions         = PRESENT
DB_OBJECT ProjectTechnologies = PRESENT
IX_ApiVersions_OneCurrent, IX_Projects_Slug, IX_ApiCatalogEntries_Slug
CK_Projects_Dates, CK_ApiVersions_DeprecatedHasSunset
```

`has-pending-model-changes` → "No changes have been made to the model since the last migration."

Two check constraints and a filtered unique index, because these three rules are cheap to enforce
in the database and expensive to get wrong in public: a project cannot have finished before it
started, a deprecated version cannot exist without a sunset date, and an API entry cannot have two
current versions.

`ProjectTechnologies` is not in the frozen entity table; the ERD draws the many-to-many and the
services side already has the same shape as E-06, so the join mirrors it (ASM-23).

## D5, D6 publish and health — NOT EVIDENCED

Unchanged and not claimed. `API_SOURCE=...\bin\Debug\net10.0\SoftwareManagement.Api.dll - NOT the
published artefact` in both the smoke and the render proof.

## D7 deny by default

```
ANON /api/v1/admin/products           => 401
ANON /api/v1/admin/product-categories => 401
ANON /api/v1/admin/pages              => 401
ANON /api/v1/admin/media              => 401
ANON /api/v1/leads                    => 401
ANON_ROUTES_CHECKED=5
ANON_PUBLIC /api/v1/public/catalog/categories => 200
ANON_PUBLIC /api/v1/public/catalog/products   => 200
ANON_PUBLIC /api/v1/public/forms/contact      => 200
QUOTE_DISCOUNT_403_STATUS=not-checked (no Sales account in this gate database)
PIPELINE_EDITOR_LEADS_STATUS=not-checked (no editor account in this gate database)
```

The two role checks the gate database cannot make are printed rather than skipped, as in every
phase since P07 (ASM-14). This phase's own role rules are asserted by tests against real tokens:
`REQ_PRJ_001_A_salesperson_may_read_projects_but_not_write_one` and
`REQ_API_008_An_auditor_may_read_the_sunset_report_and_an_editor_may_write_the_catalogue` both get
200 on the read and 403 on the write, which is exactly the row the authorization matrix draws for
Sales on the portfolio and the catalogue.

## D8 round trip, and the phase's own exit criteria

| # | Expected | Observed |
|---|---|---|
| 1 | a project reaches `Projects` | `PROJECT_CREATE_STATUS=201`, `PROJECT_ROWS=1` |
| 2 | a case study with no metric → 422 | `CASE_STUDY_NO_METRIC_STATUS=422`, `code=CASE_STUDY_NOT_READY`, shortfalls itemised |
| 3 | a logo without permission → anonymised label | `ANON_CLIENT_LABEL=a leading healthcare company`, and the page does not contain the client's name |
| 4 | a 30-day sunset → 422 | `SUNSET_30_DAY_STATUS=422`, `code=SUNSET_TOO_SOON`; 120 days → `SUNSET_120_DAY_STATUS=204` |
| 5 | `/developers` server-rendered with published entries | `ROUTE /developers => 200 marker='Probe API p11a' found=yes` |
| 6 | the case study's JSON-LD parses with its properties | `JSONLD_BLOCKS=1`, `JSONLD_TYPE=Article HEADLINE=PROBE-p11a PUBLISHED=2026-04-01` |

Also proved at runtime: `API_CURRENT_VERSION_ROWS=1` after publishing an entry with two versions,
the public directory carried the sunset date, and the sitemap carried both published slugs and
neither draft:

```
API_PUBLISH_STATUS=204
API_CURRENT_VERSION_ROWS=1
SITEMAP_STATUS=200   <loc>/projects/probe-project-p11a</loc>  <loc>/developers/probe-api-p11a</loc>
```

Markers 5 and 6 are content, not test ids: `Probe API p11a` and the case study's own outcome
sentence cannot appear in the HTML unless the renderer reached the API and got published rows back.

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE`, and after the render proof `PORT 4300 FREE`, `PORT 5199 FREE`.

## D10 frontend

`ng build --configuration production` exit 0.

```
INITIAL_FILES=4 INITIAL_KB=320.1 CEILING=500
LAZY_CHUNKS=33 CEILING_EACH=250
BUNDLE_BUDGETS=PASS
COMPONENT_IN_BUNDLE=app-project-list
COMPONENT_IN_BUNDLE=app-case-study
COMPONENT_IN_BUNDLE=app-api-directory
ROUTE projects       in bundle
ROUTE projects/:slug in bundle
ROUTE developers     in bundle
```

The parameterised route appears as Angular declares it, `projects/:slug`, without a leading slash —
the same spelling P10's `invoices/:id` has. The leading-slash form is what the router produces at
runtime, not what is compiled in.

```
API_SOURCE=...\bin\Debug\net10.0\SoftwareManagement.Api.dll - NOT the published artefact
NG_INDEX_STATUS=200  NG_APP_ROOT_COUNT=1
ROUTE /projects                    => 200 marker='PROBE-p11a' found=yes
ROUTE /projects/probe-project-p11a => 200 marker='The month-end close went from nine days to two.' found=yes
ROUTE /developers                  => 200 marker='Probe API p11a' found=yes
JSONLD_ROUTE=/projects/probe-project-p11a BLOCKS=1
JSONLD_TYPE=Article HEADLINE=PROBE-p11a PUBLISHED=2026-04-01
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=3 EXPECTED=3
RENDER_PROOF=PASS
```

164 Vitest tests, `ng lint` clean. Accessibility: axe over the project list, the case study and the
API directory, clean of serious and critical violations. A deprecated version is not signalled by
colour alone — it carries the word "Deprecated" and the date it stops answering.

A signed-in browser pass was again not done.

## D11 anti-stub

```
SOURCE_FILES=237 GENERATED_MIGRATION_FILES=21
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=25 FE_IT=164 FE_EXPECT=291
D11 PASS
```

## D15 coverage

```
PKG SoftwareManagement.Infrastructure = 78.6% (8316/10583)
PKG SoftwareManagement.Api            = 83.9% (7266/8658)
PKG SoftwareManagement.Domain         = 86.7% (784/904)
PKG SoftwareManagement.Application    = 94.1% (822/874)
LINE_COVERAGE=81.8% (17188/21019) FLOOR=70%
D15 PASS
```

81.8% against P10's 81% on the same Debug basis, over 1,900 more lines of production code.

## D12, D13, D14

Recorded in the closing commit.
