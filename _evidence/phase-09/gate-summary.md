# P09 gate evidence

Nonce p09b.

The two exceptions carried from P07 and P08 still hold: **tests ran in Debug** because Smart App
Control refuses this machine's freshly built binaries (BLK-1), and **nothing was published**, so
D5 and D6 are not evidenced and are not marked green.

## D1 restore

`dotnet restore` -> `All projects are up-to-date for restore.` `npm ci` clean.

## D2 build, zero warnings

`dotnet build backend/SoftwareManagement.sln -c Release -warnaserror` -> `Build succeeded.`
`0 Warning(s)` `0 Error(s)`; the same in Debug. `dotnet format --verify-no-changes` exit 0.
`ng lint` -> `All files pass linting.`

## D3 tests

```
TEST_CONFIGURATION=Debug
SAC_RETRIES=0
Api.IntegrationTests  251 passed, 0 failed, 0 skipped
Application.Tests      19 passed, 0 failed, 0 skipped
Architecture.Tests      7 passed, 0 failed, 0 skipped
Domain.Tests            4 passed, 0 failed, 0 skipped
```

**281 backend tests**, against P08's 235, so 46 new where `minTests` is 28. Frontend **128 Vitest
tests**, against 110, so 18 new where `minFrontendTests` is 8.

| REQ | tests | | REQ | tests |
|---|---|---|---|---|
| REQ-CUST-001 | 4 | | REQ-CUST-006 | 2 |
| REQ-CUST-002 | 2 | | REQ-CUST-007 | 2 |
| REQ-CUST-003 | 4 | | REQ-CUST-008 | 2 |
| REQ-CUST-004 | 5 | | REQ-SALE-001 | 4 |
| REQ-CUST-005 | 2 | | REQ-SALE-002 | 4 |
| REQ-SALE-003 | 5 | | REQ-SALE-005 | 3 |
| REQ-SALE-004 | 3 | | REQ-SALE-006 | 3 |

Counting them mattered again. The first pass had five requirements at nought or one, because the
tests had been written against what the code did rather than against the requirement list: the
contact tests were labelled REQ-CUST-002, which is the duplicate-GSTIN rule, and three requirements
had no endpoint at all. What was missing was built rather than relabelled — a delete that refuses
with the reason, a CSV export, and postal-code validation — and the refusal code for a duplicate
GSTIN was corrected from `GSTIN_TAKEN` to the `GSTIN_DUPLICATE` the requirement names.

## D4 schema

Migration `20260921014245_AddSalesQuotes` present in `__EFMigrationsHistory` of
`SoftwareManagementDb_GateP09b`, a database that did not exist before this run.
`has-pending-model-changes` -> "No changes have been made to the model since the last migration."
51 tables.

```
DBOBJECT Organisations = present     DBOBJECT QuoteLineItems = present
DBOBJECT Contacts = present          DBOBJECT NumberSequences = present
DBOBJECT Quotes = present
CHECK_CONSTRAINTS=3
UNIQUE_QUOTE_NUMBER=IX_Quotes_QuoteNumber
```

Three check constraints on the line items, because arithmetic impossibilities belong in the
database rather than in whichever write path remembers: a quantity above zero, a price at or above
zero, and a discount no larger than the line it discounts. The quote number is unique by index, so
a duplicate is refused by SQL Server and not merely by the application hoping.

## D5, D6 publish and health — NOT EVIDENCED

Unchanged. The runtime checks ran against the Debug build through the `dotnet` host, which the
smoke prints as `API_SOURCE`.

## D7 deny by default

The anonymous allowlist is unchanged from P08 and re-checked in the same pass: five admin routes
answer 401, three public ones answer 200, a draft product's public address answers 404.

Two role checks could not run in the gate database, which is seeded with the owner alone. Both are
printed rather than skipped silently, because a check that did not run must not read as one that
passed:

```
QUOTE_DISCOUNT_403_STATUS=not-checked (no Sales account in this gate database)
PIPELINE_EDITOR_LEADS_STATUS=not-checked (no editor account in this gate database)
```

Both are asserted by integration tests against a fixture that does have those accounts:
`REQ_SALE_004_A_sales_user_applying_a_twenty_percent_discount_is_refused_with_approval_required`
drives a Sales token and checks the 403, the `APPROVAL_REQUIRED` code, the threshold in the body,
and that no line was written; `NFR_AUTHZ_02_The_editor_is_refused_the_whole_of_quoting` and
`NFR_AUTHZ_02_The_editor_is_refused_the_customer_records` cover the editor.

## D8 round trip, and the phase's own exit criteria

