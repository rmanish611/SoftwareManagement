# P04 gate evidence

Nonce c41f0a72.

## D2 build
`0 Warning(s)` `0 Error(s)` with `-warnaserror`.

## D3 tests
Api.Integration 80, Application 19, Architecture 7, Domain 4 = 110 passed, 0 failed, 0 skipped
(minTests 24, previous total 80). Frontend 32 Vitest tests passed (minFrontendTests 8).
Every requirement has at least two tests naming it: REQ-SITE-001=3 002=2 003=3 004=2 005=2 006=3
007=2 008=3 009=3 010=2 011=2 012=3. SAC_RETRIES=0.

## D4 schema
`AddSiteContent` applied. 27 tables; the 13 this phase declared are all present: Pages,
PageSections, NavigationItems, Services, Technologies, ServiceTechnologies, TeamMembers,
Testimonials, MediaAssets, SeoMetadata, Redirects, ContentVersions, Announcements.
`has-pending-model-changes` -> "No changes have been made to the model since the last migration."

## D5, D6 publish and health
`_publish/api/SoftwareManagement.Api.dll` 216,576 bytes. `/health` 200 on the second attempt.

## D7 deny by default
ANON /api/v1/admin/pages, /media, /navigation, /services => all 401.
ANON_ROUTES_CHECKED=4 FAILURES=0. Wrong-role refusal proven by
`REQ_SITE_003_Returns403_WhenSalesTriesToPublish`.

## D8 round trip on this phase's own endpoints
POST /api/v1/admin/pages -> 201. SQL `SELECT COUNT(*) FROM dbo.Pages WHERE Title='PROBE-p04gate'`
-> 1. Publish -> 204. GET /api/v1/public/pages/probe-p04-gate -> 200 containing the probe title.

## D9 clean shutdown
`API STOPPED, PORT 5199 FREE` and `SSR STOPPED, PORT 4300 FREE` after every run.

## D10 frontend, proven against the real server-rendering process
`ng build --configuration production` exit 0, lint clean, initial total 295.49 kB (transfer 84.37 kB)
against the 500 kB ceiling, largest lazy chunk well under 250 kB -> BUNDLE_BUDGETS=PASS.

The render proof now runs `dist/server/server.mjs`, the process production runs, against the live
API on 5199:
- NG_INDEX_STATUS=200, one `<app-root`, and the home copy present in the server-rendered HTML
- /about 200 with `data-testid="about-page"` in the HTML, /services 200 with its own marker
- /admin/login 200
- NG_MISSING_ASSET_STATUS=404, NG_MISSING_DEEP_ROUTE=200 (a real route renders, a missing file does not)

## D11 anti-stub
SOURCE_FILES=95 STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0
APPROVED_EXCEPTIONS=0 -> D11 PASS.

## D12 secret scan
SECRET_SCAN_EXIT=1 (clean) after excluding exactly one file by pathspec,
`Identity/LoginAttempt.cs`, whose `WrongPassword = "wrong_password"` constant is an audit reason
code. The exclusion is documented in `docs/EXCEPTIONS.md` and the pattern was not relaxed; a canary
line was staged to prove the detector still fires (CANARY_DETECTED=yes).

## D15 coverage
Application 95.5%, Domain 82.1%, Infrastructure 81.9%, Api 66.8% -> merged 76.2% against the
70% floor, up from 75.2% in P03.
