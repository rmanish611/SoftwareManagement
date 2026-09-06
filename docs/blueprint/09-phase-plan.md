# 09 Phase Plan

## P01: Repo & Guardrails
Goal (1 line): Establish repository structure, CI skeleton, and protocol enforcement files.
REQ IDs delivered: None
NFR IDs addressed: NFR-MAINT-01, NFR-MAINT-02, NFR-ACC-01
Depends on: Preflight
Deliverables: README, ci.yml, EXCEPTIONS.md, BACKLOG.md, PROGRESS-INDEX.md
RAM note: Minimal footprint.
UI: none — infrastructure
dodApplicable: D1
acceptanceCriteria: none
minTests: 0
minFrontendTests: 0
phaseRoutes: []
phaseSelectors: []
dbObjects: []
smoke: { }
Exit criteria table: 
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `git ls-files` | Contains .github/workflows/ci.yml | Infrastructure setup |
Rollback plan if this phase fails: Reset hard to commit before P01.

## P02: Walking Skeleton
Goal (1 line): Setup ASP.NET API, Angular Workspace, and EF Core DbContext with /health and shell component.
REQ IDs delivered: None
NFR IDs addressed: NFR-PERF-01
Depends on: P01
Deliverables: .NET Solution, Angular Workspace, DbContext
RAM note: Sequential build required.
UI: yes
dodApplicable: D1, D2, D3, D5, D6, D7, D15
acceptanceCriteria: none
minTests: 2
minFrontendTests: 2
phaseRoutes: ["/"]
phaseSelectors: ["app-root"]
dbObjects: ["__EFMigrationsHistory"]
smoke: { protectedPath: "/api/protected", listPath: "/", createPath: "", probeCreateBody: "", probeTable: "", probeColumn: "", deepRoute: "", seedEmail: "admin@example.com", lowPrivEmail: "user@example.com" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `curl -s http://localhost:5000/api/health` | 200 OK | API is running |
| 2 | `curl -s -I http://localhost:5000/api/protected` | 401 Unauthorized | NFR-AUTH-01 Default Deny |
Rollback plan if this phase fails: `git clean -fdx` and hard reset.

## P03: Master Data & Catalog (Backend)
Goal (1 line): Implement EF Core entities and API endpoints for Products and Variants.
REQ IDs delivered: REQ-CAT-001, REQ-CAT-002
NFR IDs addressed: NFR-MAINT-01
Depends on: P02
Deliverables: Product API Controllers, EF Migrations
RAM note: Standard API memory.
UI: none — API only
dodApplicable: D1, D2, D4, D5, D6, D7, D8, D13, D15
acceptanceCriteria:
  [PERSIST] REQ-CAT-001 / BR-PRD-01: Admin POST /api/products saves row in Products table with IsDraft=true.
  [REJECT] REQ-CAT-001: POST /api/products without auth returns 401.
minTests: 4
minFrontendTests: 0
phaseRoutes: []
phaseSelectors: []
dbObjects: ["Products", "ProductVariants"]
smoke: { protectedPath: "/api/products", listPath: "/api/products/published", createPath: "/api/products", probeCreateBody: '{"name":"__PROBE__","isDraft":true}', probeTable: "Products", probeColumn: "Name", deepRoute: "", seedEmail: "admin@example.com", lowPrivEmail: "user@example.com" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `Invoke-Sql -Query "SELECT Name FROM Products WHERE Name='__PROBE__'"` | `__PROBE__` | REQ-CAT-001 Persist |
Rollback plan if this phase fails: Revert EF migration, drop tables, reset git.

## P04: Public Storefront (UI)
Goal (1 line): Build the Angular public homepage listing published products.
REQ IDs delivered: REQ-CAT-004, REQ-CAT-005
NFR IDs addressed: NFR-UI-01, NFR-ACC-01, NFR-PERF-03
Depends on: P03
Deliverables: Angular Product List, HTTP Interceptors
RAM note: Limit Node memory during build.
UI: yes
dodApplicable: D1, D2, D9, D11, D12, D14, D15
acceptanceCriteria:
  [UI] REQ-CAT-004: Navigating to `/` displays the product title.
  [REJECT] REQ-CAT-005: Navigating to drafted product returns 404 in UI.
minTests: 4
minFrontendTests: 4
phaseRoutes: ["/", "/product/:id"]
phaseSelectors: ["app-product-list", "app-product-detail"]
dbObjects: []
smoke: { protectedPath: "", listPath: "/", createPath: "", probeCreateBody: "", probeTable: "", probeColumn: "", deepRoute: "/product/1", seedEmail: "", lowPrivEmail: "" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `ng test --watch=false --browsers=ChromeHeadless` | SUCCESS | UI logic works |
| 2 | `npx @axe-core/cli http://localhost:4200/` | 0 violations | NFR-ACC-01 |
Rollback plan if this phase fails: Hard reset.

## P05: Checkout & Webhooks (Paddle Integration)
Goal (1 line): Integrate Paddle.js for checkout and process Paddle webhooks.
REQ IDs delivered: REQ-PAY-001, REQ-PAY-002, REQ-PAY-003, REQ-PAY-004
NFR IDs addressed: NFR-SEC-02
Depends on: P04
Deliverables: Paddle Checkout UI, Webhook API Controller, Signature Validation
RAM note: Standard.
UI: yes
dodApplicable: D1, D2, D4, D5, D6, D7, D8, D13, D15
acceptanceCriteria:
  [PERSIST] REQ-PAY-002: Valid webhook inserts into PaymentEvents.
  [REJECT] REQ-PAY-003: Invalid webhook signature returns 401.
  [BR] REQ-PAY-004 / BR-ORD-01: Valid `payment_succeeded` inserts into Orders with Status='Paid'.
minTests: 8
minFrontendTests: 2
phaseRoutes: ["/checkout"]
phaseSelectors: ["app-checkout-button"]
dbObjects: ["Orders", "PaymentEvents"]
smoke: { protectedPath: "/api/webhooks/paddle", listPath: "", createPath: "/api/webhooks/paddle", probeCreateBody: '{"event":"payment_succeeded","transaction_id":"__PROBE__"}', probeTable: "Orders", probeColumn: "PaddleTransactionId", deepRoute: "", seedEmail: "admin@example.com", lowPrivEmail: "" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `Invoke-Sql -Query "SELECT Status FROM Orders WHERE PaddleTransactionId='__PROBE__'"` | `Paid` | REQ-PAY-004 |
| 2 | `curl -X POST -H "Paddle-Signature: invalid" /api/webhooks/paddle` | 401 Unauthorized | NFR-SEC-02 |
Rollback plan if this phase fails: Hard reset.

## P06: License & Fulfillment Logic (Backend)
Goal (1 line): Implement LicenseKey generation and DownloadLink generation on successful orders.
REQ IDs delivered: REQ-LIC-001, REQ-FUL-001
NFR IDs addressed: -
Depends on: P05
Deliverables: LicenseService, FulfillmentService
RAM note: Standard.
UI: none — backend logic only
dodApplicable: D1, D4, D5, D6, D13, D15
acceptanceCriteria:
  [PERSIST] REQ-LIC-001 / BR-LIC-01: Order transition to Paid creates LicenseKey row.
  [PERSIST] REQ-FUL-001 / BR-DL-01: Order transition to Paid creates DownloadLink row.
minTests: 4
minFrontendTests: 0
phaseRoutes: []
phaseSelectors: []
dbObjects: ["LicenseKeys", "DownloadLinks"]
smoke: { protectedPath: "", listPath: "", createPath: "", probeCreateBody: "", probeTable: "LicenseKeys", probeColumn: "KeyValue", deepRoute: "", seedEmail: "", lowPrivEmail: "" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `dotnet test --filter "Category=Fulfillment"` | Passed | REQ-LIC-001 |
Rollback plan if this phase fails: Hard reset.

## P07: Customer Portal (UI)
Goal (1 line): Allow authenticated customers to view their purchases, license keys, and download assets.
REQ IDs delivered: REQ-LIC-002, REQ-FUL-002, REQ-FUL-003
NFR IDs addressed: NFR-PERF-04
Depends on: P06
Deliverables: Portal Angular Module, Auth Interceptor, API endpoints for Customer.
RAM note: Standard.
UI: yes
dodApplicable: D1, D2, D9, D11, D12, D14, D15
acceptanceCriteria:
  [UI] REQ-LIC-002: Customer sees "Your Licenses" on `/portal/licenses`.
  [REJECT] REQ-FUL-003 / BR-DL-01: Expired download link returns 403.
minTests: 6
minFrontendTests: 6
phaseRoutes: ["/portal", "/portal/licenses"]
phaseSelectors: ["app-portal-layout", "app-license-list"]
dbObjects: []
smoke: { protectedPath: "/api/portal/licenses", listPath: "/api/portal/licenses", createPath: "", probeCreateBody: "", probeTable: "", probeColumn: "", deepRoute: "/portal/licenses", seedEmail: "", lowPrivEmail: "user@example.com" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `ng build` size check | Lazy chunk < 150kb | NFR-PERF-04 |
Rollback plan if this phase fails: Hard reset.

## P08: License Validation API
Goal (1 line): Public API for external software clients to validate keys and record activations.
REQ IDs delivered: REQ-LIC-003, REQ-LIC-004, REQ-LIC-005
NFR IDs addressed: NFR-CONC-02, NFR-SEC-04
Depends on: P06
Deliverables: Validation Controller, Rate Limiting configuration.
RAM note: Standard.
UI: none
dodApplicable: D1, D4, D5, D6, D7, D13, D15
acceptanceCriteria:
  [PERSIST] REQ-LIC-005 / BR-LIC-02: Valid activation inserts into LicenseActivations.
  [REJECT] REQ-LIC-004 / BR-LIC-03: Expired key returns 403 Forbidden.
minTests: 6
minFrontendTests: 0
phaseRoutes: []
phaseSelectors: []
dbObjects: ["LicenseActivations"]
smoke: { protectedPath: "", listPath: "", createPath: "/api/licenses/validate", probeCreateBody: '{"key":"__PROBE__"}', probeTable: "LicenseActivations", probeColumn: "MachineId", deepRoute: "", seedEmail: "", lowPrivEmail: "" }
Exit criteria table:
| # | Command | Expected result | Proves REQ/NFR |
|---|---|---|---|
| 1 | `curl -X POST /api/licenses/validate -d '{"key":"expired"}'` | 403 Forbidden | REQ-LIC-004 |
| 2 | `Artillery test` | Rate limit blocks after 10req/min | NFR-SEC-04 |
Rollback plan if this phase fails: Hard reset.

*(P09 to P12 omitted for brevity in draft, will encompass Subscriptions and Admin panel)*
