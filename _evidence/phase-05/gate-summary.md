# P05 gate evidence

Nonce p05gate.

## D2 build

`dotnet build backend/SoftwareManagement.sln -c Release -warnaserror` -> `0 Warning(s)` `0 Error(s)`.
Frontend `ng lint` -> `All files pass linting.`

## D3 tests

Api.Integration 99, Application 19, Architecture 7, Domain 4 = **129 passed, 0 failed, 0 skipped**
(minTests 16, previous total 110, so 19 new). Frontend **47 Vitest tests passed** (minFrontendTests
6, previous total 32, so 15 new). `SAC_RETRIES=0`.

Tests naming each requirement, all at or above the two-per-requirement floor:

| REQ | tests |
|---|---|
| REQ-CAT-001 | 3 |
| REQ-CAT-002 | 2 |
| REQ-CAT-003 | 2 |
| REQ-CAT-004 | 2 |
| REQ-CAT-005 | 4 |
| REQ-CAT-006 | 2 |
| REQ-CAT-007 | 2 |
| REQ-CAT-008 | 2 |

## D4 schema

Migration `20260908015659_AddProductCatalog` created and applied.
`dotnet ef migrations has-pending-model-changes` -> "No changes have been made to the model since the
last migration."

All eight tables this phase declared are present: `DemoEnvironments`, `FaqItems`, `PlanFeatures`,
`PricingPlans`, `ProductCategories`, `ProductFeatures`, `Products`, `ProductScreenshots`. 35 tables
in total.

The rules that live in the database rather than only in code:

```
UX_PricingPlans_OneRecommendedPerProduct | filter=([IsRecommended]=(1))
CK_PlanFeatures_LimitValue | ([Availability]<>(1) OR [LimitValue] IS NOT NULL AND len([LimitValue])>(0))
CK_PricingPlans_Price | ([Price]>=(0) AND ([Price]>(0) OR [IsFreeTier]=(1)))
CK_PricingPlans_Seats | ([IncludedSeats]>(0))
```

## D5, D6 publish and health

`_publish/api/SoftwareManagement.Api.dll` 312,832 bytes. `HEALTH_STATUS=200`.

The gate runs against `SoftwareManagementDb_GateP05`, a database that did not exist before this run.
Every migration applied to an empty server and the seed ran from nothing: `TABLES=35`,
`SEEDED_CATEGORIES=4`.

## D7 deny by default

```
ANON /api/v1/admin/products => 401
ANON /api/v1/admin/product-categories => 401
ANON /api/v1/admin/pages => 401
ANON /api/v1/admin/media => 401
ANON_ROUTES_CHECKED=4
ANON_PUBLIC /api/v1/public/catalog/categories => 200
ANON_PUBLIC /api/v1/public/catalog/products => 200
```

The two public paths are inside the frozen allowlist prefix `/api/v1/public/**`, and they return
published products only: `REQ_CAT_001_A_new_product_is_stored_as_a_draft_and_stays_out_of_the_public_catalogue`
proves a draft is absent rather than forbidden.

Wrong-role refusal is proven by `REQ_CAT_001_Sales_reads_the_catalogue_but_cannot_author_it`
(200 on the read, 403 on both writes, and the database checked afterwards) and by
`REQ_CAT_008_Demo_credentials_are_returned_only_to_a_caller_holding_the_demo_permission`
(Auditor refused). See ADR-R04 for why the read is a 200 and not the 403 the phase-plan line
predicted.

## D8 round trip on this phase's own endpoints

```
PROBE_CREATE_STATUS=201
PROBE_DUPLICATE_STATUS=409
PROBE_DUPLICATE_BODY={"type":".../slug-taken","title":"Slug is taken","status":409,
  "detail":"That address is already in use. Try \"probe-product-p05gate-2\".",
  "code":"SLUG_TAKEN","suggestedSlug":"probe-product-p05gate-2"}
PLAN_A_STATUS=200 PLAN_B_STATUS=200
PLAN_RECOMMEND_MOVE_STATUS=200
PLAN_IMPLAUSIBLE_YEARLY_STATUS=422
```

Read back out of the database by SQL, not from the API's own answer:

```
PROBE_ROWS=1
RECOMMENDED_COUNT=1
RECOMMENDED_PLAN=Growth
PLAN_PRICE=3000.00 INR
```

That covers all five rows of the phase's exit criteria table: the probe persists (1), exactly one
plan is recommended after moving the flag (2), a duplicate slug is 409 `SLUG_TAKEN` with a usable
alternative (3), a yearly price above twelve monthly payments is 422 (4), and the Sales refusal (5)
as qualified by ADR-R04.

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE` after the smoke run; `PORT 4300 FREE` and `PORT 5199 FREE` after the
render proof.

## D10 frontend, proven against the real server-rendering process

`ng build --configuration production` exit 0. `INITIAL_FILES=4 INITIAL_KB=290.9 CEILING=500`,
`LAZY_CHUNKS=11 CEILING_EACH=250`, largest lazy chunk 50.4 kB -> `BUNDLE_BUDGETS=PASS`.

`dist/server/server.mjs` run against the live API on 5199:

```
NG_INDEX_STATUS=200
NG_APP_ROOT_COUNT=1
ROUTE /about => 200 marker='data-testid="about-page"' found=yes
ROUTE /services => 200 marker='data-testid="services-page"' found=yes
ROUTE /admin/login => 200
ROUTE /admin/products => 200
ROUTE /admin/products/new => 200
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=5 EXPECTED=5
RENDER_PROOF=PASS
```

Both routes this phase declared answer 200. They carry no server-rendered marker by design: the
admin area is `RenderMode.Client`, so its HTML arrives after hydration. The three declared selectors
are proved present in the shipped build instead, and exercised by the Vitest suite, which mounts the
real components:

```
SELECTOR app-product-list present in 2 built files
SELECTOR app-product-editor present in 2 built files
SELECTOR app-plan-editor present in 2 built files
```

## D11 anti-stub

```
SOURCE_FILES=109 GENERATED_MIGRATION_FILES=9
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=10 FE_IT=47 FE_EXPECT=81
D11 PASS
```

## D12 secret scan

`SECRET_SCAN_EXIT=1` (clean) over the staged index, with the same single documented pathspec
exclusion as P04 and no change to the pattern. The scan first flagged a literal
`Gate-Pass-$Nonce` in the new `scripts/phase-smoke.ps1`; the fix was to the script, which now
generates the gate password per run instead of carrying a literal, not to the detector. A canary line
was then staged and detected (`CANARY_SCAN_EXIT=0`) to prove the pattern still fires, and removed.

## D15 coverage

```
PKG SoftwareManagement.Api = 68.2% (2695/3954)
PKG SoftwareManagement.Infrastructure = 82.4% (2265/2748)
PKG SoftwareManagement.Domain = 83.3% (150/180)
PKG SoftwareManagement.Application = 95.5% (462/484)
LINE_COVERAGE=75.6% (5572/7366) FLOOR=70%
D15 PASS
```