| # | Expected | Observed |
|---|---|---|
| 1 | an organisation reaches the database | `ORG_CREATE_STATUS=201`, `ORG_ROWS=1` |
| 2 | a 14-character GSTIN -> 422 with the pattern | `ORG_GSTIN_SHORT_STATUS=422`, body carries `expectedPattern` |
| 3 | 20 quotes at once -> contiguous, no duplicates | `QUOTE_NUMBERS_DISTINCT=20 SPAN=20`, `Q/2026-27/00001` to `Q/2026-27/00020` |
| 4 | Sales applying 20% -> 403 `APPROVAL_REQUIRED` | asserted by test; see D7 |
| 5 | editing a sent quote -> 409 offering a revision | `QUOTE_EDIT_AFTER_SEND_STATUS=409`, detail says "Revise it" |
| 6 | the two-line rounding example | `QUOTE_GRAND_TOTAL=17696.46`, and the full example asserted by test |

Row 3 is the one worth reading twice. Twenty numbers, twenty distinct values, spanning exactly
twenty — no gap and no repeat. `MAX(number) + 1` cannot produce that under concurrency, which is
why the sequence is a row updated under `UPDLOCK, ROWLOCK` inside the caller's transaction: a quote
that fails to save takes its number back with it.

Row 6, in full, from `REQ_SALE_003_Tax_is_rounded_per_line_and_the_total_is_the_sum_of_the_rounded_lines`:
14,997.00 and 2,500.00 at 18 percent give 2,699.46 and 450.00 in tax and a grand total of
20,646.46 — the sum of the rounded lines, not 18 percent of 17,497.00 taken once at the end. A
second test pins the rounding mode: .NET rounds half to even by default, so 0.125 would become
0.12, and an invoice that disagrees with a hand calculator by a paisa is an invoice somebody
queries.

### The two bugs the first test run found, both mine

Adding a quote line produced totals exactly double what they should be. The line was being added to
the DbSet *and* to `quote.Lines`, and EF's change-tracker fixup had already put it in that
collection the moment its QuoteId matched a tracked quote — so the totals summed it twice.

Removing the explicit `Add` then broke it differently: an entity discovered only through a
navigation, with a key already set, is marked Modified rather than Added, so EF issued an UPDATE
for a row that did not exist and the save came back as a concurrency failure. Both reasons are
written into the code, because either fix looks correct in isolation.

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE`.

## D10 frontend

`ng build` exit 0. `INITIAL_FILES=4 INITIAL_KB=318.9 CEILING=500`, `LAZY_CHUNKS=25
CEILING_EACH=250` -> `BUNDLE_BUDGETS=PASS`. 128 Vitest tests, `ng lint` clean.

```
ROUTE /                 => 200 marker='Software that runs the business' found=yes bytes=18872
ROUTE /products         => 200 marker='Our software'                    found=yes bytes=15806
ROUTE /contact          => 200 marker='Talk to us'                      found=yes bytes=17853
ROUTE /admin/customers  => 200 marker='<app-root'                       found=yes bytes=2085
ROUTE /admin/quotes     => 200 marker='<app-root'                       found=yes bytes=2085
ROUTE /admin/quotes/... => 200 marker='<app-root'                       found=yes bytes=2085
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=6 EXPECTED=6 FAILED=0
```

The admin routes are 2 kB against the public routes' 15–19 kB for the reason given in P08: the
server renders none of the admin area, which is right for screens showing customer names, GSTINs
and prices. A signed-in browser pass was again not done, for the same reason.

Accessibility: axe over the customer list and the quote editor, clean of serious and critical
violations.

### A flaky accessibility check, fixed properly rather than re-run

As the suite grew past twenty spec files, four axe tests began timing out at Vitest's five-second
default — on a machine that was merely busy, not on markup that was wrong. That reads as a failing
accessibility check when nothing is wrong, which is the worst kind of false alarm: the one people
learn to re-run rather than read. Every axe test now carries an explicit `AxeTimeout`, declared
beside the helper with the reason.

## D11 anti-stub

```
SOURCE_FILES=202 GENERATED_MIGRATION_FILES=17
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=20 FE_IT=128 FE_EXPECT=227
D11 PASS
```

## D15 coverage

```
PKG SoftwareManagement.Infrastructure = 76.3% (6680/8757)
PKG SoftwareManagement.Api            = 84.8% (5640/6651)
PKG SoftwareManagement.Domain         = 89%   (580/652)
PKG SoftwareManagement.Application    = 94%   (750/798)
LINE_COVERAGE=81% (13650/16858) FLOOR=70%
D15 PASS
```

81% against P08's 80.2%, on the same basis.

## D12, D13, D14

Recorded in the closing commit.
