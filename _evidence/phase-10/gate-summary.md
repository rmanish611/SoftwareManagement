# P10 gate evidence

Nonce p10b.

The two exceptions carried from P07 still hold: **tests ran in Debug** because Smart App Control
refuses this machine's freshly built binaries (BLK-1), and **nothing was published**, so D5 and D6
are not evidenced and are not marked green.

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
Api.IntegrationTests  283 passed, 0 failed, 0 skipped
Application.Tests      19 passed, 0 failed, 0 skipped
Architecture.Tests      7 passed, 0 failed, 0 skipped
Domain.Tests            4 passed, 0 failed, 0 skipped
```

**313 backend tests**, against P09's 281, so 32 new where `minTests` is 20. Frontend **148 Vitest
tests**, against 128, so 20 new where `minFrontendTests` is 6.

| REQ | tests | | REQ | tests |
|---|---|---|---|---|
| REQ-SALE-007 | 3 | | REQ-SALE-012 | 4 |
| REQ-SALE-008 | 2 | | REQ-SALE-013 | 3 |
| REQ-SALE-009 | 2 | | REQ-SALE-014 | 2 |
| REQ-SALE-010 | 2 | | REQ-SALE-015 | 4 |
| REQ-SALE-011 | 4 | | REQ-SALE-016 | 2 |

### Five tests this phase broke, and why they were right to break

The whole suite failed five lead tests that passed on their own. P08 changed the inbox ordering from
"newest first" to "unanswered first, oldest deadline before newest", and four P07 tests were reading
the default twenty-five rows and expecting to find a lead they had just submitted. With P10's
backdated fixtures in the same database, that lead sat below the twenty-five oldest unanswered.

Four of them now ask for their lead by name, which is the lesson P06 already recorded about the
catalogue. The fifth was worse: `..._The_newest_enquiry_is_the_first_one_the_owner_sees` was
asserting the ordering P08 deliberately replaced. It is rewritten to assert what the inbox is for,
with the reason in the test.

## D4 schema

Migration `20260921102134_AddBilling` present in `__EFMigrationsHistory` of
`SoftwareManagementDb_GateP10b`, a database that did not exist before this run.
`has-pending-model-changes` -> "No changes have been made to the model since the last migration."
56 tables.

```
DBOBJECT Tenants = present            DBOBJECT Invoices = present
DBOBJECT Subscriptions = present      DBOBJECT Payments = present
DBOBJECT SubscriptionEvents = present
CHECK_CONSTRAINTS=4
UNIQUE_INVOICE_NUMBER=IX_Invoices_InvoiceNumber
UNIQUE_PAYMENT_REF=IX_Payments_InvoiceId_ReferenceNumber
```

Four check constraints and two unique indexes, because the rules that cost money belong in the
database: seats above zero, a price at or above zero, a payment above zero, and `AmountPaid`
between nothing and the total. The unique index on `(InvoiceId, ReferenceNumber)` is what stops two
people reconciling the same bank statement from entering one transfer twice (EX-226).

**No `InvoiceLineItems` table.** The frozen data model has no such entity and the ERD draws
`Subscription ||--o{ Invoice` with no line between them, so an invoice for a subscription is a
single computed amount: seats times unit price, with the tax rounded the same way a quote line is
(ASM-21).

## D5, D6 publish and health — NOT EVIDENCED

Unchanged. The runtime checks ran against the Debug build through the `dotnet` host, which the
smoke prints as `API_SOURCE`.

## D7 deny by default

The anonymous allowlist is unchanged and re-checked in the same pass: five admin routes answer 401,
three public ones answer 200, a draft product's public address answers 404.

The two role checks that need accounts the gate database does not have are printed rather than
skipped:

```
QUOTE_DISCOUNT_403_STATUS=not-checked (no Sales account in this gate database)
PIPELINE_EDITOR_LEADS_STATUS=not-checked (no editor account in this gate database)
```

This phase's own authorization is asserted by `NFR_AUTHZ_02_The_editor_is_refused_the_whole_of_billing`,
which drives an editor token at the subscription list, one subscription, the invoice register and
an invoice issue, and gets 403 from each.

## D8 round trip, and the phase's own exit criteria

| # | Expected | Observed |
|---|---|---|
| 1 | a tenant reaches `Tenants` | `TENANT_CREATE_STATUS=201`, `TENANT_ROWS=1` |
| 2 | accepting the same quote twice gives one subscription | `SAME_SUBSCRIPTION=True`, `SUBSCRIPTION_ROWS=1` |
| 3 | overpaying an invoice -> 422 `OVERPAYMENT` | `PAYMENT_OVERPAY_STATUS=422`, body carries `outstanding: 7080.00` |
| 4 | 8 then 22 days late -> PastDue then Suspended with events | asserted by test; see below |
| 5 | two schedulers, one dunning email | asserted by two tests; see below |
| 6 | proration, 20 of 30 days | asserted by test: 1333.33 |

Also proved at runtime: the invoice number matched `INV/<FY>/<00001>`, the total was 7080.00 — two
seats at 3,000 plus eighteen percent, rounded as a quote line is — a part payment left 6,080.00
outstanding, and the same bank reference twice was refused with 409 leaving one payment row.

Rows 4, 5 and 6 are asserted by tests rather than by the smoke because each needs the calendar moved
or two schedulers started, and a clock a smoke script can set is not the clock the sweep uses:

- **Row 4**: `REQ_SALE_013_An_invoice_eight_days_late_moves_the_subscription_to_past_due` and
  `..._The_same_invoice_at_twenty_two_days_suspends_the_subscription`. The second checks that both
  steps left an event, so the history explains itself. A third test checks the way back: paying in
  full returns the subscription to Active, because leaving a paying customer past due is how one
  gets suspended for nothing.
- **Row 5**: two tests, because the guarantee and the mechanism are different claims.
  `NFR_DEP_05_Two_schedulers_against_one_database_do_the_work_once` runs two sweeps together and
  asserts one escalation and one queued email. `NFR_DEP_05_A_sweep_declines_while_another_holds_the_lock`
  holds the lock from the test and asserts the sweep does nothing at all.

  The first of those was written wrongly at first: it asserted that only one sweep *took* the lock.
  They do not race that way — the second waits, gets the lock, and finds the work done. What the
  requirement promises is that nothing is double-sent, and that is what is asserted now.
- **Row 6**: `REQ_SALE_015_The_proration_formula_is_the_one_the_requirement_states`. Twenty days of
  thirty, 3,000 to 5,000: 3333.33 owed for the new plan less 2000.00 already paid, so 1333.33. Each
  side is rounded before subtracting, so the charge is the difference between two figures that
  could each appear on an invoice.

## D9 clean shutdown

`API STOPPED, PORT 5199 FREE`.

## D10 frontend

`ng build` exit 0. `INITIAL_FILES=4 INITIAL_KB=319.4 CEILING=500`, `LAZY_CHUNKS=29
CEILING_EACH=250` -> `BUNDLE_BUDGETS=PASS`. 148 Vitest tests, `ng lint` clean.

```
ROUTE /                    => 200 marker='Software that runs the business' found=yes bytes=18872
ROUTE /products            => 200 marker='Our software'                    found=yes bytes=15806
ROUTE /contact             => 200 marker='Talk to us'                      found=yes bytes=17853
ROUTE /admin/subscriptions => 200 marker='<app-root'                       found=yes bytes=2085
ROUTE /admin/invoices      => 200 marker='<app-root'                       found=yes bytes=2085
ROUTE /admin/invoices/...  => 200 marker='<app-root'                       found=yes bytes=2085
NG_MISSING_ASSET_STATUS=404
ROUTES_CHECKED=6 EXPECTED=6 FAILED=0
```

The admin routes are 2 kB against the public routes' 15–19 kB for the reason given in P08: the
server renders none of the admin area, which is right for screens showing customer names, GSTINs
and amounts owed. A signed-in browser pass was again not done.

Accessibility: axe over the subscription list and the invoice detail, clean of serious and critical
violations. Everything the colour says, the words say too: "PastDue", "14d late", "settled".

## D11 anti-stub

```
SOURCE_FILES=219 GENERATED_MIGRATION_FILES=19
STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0 APPROVED_EXCEPTIONS=0
SPEC_FILES=22 FE_IT=148 FE_EXPECT=259
D11 PASS
```

## D15 coverage

```
PKG SoftwareManagement.Infrastructure = 77.5% (7838/10117)
PKG SoftwareManagement.Api            = 83.4% (6166/7389)
PKG SoftwareManagement.Domain         = 89.2% (660/740)
PKG SoftwareManagement.Application    = 94.1% (804/854)
LINE_COVERAGE=81% (15468/19100) FLOOR=70%
D15 PASS
```

81%, level with P09 on the same basis, over 3,600 more lines of production code.

## D12, D13, D14

Recorded in the closing commit.
