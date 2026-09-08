# P02 gate evidence

Nonce 384a5d01.

## D1 restore
`dotnet restore` and `npm ci` both exit 0 (npm install performed by `ng new`, lockfile committed).

## D2 build
`dotnet build SoftwareManagement.sln -c Release --no-restore -warnaserror -m:1 -nodeReuse:false`
-> `Build succeeded.` `0 Warning(s)` `0 Error(s)`

## D3 tests
Domain 4, Application 19, Architecture 7, Api.Integration 17 = 47 passed, 0 failed, 0 skipped
(minTests 6). Frontend: 6 Vitest tests passed (minFrontendTests 2).
Integration-category tests that need SQL Server: 4, run locally, filtered out in CI (NFR-MAINT-04).

## D5 publish
`_publish/api/SoftwareManagement.Api.dll` 43,520 bytes, exit 0.

## D6 health
ATTEMPT 1 STATUS=200, HEALTH_BODY=Healthy, LIVE=200, READY=200.

## D7 (not in this phase's frozen set, measured anyway)
ANON_SECURE=401 ANON_PUBLIC=200 BAD_TOKEN=401 UNKNOWN_ROUTE=404.

## D9 clean shutdown
`API STOPPED CLEAN, PORT 5199 FREE`; static server `STATIC SERVER STOPPED, PORT 4300 FREE`.

## D10 frontend
`ng build --configuration production` exit 0.
NG_INDEX_STATUS=200, `<app-root` present, prerendered `data-testid=app-shell` and the home
heading present in the served HTML (server-side rendering is working, NFR-SEO-01).
NG_MAIN_STATUS_BYTES=200 103473. NG_MISSING_ASSET_STATUS=404 (the SPA fallback is a real
fallback). Selectors app-root and app-shell both present in the built bundle.
Bundle ceilings: INITIAL_KB=244 against a 500 kB ceiling, LAZY_CHUNKS=0 -> BUNDLE_BUDGETS=PASS.
Lint: `All files pass linting.`

## D11 anti-stub
SOURCE_FILES=38 STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 WEAK_FE_SPECS=0
APPROVED_EXCEPTIONS=0 -> D11 PASS. EXC-01 documents EF Core's own `#pragma` inside generated
migration files, which the scan excludes as generated output.

## D12 push
LOCAL_HEAD=0655d9f78585c586f243675f10fcf27b2f53952c == REMOTE_HEAD, divergence `0 0`,
tag phase-02-gate on the remote, working tree clean.

## D13 backup
src-phase-02 zip 448,179 bytes, 173 entries, sha256 675ED1EC..., no build output inside.
Database backup 3,526,656 bytes with RESTORE VERIFYONLY PASS. Bundle verify: okay.

## D15 coverage
Domain 100%, Application 96.6%, Api 90%, Infrastructure 63.6% -> merged 79.8% against the
70% floor. Generated migrations are excluded from instrumentation and verified by the database
gate instead (ASM-6).
